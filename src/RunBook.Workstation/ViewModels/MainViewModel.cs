using RunBook.Workstation.Models;
using RunBook.Workstation.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace RunBook.Workstation.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly RunBookWorkstationApiClient _api = new RunBookWorkstationApiClient();
        private readonly WorkstationConnectionStatusService _connectionStatusService = new WorkstationConnectionStatusService();
        private readonly DispatcherTimer _sessionTimer;
        private readonly DispatcherTimer _syncTimer;
        private static readonly TimeSpan AuthRefreshInterval = TimeSpan.FromMinutes(5);

        private WorkstationSettings _settings;
        private WorkstationSessionSnapshot? _session;
        private WorkstationRegistrationSnapshot? _registration;
        private WorkstationTimeclockSnapshot? _timeclockSnapshot;
        private WorkstationEmployeeAuthCache? _authCache;
        private WorkstationModuleCard? _selectedModule;
        private WorkstationRosterEmployee? _selectedRosterEmployee;
        private WorkstationWorkOrderSummary? _selectedWorkOrder;
        private WorkstationWorkOrderDetail? _workOrderDetail;
        private WorkstationWorkOrderOperationSummary? _selectedOperation;
        private WorkstationDrawingReference? _selectedDrawing;
        private WorkstationInspectionTaskPackage? _inspectionPackage;
        private WorkstationInspectionTask? _selectedInspectionTask;
        private WorkstationActiveContext? _activeContext;
        private WorkstationCurrentJobContext? _currentJob;
        private WorkstationCurrentJobContext? _recentJob;
        private WorkstationShopAwareness _shopAwareness = new WorkstationShopAwareness();
        private string _statusText = "Workstation ready.";
        private string _sessionCountdown = "Signed out";
        private string _passcode = "";
        private string _timeClockStatus = "No punches yet.";
        private string _offlineStatus = "Offline status unknown";
        private string _authCacheStatus = "No Desktop employee auth cache loaded.";
        private bool _isBusy;
        private DateTime _lastAuthRefreshAttemptUtc = DateTime.MinValue;
        private string _settingsBaseUrl = "";
        private string _settingsDesktopBaseUrl = "";
        private string _settingsShopId = "";
        private string _settingsShopName = "";
        private string _settingsWorkstationName = "";
        private string _settingsPairingCode = "";
        private string _settingsControlEmail = "";
        private string _settingsControlPassword = "";
        private string _controlSessionStatus = "Not signed in";
        private string _currentShiftStatus = "CLOCKED OUT";
        private string _currentShiftDetail = "No shift loaded yet.";
        private string _pendingSyncSummary = "No pending sync items.";
        private string _timeOffType = "VACATION";
        private string _timeOffStartDate = "";
        private string _timeOffEndDate = "";
        private string _timeOffHoursText = "";
        private string _timeOffNote = "";
        private bool _isPasscodeDialogOpen;
        private string _workOrdersStatus = "Work orders will load when this module opens.";
        private const int MaxProductionQuantityValue = 1000000;
        private const int MaxWorkstationNoteLength = 1000;
        private const int MaxInspectionActualValueLength = 256;

        private string _quantityReportText = "";
        private string _scrapReportText = "";
        private string _operationNoteText = "";
        private string _operationActionNoteText = "";
        private string _inspectionActualValue = "";
        private string _inspectionResultNoteText = "";
        private int _assignedWorkOrderCount;
        private int _backupWorkOrderCount;
        private DateTime? _lastDesktopSuccessUtc;
        private DateTime? _lastDesktopFailureUtc;
        private string _lastDesktopFailureReason = "";

        public MainViewModel()
        {
            _settings = WorkstationStorageService.LoadSettings();
            _registration = WorkstationStorageService.LoadRegistration();
            _session = WorkstationStorageService.LoadSession();
            _timeclockSnapshot = WorkstationStorageService.LoadTimeclockState();
            _authCache = WorkstationStorageService.LoadAuthCache();
            _settingsBaseUrl = _settings.ControlBaseUrl;
            _settingsDesktopBaseUrl = _settings.DesktopBaseUrl;
            _settingsShopId = _settings.ShopId;
            _settingsShopName = _settings.ShopName;
            _settingsWorkstationName = _settings.WorkstationName;
            _settingsPairingCode = _settings.PairingCode;
            _controlSessionStatus = ControlSessionService.GetStatusLabel();

            LoginCommand = new RelayCommand(async () => await LoginAsync(), () => CanConfirmPasscode);
            LogoutCommand = new RelayCommand(Logout, () => CurrentSession != null && !IsBusy);
            OpenSettingsCommand = new RelayCommand(() => StatusText = "Settings stay available from the left rail.", () => !IsBusy);
            SaveSettingsCommand = new RelayCommand(SaveSettings, () => !IsBusy);
            ConnectControlCommand = new RelayCommand(async () => await ConnectControlAsync(), () => !IsBusy);
            DisconnectControlCommand = new RelayCommand(DisconnectControl, () => !IsBusy);
            RefreshRegistrationCommand = new RelayCommand(async () => await RefreshRegistrationAsync(), () => !IsBusy);
            RefreshTimeClockCommand = new RelayCommand(async () => await RefreshTimeClockAsync(), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            ClockInCommand = new RelayCommand(async () => await SubmitPunchAsync("clock_in", ""), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            ClockOutCommand = new RelayCommand(async () => await SubmitPunchAsync("clock_out", ""), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            BreakStartCommand = new RelayCommand(async () => await SubmitPunchAsync("break_start", ""), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            BreakEndCommand = new RelayCommand(async () => await SubmitPunchAsync("break_end", ""), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            LunchStartCommand = new RelayCommand(async () => await SubmitPunchAsync("lunch_start", ""), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            LunchEndCommand = new RelayCommand(async () => await SubmitPunchAsync("lunch_end", ""), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            SyncPendingCommand = new RelayCommand(async () => await SyncPendingQueueAsync(true), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            SubmitTimeOffCommand = new RelayCommand(async () => await SubmitTimeOffAsync(), () => CurrentSession != null && HasTimeClockAccess && !IsBusy);
            SelectModuleCommand = new RelayCommand<WorkstationModuleCard>(SelectModule);
            SelectModuleKeyCommand = new RelayCommand<string>(SelectModuleByKey, key => !string.IsNullOrWhiteSpace(key) && CurrentSession != null && !IsBusy);
            RefreshWorkOrdersCommand = new RelayCommand(async () => await RefreshWorkOrdersAsync(true), () => CurrentSession != null && HasWorkOrdersAccess && !IsBusy);
            ResumeCurrentJobCommand = new RelayCommand(async () => await ResumeCurrentJobAsync(), () => CurrentSession != null && HasWorkOrdersAccess && CurrentJob != null && !IsBusy);
            ResumeRecentJobCommand = new RelayCommand(async () => await ResumeRecentJobAsync(), () => CurrentSession != null && HasWorkOrdersAccess && !HasCurrentJob && RecentJob != null && !IsBusy);
            SelectWorkOrderCommand = new RelayCommand<WorkstationWorkOrderSummary>(async workOrder => await SelectWorkOrderAsync(workOrder), workOrder => workOrder != null && CurrentSession != null && HasWorkOrdersAccess && !IsBusy);
            OpenDrawingCommand = new RelayCommand<WorkstationDrawingReference>(async drawing => await OpenDrawingAsync(drawing), drawing => drawing != null && CurrentSession != null && HasDrawingsAccess && !IsBusy);
            SelectOperationCommand = new RelayCommand<WorkstationWorkOrderOperationSummary>(async operation => await SelectOperationAsync(operation), operation => operation != null && CurrentSession != null && HasWorkOrdersAccess && !IsBusy);
            StartOperationCommand = new RelayCommand(async () => await ExecuteOperationAsync("start"), () => CurrentSession != null && HasOperationExecutionAccess && SelectedOperation?.CanStart == true && !IsBusy);
            StopOperationCommand = new RelayCommand(async () => await ExecuteOperationAsync("stop"), () => CurrentSession != null && HasOperationExecutionAccess && SelectedOperation?.CanStop == true && !IsBusy);
            CompleteOperationCommand = new RelayCommand(async () => await ExecuteOperationAsync("complete"), () => CurrentSession != null && HasOperationExecutionAccess && SelectedOperation?.CanComplete == true && !IsBusy);
            SubmitQuantityCommand = new RelayCommand(async () => await SubmitQuantityAsync(), () => CurrentSession != null && HasProductionQuantityAccess && SelectedOperation != null && !IsBusy);
            SubmitScrapCommand = new RelayCommand(async () => await SubmitScrapAsync(), () => CurrentSession != null && HasProductionScrapAccess && SelectedOperation != null && !IsBusy);
            SubmitOperationNoteCommand = new RelayCommand(async () => await SubmitOperationNoteAsync(), () => CurrentSession != null && HasProductionNoteAccess && SelectedOperation != null && !IsBusy);
            SubmitOperationHelpCommand = new RelayCommand(async () => await SubmitOperationHelpAsync(), () => CurrentSession != null && HasWorkOrdersAccess && SelectedOperation != null && !IsBusy);
            RefreshInspectionTasksCommand = new RelayCommand(async () => await RefreshInspectionTasksAsync(false), () => CurrentSession != null && HasInspectionViewAccess && SelectedWorkOrder != null && !IsBusy);
            SelectInspectionTaskCommand = new RelayCommand<WorkstationInspectionTask>(SelectInspectionTask, task => task != null && !IsBusy);
            SubmitInspectionResultCommand = new RelayCommand(async () => await SubmitInspectionResultAsync(), () => CurrentSession != null && HasInspectionEntryAccess && SelectedInspectionTask != null && SelectedWorkOrder != null && !IsBusy);
            OpenRosterEmployeeCommand = new RelayCommand<WorkstationRosterEmployee>(OpenRosterEmployee, employee => employee != null && !IsBusy);
            AppendPasscodeDigitCommand = new RelayCommand<string>(AppendPasscodeDigit, digit => !IsBusy && IsPasscodeDialogOpen && !string.IsNullOrWhiteSpace(digit));
            BackspacePasscodeCommand = new RelayCommand(RemovePasscodeDigit, () => !IsBusy && IsPasscodeDialogOpen && _passcode.Length > 0);
            ClearPasscodeCommand = new RelayCommand(ClearPasscode, () => !IsBusy && IsPasscodeDialogOpen && _passcode.Length > 0);
            CancelPasscodeCommand = new RelayCommand(CancelPasscodeDialog, () => !IsBusy && IsPasscodeDialogOpen);

            _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sessionTimer.Tick += (_, _) => UpdateSessionCountdown();
            _sessionTimer.Start();
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _syncTimer.Tick += async (_, _) => await BackgroundSyncAsync();
            _syncTimer.Start();

            NormalizeLocalShopScope();
            LoadCachedPunches();
            LoadCachedSnapshot();
            LoadCachedAuthCache();
            RestoreSessionState();
            BuildVisibleModules();
            if (VisibleModules.Count > 0)
                SelectedModule = VisibleModules[0];

            _ = InitializeFromControlSessionAsync();
            RefreshConnectionStatuses();
            RefreshTimeClockPresentation();
        }

        public ObservableCollection<WorkstationModuleCard> VisibleModules { get; } = new();
        public ObservableCollection<ConnectionStatusItem> ConnectionStatuses { get; } = new();
        public ObservableCollection<WorkstationPunchRecord> RecentPunches { get; } = new();
        public ObservableCollection<WorkstationRosterEmployee> RosterEmployees { get; } = new();
        public ObservableCollection<WorkstationSyncQueueItem> PendingSyncItems { get; } = new();
        public ObservableCollection<WorkstationTimeOffRequestRecord> RecentTimeOffRequests { get; } = new();
        public ObservableCollection<WorkstationTimeClockSummaryRow> TodaySummaryRows { get; } = new();
        public ObservableCollection<WorkstationTimeClockSummaryRow> WeeklySummaryRows { get; } = new();
        public ObservableCollection<WorkstationTimeClockSummaryRow> CurrentStatusSummaryRows { get; } = new();
        public ObservableCollection<WorkstationTimeClockActivityRow> TimeClockActivityRows { get; } = new();
        public ObservableCollection<WorkstationWorkOrderSummary> WorkOrders { get; } = new();
        public ObservableCollection<WorkstationWorkOrderSummary> AssignedWorkOrders { get; } = new();
        public ObservableCollection<WorkstationWorkOrderSummary> AvailableWorkOrders { get; } = new();
        public ObservableCollection<WorkstationOperatorAwareness> ShopOperators { get; } = new();
        public ObservableCollection<WorkstationDrawingReference> DrawingReferences { get; } = new();
        public ObservableCollection<WorkstationInspectionTask> InspectionTasks { get; } = new();

        public ICommand LoginCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ConnectControlCommand { get; }
        public ICommand DisconnectControlCommand { get; }
        public ICommand RefreshRegistrationCommand { get; }
        public ICommand RefreshTimeClockCommand { get; }
        public ICommand ClockInCommand { get; }
        public ICommand ClockOutCommand { get; }
        public ICommand BreakStartCommand { get; }
        public ICommand BreakEndCommand { get; }
        public ICommand LunchStartCommand { get; }
        public ICommand LunchEndCommand { get; }
        public ICommand SyncPendingCommand { get; }
        public ICommand SubmitTimeOffCommand { get; }
        public ICommand SelectModuleCommand { get; }
        public ICommand SelectModuleKeyCommand { get; }
        public ICommand RefreshWorkOrdersCommand { get; }
        public ICommand ResumeCurrentJobCommand { get; }
        public ICommand ResumeRecentJobCommand { get; }
        public ICommand SelectWorkOrderCommand { get; }
        public ICommand OpenDrawingCommand { get; }
        public ICommand SelectOperationCommand { get; }
        public ICommand StartOperationCommand { get; }
        public ICommand StopOperationCommand { get; }
        public ICommand CompleteOperationCommand { get; }
        public ICommand SubmitQuantityCommand { get; }
        public ICommand SubmitScrapCommand { get; }
        public ICommand SubmitOperationNoteCommand { get; }
        public ICommand SubmitOperationHelpCommand { get; }
        public ICommand RefreshInspectionTasksCommand { get; }
        public ICommand SelectInspectionTaskCommand { get; }
        public ICommand SubmitInspectionResultCommand { get; }
        public ICommand OpenRosterEmployeeCommand { get; }
        public ICommand AppendPasscodeDigitCommand { get; }
        public ICommand BackspacePasscodeCommand { get; }
        public ICommand ClearPasscodeCommand { get; }
        public ICommand CancelPasscodeCommand { get; }

        public WorkstationSettings Settings
        {
            get => _settings;
            private set
            {
                _settings = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorkstationIdentityLine));
                OnPropertyChanged(nameof(ConnectivityLine));
                RefreshConnectionStatuses();
            }
        }

        public WorkstationSessionSnapshot? CurrentSession
        {
            get => _session;
            private set
            {
                _session = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(ShowLoginPanel));
                OnPropertyChanged(nameof(ShowNoAccessAssigned));
                OnPropertyChanged(nameof(ShowModulesShell));
                OnPropertyChanged(nameof(SessionEmployeeName));
                OnPropertyChanged(nameof(SessionRole));
                OnPropertyChanged(nameof(SessionModulesLine));
                OnPropertyChanged(nameof(HasTimeClockAccess));
                OnPropertyChanged(nameof(HasWorkOrdersAccess));
                OnPropertyChanged(nameof(HasDrawingsAccess));
                OnPropertyChanged(nameof(HasOperationExecutionAccess));
                OnPropertyChanged(nameof(HasProductionQuantityAccess));
                OnPropertyChanged(nameof(HasProductionScrapAccess));
                OnPropertyChanged(nameof(HasProductionNoteAccess));
                OnPropertyChanged(nameof(HasInspectionViewAccess));
                OnPropertyChanged(nameof(HasInspectionEntryAccess));
                OnPropertyChanged(nameof(IsTimeClockSelected));
                OnPropertyChanged(nameof(IsHomeSelected));
                OnPropertyChanged(nameof(IsWorkOrdersSelected));
                OnPropertyChanged(nameof(IsDrawingsSelected));
                RefreshConnectionStatuses();
                RaiseCommandStates();
            }
        }

        public WorkstationRegistrationSnapshot? Registration
        {
            get => _registration;
            private set
            {
                _registration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConnectivityLine));
                RefreshConnectionStatuses();
            }
        }

        public WorkstationModuleCard? SelectedModule
        {
            get => _selectedModule;
            set
            {
                if (_selectedModule == value)
                    return;

                _selectedModule = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentModuleTitle));
                OnPropertyChanged(nameof(CurrentModuleSubtitle));
                OnPropertyChanged(nameof(IsTimeClockSelected));
                OnPropertyChanged(nameof(IsHomeSelected));
                OnPropertyChanged(nameof(IsWorkOrdersSelected));
                OnPropertyChanged(nameof(IsDrawingsSelected));
            }
        }

        public WorkstationWorkOrderSummary? SelectedWorkOrder
        {
            get => _selectedWorkOrder;
            set
            {
                if (_selectedWorkOrder == value)
                    return;

                var workOrderChanged = _selectedWorkOrder?.WorkOrderId != value?.WorkOrderId;
                _selectedWorkOrder = value;
                if (workOrderChanged)
                {
                    WorkOrderDetail = null;
                    SelectedOperation = null;
                    DrawingReferences.Clear();
                    SelectedDrawing = null;
                    InspectionPackage = null;
                    InspectionTasks.Clear();
                    SelectedInspectionTask = null;
                    InspectionActualValue = "";
                    InspectionResultNoteText = "";
                    QuantityReportText = "";
                    ScrapReportText = "";
                    OperationNoteText = "";
                    OperationActionNoteText = "";
                    UpdateActiveContext();
                    OnPropertyChanged(nameof(InspectionSubtitle));
                }

                OnPropertyChanged();
                RaiseCommandStates();
            }
        }

        public WorkstationWorkOrderDetail? WorkOrderDetail
        {
            get => _workOrderDetail;
            private set
            {
                _workOrderDetail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedWorkOrderTitle));
                OnPropertyChanged(nameof(SelectedWorkOrderSummary));
                OnPropertyChanged(nameof(SelectedWorkOrderContext));
                OnPropertyChanged(nameof(SelectedWorkOrderOperationsSummary));
                OnPropertyChanged(nameof(SelectedWorkOrderProgressSummary));
                OnPropertyChanged(nameof(SelectedWorkOrderActiveOperatorsSummary));
                OnPropertyChanged(nameof(ActiveWorkContextLine));
            }
        }

        public WorkstationDrawingReference? SelectedDrawing
        {
            get => _selectedDrawing;
            set
            {
                _selectedDrawing = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }

        public WorkstationWorkOrderOperationSummary? SelectedOperation
        {
            get => _selectedOperation;
            set
            {
                if (_selectedOperation == value)
                    return;

                _selectedOperation = value;
                UpdateActiveContext();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedOperationTitle));
                OnPropertyChanged(nameof(SelectedOperationContext));
                OnPropertyChanged(nameof(ActiveWorkContextLine));
                RaiseCommandStates();
                if (CurrentSession != null && HasInspectionViewAccess && !IsBusy && SelectedWorkOrder != null)
                    _ = RefreshInspectionTasksAsync(false);
            }
        }

        public WorkstationInspectionTaskPackage? InspectionPackage
        {
            get => _inspectionPackage;
            private set
            {
                _inspectionPackage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InspectionTitle));
                OnPropertyChanged(nameof(InspectionSubtitle));
                RaiseCommandStates();
            }
        }

        public WorkstationInspectionTask? SelectedInspectionTask
        {
            get => _selectedInspectionTask;
            set
            {
                if (_selectedInspectionTask == value)
                    return;

                _selectedInspectionTask = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedInspectionTaskTitle));
                OnPropertyChanged(nameof(SelectedInspectionTaskSummary));
                RaiseCommandStates();
            }
        }

        public WorkstationRosterEmployee? SelectedRosterEmployee
        {
            get => _selectedRosterEmployee;
            private set
            {
                _selectedRosterEmployee = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PasscodeDialogTitle));
                OnPropertyChanged(nameof(PasscodeDialogSubtitle));
                OnPropertyChanged(nameof(PasscodeDialogModuleSummary));
                RaiseCommandStates();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value)
                    return;

                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConfirmPasscode));
                RaiseCommandStates();
            }
        }

        public bool IsPasscodeDialogOpen
        {
            get => _isPasscodeDialogOpen;
            private set
            {
                if (_isPasscodeDialogOpen == value)
                    return;

                _isPasscodeDialogOpen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConfirmPasscode));
                RaiseCommandStates();
            }
        }

        public string StatusText { get => _statusText; private set { _statusText = value ?? ""; OnPropertyChanged(); } }
        public string SessionCountdown { get => _sessionCountdown; private set { _sessionCountdown = value ?? ""; OnPropertyChanged(); } }
        public string TimeClockStatus { get => _timeClockStatus; private set { _timeClockStatus = value ?? ""; OnPropertyChanged(); } }
        public string OfflineStatus { get => _offlineStatus; private set { _offlineStatus = value ?? ""; OnPropertyChanged(); } }
        public string AuthCacheStatus { get => _authCacheStatus; private set { _authCacheStatus = value ?? ""; OnPropertyChanged(); } }
        public string SettingsBaseUrl { get => _settingsBaseUrl; set { _settingsBaseUrl = value ?? ""; OnPropertyChanged(); } }
        public string SettingsDesktopBaseUrl { get => _settingsDesktopBaseUrl; set { _settingsDesktopBaseUrl = value ?? ""; OnPropertyChanged(); } }
        public string SettingsShopId { get => _settingsShopId; set { _settingsShopId = value ?? ""; OnPropertyChanged(); } }
        public string SettingsShopName { get => _settingsShopName; set { _settingsShopName = value ?? ""; OnPropertyChanged(); } }
        public string SettingsWorkstationName { get => _settingsWorkstationName; set { _settingsWorkstationName = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(WorkstationIdentityLine)); } }
        public string SettingsPairingCode { get => _settingsPairingCode; set { _settingsPairingCode = value ?? ""; OnPropertyChanged(); } }
        public string SettingsControlEmail { get => _settingsControlEmail; set { _settingsControlEmail = value ?? ""; OnPropertyChanged(); } }
        public string SettingsControlPassword { get => _settingsControlPassword; set { _settingsControlPassword = value ?? ""; OnPropertyChanged(); } }
        public string ControlSessionStatus { get => _controlSessionStatus; private set { _controlSessionStatus = value ?? ""; OnPropertyChanged(); } }
        public string CurrentShiftStatus { get => _currentShiftStatus; private set { _currentShiftStatus = value ?? ""; OnPropertyChanged(); } }
        public string CurrentShiftDetail { get => _currentShiftDetail; private set { _currentShiftDetail = value ?? ""; OnPropertyChanged(); } }
        public string PendingSyncSummary { get => _pendingSyncSummary; private set { _pendingSyncSummary = value ?? ""; OnPropertyChanged(); } }
        public string WorkOrdersStatus { get => _workOrdersStatus; private set { _workOrdersStatus = value ?? ""; OnPropertyChanged(); } }
        public string HeaderDateText => DateTime.Now.ToString("dddd, MMMM d, yyyy");
        public string HeaderTimeText => DateTime.Now.ToString("h:mm tt");
        public string TimeClockStateKey => GetShiftStateKey(CurrentShiftStatus);
        public string TimeClockHeroTitle => TimeClockStateKey switch
        {
            "working" => "YOU ARE WORKING",
            "break" => "ON BREAK",
            "lunch" => "ON LUNCH",
            _ => "NOT WORKING"
        };
        public string TimeClockHeroStatusLabel => CurrentShiftStatus;
        public string TimeClockHeroHelperText => TimeClockStateKey switch
        {
            "working" => "You are currently on the clock.",
            "break" => "Tap End Break when you return.",
            "lunch" => "Tap End Lunch when you return.",
            _ => "Tap Clock In to start your shift."
        };
        public string TimeClockHeroPrimaryLine => BuildHeroPrimaryLine();
        public string TimeClockHeroSecondaryLine => BuildHeroSecondaryLine();
        public string TimeClockHeroAccentBrush => TimeClockStateKey switch
        {
            "working" => "#62D89B",
            "break" => "#E5B05F",
            "lunch" => "#7FAEEA",
            _ => "#E77D87"
        };
        public string TimeClockHeroBorderBrush => TimeClockStateKey switch
        {
            "working" => "#6662D89B",
            "break" => "#66E5B05F",
            "lunch" => "#667FAEEA",
            _ => "#66E77D87"
        };
        public string TimeClockHeroBackgroundBrush => TimeClockStateKey switch
        {
            "working" => "#CC10251F",
            "break" => "#CC271C10",
            "lunch" => "#CC132238",
            _ => "#CC281419"
        };
        public bool ShowClockInAction => TimeClockStateKey == "out";
        public bool ShowWorkingActions => TimeClockStateKey == "working";
        public bool ShowBreakEndAction => TimeClockStateKey == "break";
        public bool ShowLunchEndAction => TimeClockStateKey == "lunch";
        public bool ShowLunchStartAction => SupportsLunch && ShowWorkingActions;
        public bool HasRecentTimeOffRequests => RecentTimeOffRequests.Count > 0;
        public bool ShowNoRecentTimeOffRequests => !HasRecentTimeOffRequests;
        public bool HasTimeClockActivity => TimeClockActivityRows.Count > 0;
        public bool ShowEmptyTimeClockActivity => !HasTimeClockActivity;
        public string TimeClockActivityTitle => "Recent Activity";
        public string TimeOffSectionSubtitle => HasRecentTimeOffRequests
            ? "Request time away without leaving the punch screen."
            : "Need time away? Submit the request here.";
        public WorkstationShopAwareness ShopAwareness
        {
            get => _shopAwareness;
            private set
            {
                _shopAwareness = value ?? new WorkstationShopAwareness();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasShopAwareness));
                OnPropertyChanged(nameof(ShopSummaryLine));
                OnPropertyChanged(nameof(ShopSummaryCountsLine));
                OnPropertyChanged(nameof(ShopOperatorAwarenessLine));
                OnPropertyChanged(nameof(ShopActiveOperatorCountLine));
            }
        }
        public WorkstationCurrentJobContext? CurrentJob
        {
            get => _currentJob;
            private set
            {
                _currentJob = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentJob));
                OnPropertyChanged(nameof(CurrentJobTitle));
                OnPropertyChanged(nameof(CurrentJobSummary));
                OnPropertyChanged(nameof(CurrentJobHint));
                RaiseCommandStates();
            }
        }
        public WorkstationCurrentJobContext? RecentJob
        {
            get => _recentJob;
            private set
            {
                _recentJob = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRecentJob));
                OnPropertyChanged(nameof(RecentJobTitle));
                OnPropertyChanged(nameof(RecentJobSummary));
                RaiseCommandStates();
            }
        }
        public string QuantityReportText { get => _quantityReportText; set { _quantityReportText = value ?? ""; OnPropertyChanged(); RaiseCommandStates(); } }
        public string ScrapReportText { get => _scrapReportText; set { _scrapReportText = value ?? ""; OnPropertyChanged(); RaiseCommandStates(); } }
        public string OperationNoteText { get => _operationNoteText; set { _operationNoteText = value ?? ""; OnPropertyChanged(); RaiseCommandStates(); } }
        public string OperationActionNoteText { get => _operationActionNoteText; set { _operationActionNoteText = value ?? ""; OnPropertyChanged(); } }
        public string InspectionActualValue { get => _inspectionActualValue; set { _inspectionActualValue = value ?? ""; OnPropertyChanged(); RaiseCommandStates(); } }
        public string InspectionResultNoteText { get => _inspectionResultNoteText; set { _inspectionResultNoteText = value ?? ""; OnPropertyChanged(); } }
        public string TimeOffType { get => _timeOffType; set { _timeOffType = value ?? "VACATION"; OnPropertyChanged(); } }
        public string TimeOffStartDate { get => _timeOffStartDate; set { _timeOffStartDate = value ?? ""; OnPropertyChanged(); } }
        public string TimeOffEndDate { get => _timeOffEndDate; set { _timeOffEndDate = value ?? ""; OnPropertyChanged(); } }
        public string TimeOffHoursText { get => _timeOffHoursText; set { _timeOffHoursText = value ?? ""; OnPropertyChanged(); } }
        public string TimeOffNote { get => _timeOffNote; set { _timeOffNote = value ?? ""; OnPropertyChanged(); } }
        public DateTime? TimeOffStartDateValue
        {
            get => TryParsePickerDate(TimeOffStartDate);
            set
            {
                TimeOffStartDate = value.HasValue
                    ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "";
                OnPropertyChanged();
            }
        }
        public DateTime? TimeOffEndDateValue
        {
            get => TryParsePickerDate(TimeOffEndDate);
            set
            {
                TimeOffEndDate = value.HasValue
                    ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : "";
                OnPropertyChanged();
            }
        }

        public bool IsLoggedIn => HasUsableEmployeeSession(CurrentSession);
        public bool ShowLoginPanel => !IsLoggedIn;
        public bool ShowNoAccessAssigned => IsLoggedIn && VisibleModules.Count == 0;
        public bool ShowModulesShell => IsLoggedIn && VisibleModules.Count > 0;
        public bool HasTimeClockAccess => HasModule("timeclock");
        public bool HasWorkOrdersAccess => HasAction("workorders.view");
        public bool HasDrawingsAccess => HasAction("drawings.view");
        public bool HasOperationExecutionAccess => HasAction("workorders.execute");
        public bool HasProductionQuantityAccess => HasAction("production.reportQuantity");
        public bool HasProductionScrapAccess => HasAction("production.reportScrap");
        public bool HasProductionNoteAccess => HasAction("production.addNote");
        public bool HasInspectionViewAccess => HasAction("inspection.view");
        public bool HasInspectionEntryAccess => HasAction("inspection.enterResult");
        public bool SupportsLunch => _timeclockSnapshot?.SupportsLunch == true;
        public bool HasPendingSyncItems => PendingSyncItems.Any(item => !string.Equals(item.SyncStatus, "synced", StringComparison.OrdinalIgnoreCase));
        public bool IsHomeSelected => string.Equals(SelectedModule?.Key, "home", StringComparison.OrdinalIgnoreCase);
        public bool IsTimeClockSelected => string.Equals(SelectedModule?.Key, "timeclock", StringComparison.OrdinalIgnoreCase);
        public bool IsWorkOrdersSelected => string.Equals(SelectedModule?.Key, "workorders", StringComparison.OrdinalIgnoreCase);
        public bool IsDrawingsSelected => string.Equals(SelectedModule?.Key, "drawings", StringComparison.OrdinalIgnoreCase);
        public bool HasCurrentJob => CurrentJob != null && CurrentJob.WorkOrderId > 0;
        public bool HasRecentJob => !HasCurrentJob && RecentJob != null && RecentJob.WorkOrderId > 0;
        public bool HasShopAwareness => ShopAwareness.Summary.VisibleJobs > 0 || ShopOperators.Count > 0;
        public bool CanConfirmPasscode => !IsBusy && IsPasscodeDialogOpen && SelectedRosterEmployee != null && _passcode.Length >= 4 && _passcode.Length <= 6;
        public string WorkstationIdentityLine => $"{Settings.WorkstationName}  |  {Settings.WorkstationId}";
        public string ConnectivityLine
        {
            get
            {
                var shop = string.IsNullOrWhiteSpace(Settings.ShopName) ? "Unassigned shop" : Settings.ShopName;
                var registration = Registration == null ? "Not registered" : $"Registered: {Registration.Status}";
                return $"{shop}  |  {registration}";
            }
        }

        public string SessionEmployeeName => CurrentSession?.Employee?.DisplayName ?? "No active user";
        public string SessionRole => CurrentSession?.Employee?.Role ?? "Signed out";
        public string SessionModulesLine => CurrentSession == null ? "No modules available" : (CurrentSession.Modules.Count == 0 ? "No access assigned" : string.Join("  |  ", CurrentSession.Modules.Select(ToModuleLabel)));
        public string CurrentJobTitle => CurrentJob == null ? "No active job" : $"{CurrentJob.WorkOrderNumber} • Op {CurrentJob.OperationNumber:000}";
        public string CurrentJobSummary => CurrentJob == null ? "Desktop has not assigned an active job to this employee yet." : $"{CurrentJob.PartNumber} • {CurrentJob.OperationTitle}";
        public string CurrentJobHint => CurrentJob == null ? "Select an assigned job to begin work." : $"{CurrentJob.AssignmentLabel} • {CurrentJob.OperationStatus}";
        public string RecentJobTitle => RecentJob == null ? "No recent job" : $"{RecentJob.WorkOrderNumber} • Op {RecentJob.OperationNumber:000}";
        public string RecentJobSummary => RecentJob == null ? "No recent workstation job is waiting to resume." : $"{RecentJob.PartNumber} • {RecentJob.OperationTitle}";
        public string ShopSummaryLine => !HasShopAwareness
            ? "Desktop will surface shop awareness after workstation jobs load."
            : $"Active jobs {ShopAwareness.Summary.ActiveJobs} | Waiting jobs {ShopAwareness.Summary.WaitingJobs} | Completed {ShopAwareness.Summary.CompletedJobs}";
        public string ShopSummaryCountsLine => !HasShopAwareness
            ? "No shop-level visibility is loaded yet."
            : $"Active operators {ShopAwareness.Summary.ActiveOperators} | Idle {ShopAwareness.Summary.IdleOperators} | Visible jobs {ShopAwareness.Summary.VisibleJobs}";
        public string ShopOperatorAwarenessLine => ShopOperators.Count == 0
            ? "No operators are currently active in this workstation view."
            : $"Showing {ShopOperators.Count} active operator{(ShopOperators.Count == 1 ? "" : "s")} from Desktop.";
        public string ShopActiveOperatorCountLine => ShopOperators.Count == 0
            ? "No active operators"
            : ShopOperators.Count == 1
                ? $"{ShopOperators[0].OperatorName} is active now."
                : $"{ShopOperators.Count} operators are active now.";
        public string CurrentModuleTitle => SelectedModule?.Title ?? "RunBook Workstation";
        public string CurrentModuleSubtitle => SelectedModule?.Subtitle ?? "Shop-floor access shell";
        public string SelectedWorkOrderTitle => WorkOrderDetail?.WorkOrderNumber ?? "Select a work order";
        public string SelectedWorkOrderSummary => WorkOrderDetail == null
            ? "Read-only released work-order detail will appear here."
            : $"{WorkOrderDetail.PartNumber}  •  Rev {WorkOrderDetail.Revision}  •  Qty {WorkOrderDetail.Quantity}";
        public string SelectedWorkOrderContext => WorkOrderDetail == null
            ? "Desktop remains the authority for work-order data."
            : $"{WorkOrderDetail.ReleaseState} snapshot  •  Due {FormatFriendlyDate(WorkOrderDetail.DueDate)}  •  {WorkOrderDetail.SnapshotLoadSource}";
        public string SelectedWorkOrderOperationsSummary => WorkOrderDetail == null
            ? "No operation summary loaded yet."
            : WorkOrderDetail.TotalOperations <= 0
                ? "No released operations were found for this work order."
                : $"In Progress: {WorkOrderDetail.OperationSummary.InProgress}  •  Completed: {WorkOrderDetail.OperationSummary.Completed}  •  Remaining: {WorkOrderDetail.OperationSummary.NotStarted}";
        public string SelectedWorkOrderProgressSummary => WorkOrderDetail == null
            ? "Desktop computes production visibility from authoritative operation state."
            : WorkOrderDetail.TotalOperations <= 0
                ? "Progress will appear once released operations exist."
                : $"{WorkOrderDetail.ProgressPercent}% complete  •  {WorkOrderDetail.CompletedOperations}/{WorkOrderDetail.TotalOperations} operations finished";
        public string SelectedWorkOrderActiveOperatorsSummary => WorkOrderDetail == null
            ? "Operator awareness will appear when a work order is selected."
            : WorkOrderDetail.ActiveOperators.Count == 0
                ? "No operators are currently active on this job."
                : WorkOrderDetail.ActiveOperators.Count == 1
                    ? $"{WorkOrderDetail.ActiveOperators[0].EmployeeName} is currently working this job."
                    : $"{WorkOrderDetail.ActiveOperators.Count} operators are currently active on this job.";
        public string ActiveWorkContextLine => _activeContext == null || _activeContext.WorkOrderId <= 0
            ? "No active work context selected."
            : _activeContext.OperationId <= 0
                ? $"{_activeContext.WorkOrderNumber}  â€¢  {_activeContext.PartNumber}"
                : $"{_activeContext.WorkOrderNumber}  â€¢  Op {_activeContext.OperationNumber:000} {_activeContext.OperationTitle}";
        public string SelectedOperationTitle => SelectedOperation == null
            ? "Select an operation"
            : $"Op {SelectedOperation.OperationNumber:000}  â€¢  {SelectedOperation.Title}";
        public string SelectedOperationContext => SelectedOperation == null
            ? "Pick a released operation to execute from Workstation."
            : $"{SelectedOperation.Status}  â€¢  {SelectedOperation.Department} / {SelectedOperation.WorkCenter}";
        public string InspectionTitle => InspectionPackage == null
            ? "Inspection Tasks"
            : string.IsNullOrWhiteSpace(InspectionPackage.FeatureSetName) ? "Inspection Tasks" : InspectionPackage.FeatureSetName;
        public string InspectionSubtitle => InspectionPackage == null
            ? "Inspection entry will appear when an operation is selected."
            : (InspectionTasks.Count == 0 ? "No inspection tasks available for the current context." : $"Loaded {InspectionTasks.Count} inspection tasks from Desktop.");
        public string SelectedInspectionTaskTitle => SelectedInspectionTask == null
            ? "Select an inspection item"
            : (SelectedInspectionTask.BalloonNumber > 0 ? $"Balloon {SelectedInspectionTask.BalloonNumber}" : $"Feature {SelectedInspectionTask.FeatureId}");
        public string SelectedInspectionTaskSummary => SelectedInspectionTask == null
            ? "Measured value entry will appear here."
            : $"{SelectedInspectionTask.FeatureText}  â€¢  {SelectedInspectionTask.InputType}";
        public string PasscodeDialogTitle => SelectedRosterEmployee?.DisplayName ?? "Employee Sign In";
        public string PasscodeDialogSubtitle => SelectedRosterEmployee == null ? "Select an employee to continue." : SelectedRosterEmployee.Role;
        public string PasscodeDialogModuleSummary => SelectedRosterEmployee?.AccessSummary ?? "";
        public string PasscodeMaskDisplay => _passcode.Length == 0 ? "Enter code" : string.Join(" ", _passcode.Select(_ => "\u2022"));

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetControlPassword(string password) => SettingsControlPassword = password ?? "";

        public bool HandlePasscodeKey(Key key)
        {
            if (!IsPasscodeDialogOpen || IsBusy)
                return false;

            if (key >= Key.D0 && key <= Key.D9)
            {
                AppendPasscodeDigit(((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture));
                return true;
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                AppendPasscodeDigit(((int)(key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture));
                return true;
            }

            if (key == Key.Back)
            {
                RemovePasscodeDigit();
                return true;
            }

            if (key == Key.Delete)
            {
                ClearPasscode();
                return true;
            }

            if (key == Key.Enter || key == Key.Return)
            {
                if (CanConfirmPasscode)
                    _ = LoginAsync();
                return true;
            }

            if (key == Key.Escape)
            {
                CancelPasscodeDialog();
                return true;
            }

            return false;
        }

        private async Task LoginAsync()
        {
            if (!CanConfirmPasscode || SelectedRosterEmployee == null)
            {
                StatusText = "Enter a 4 to 6 digit passcode.";
                return;
            }

            NormalizeLocalShopScope();
            if (!ValidateLocalAuthSettings())
                return;

            var submittedPasscode = _passcode;
            await RunBusyAsync(async () =>
            {
                await RefreshAuthPackageAsyncCore(false);
                var employee = FindRosterEmployee(
                    SelectedRosterEmployee.RemoteEmployeeId,
                    SelectedRosterEmployee.EmployeeId,
                    SelectedRosterEmployee.EmployeeCode);
                var authHealth = GetAuthCacheHealth();
                UpdateAuthCacheStatus(authHealth);

                if (employee == null)
                {
                    StatusText = "That employee is no longer present in the Desktop auth cache.";
                    ClearPasscode();
                    return;
                }

                var login = await _api.LoginLocalEmployeeAsync(
                    Settings,
                    employee.RemoteEmployeeId,
                    employee.EmployeeId,
                    employee.EmployeeCode,
                    submittedPasscode,
                    CancellationToken.None);

                var payload = MapCapabilityPayload(login.Payload);
                var modules = payload.Modules
                    .Where(entry => entry.Value)
                    .Select(entry => entry.Key)
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var snapshot = new WorkstationSessionSnapshot
                {
                    Token = login.Session?.Token ?? "",
                    IssuedAtUtc = login.Session?.IssuedAtUtc ?? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ExpiresAtUtc = login.Session?.ExpiresAtUtc ?? DateTime.UtcNow.AddMinutes(Math.Max(1, employee.SessionTimeoutMinutes)).ToString("O", CultureInfo.InvariantCulture),
                    SessionTimeoutMinutes = login.Session?.SessionTimeoutMinutes ?? employee.SessionTimeoutMinutes,
                    WorkstationId = Registration?.WorkstationId ?? Settings.WorkstationId,
                    WorkstationName = Registration?.WorkstationName ?? Settings.WorkstationName,
                    ShopId = Settings.ShopId,
                    ShopName = Settings.ShopName,
                    Employee = new WorkstationEmployeeIdentity
                    {
                        RemoteEmployeeId = string.IsNullOrWhiteSpace(payload.Employee.RemoteEmployeeId)
                            ? employee.RemoteEmployeeId
                            : payload.Employee.RemoteEmployeeId,
                        EmployeeId = payload.Employee.EmployeeId,
                        ShopId = Settings.ShopId,
                        EmployeeCode = payload.Employee.EmployeeCode,
                        DisplayName = payload.Employee.DisplayName,
                        Role = payload.Employee.Role,
                    },
                    Modules = modules,
                    Actions = new Dictionary<string, bool>(payload.Actions, StringComparer.OrdinalIgnoreCase),
                    Capabilities = payload,
                };

                CurrentSession = snapshot;
                WorkstationStorageService.SaveSession(snapshot);
                MarkDesktopRequestSuccess();
                Settings.WorkstationName = Registration?.WorkstationName ?? Settings.WorkstationName;
                WorkstationStorageService.SaveSettings(Settings);
                BuildVisibleModules();
                SelectedModule = VisibleModules.FirstOrDefault();
                StatusText = $"Signed in as {SessionEmployeeName}.";
                OfflineStatus = "Desktop employee session is active.";
                _passcode = "";
                IsPasscodeDialogOpen = false;
                SelectedRosterEmployee = null;
                OnPropertyChanged(nameof(PasscodeMaskDisplay));
                UpdateSessionCountdown();
                LoadQueue();
                await RefreshDesktopTimeclockStateAsync();
                await SyncPendingQueueAsync(false);
            });

            if (CurrentSession != null && HasWorkOrdersAccess)
                await RefreshWorkOrdersAsync(false);
        }

        private async Task RefreshRegistrationAsync()
        {
            await RunBusyAsync(async () =>
            {
                if (ControlSessionService.HasSession())
                    await SyncCurrentShopAsync();
                await EnsureRegistrationAsync();
                await RefreshAuthPackageAsyncCore(true);
                await LoadRosterCoreAsync();
                StatusText = "Workstation registration and employee auth refreshed.";
            });
        }

        private async Task RefreshTimeClockAsync()
        {
            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            await RunBusyAsync(async () =>
            {
                await RefreshDesktopTimeclockStateAsync();
                await SyncPendingQueueAsync(false);
            });
        }

        private async Task SubmitPunchAsync(string eventType, string note)
        {
            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            await RunBusyAsync(async () =>
            {
                if (!CanSubmitEvent(eventType))
                {
                    StatusText = $"Cannot record {ToEventLabel(eventType)} while state is {CurrentShiftStatus}.";
                    return;
                }

                EnqueuePunch(eventType, note);
                await SyncPendingQueueAsync(false);
            });
        }

        private void Logout()
        {
            CurrentSession = null;
            WorkstationStorageService.SaveSession(null);
            VisibleModules.Clear();
            WorkOrders.Clear();
            AssignedWorkOrders.Clear();
            AvailableWorkOrders.Clear();
            DrawingReferences.Clear();
            InspectionTasks.Clear();
            RecentPunches.Clear();
            RecentTimeOffRequests.Clear();
            TodaySummaryRows.Clear();
            WeeklySummaryRows.Clear();
            CurrentStatusSummaryRows.Clear();
            TimeClockActivityRows.Clear();
            SelectedModule = null;
            SelectedWorkOrder = null;
            WorkOrderDetail = null;
            SelectedOperation = null;
            SelectedDrawing = null;
            InspectionPackage = null;
            SelectedInspectionTask = null;
            OnPropertyChanged(nameof(InspectionSubtitle));
            _activeContext = null;
            CurrentJob = null;
            RecentJob = null;
            ShopAwareness = new WorkstationShopAwareness();
            ShopOperators.Clear();
            _assignedWorkOrderCount = 0;
            _backupWorkOrderCount = 0;
            SelectedRosterEmployee = null;
            _passcode = "";
            SessionCountdown = "Signed out";
            StatusText = "Session cleared. Ready for next employee.";
            IsPasscodeDialogOpen = false;
            CurrentShiftStatus = "CLOCKED OUT";
            CurrentShiftDetail = "No shift loaded yet.";
            PendingSyncSummary = "No pending sync items.";
            WorkOrdersStatus = "Work orders will load when this module opens.";
            QuantityReportText = "";
            ScrapReportText = "";
            OperationNoteText = "";
            OperationActionNoteText = "";
            InspectionActualValue = "";
            InspectionResultNoteText = "";
            RefreshTimeClockPresentation();
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            OnPropertyChanged(nameof(ActiveWorkContextLine));
            RefreshConnectionStatuses();
        }

        private void SelectModule(WorkstationModuleCard? module)
        {
            if (module == null)
                return;

             if (!HasModule(module.Key))
             {
                 StatusText = $"{module.Title} is not available for this employee.";
                 return;
             }

            SelectedModule = module;
            if (IsTimeClockSelected)
                TimeClockStatus = RecentPunches.Count == 0 ? "No recent punches yet." : $"Showing {RecentPunches.Count} recent punches.";
            else if (IsHomeSelected && HasWorkOrdersAccess)
                _ = RefreshWorkOrdersAsync(false);
            else if (IsWorkOrdersSelected || IsDrawingsSelected)
                _ = RefreshWorkOrdersAsync(false);
        }

        private void SelectModuleByKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            var module = VisibleModules.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
            if (module != null)
                SelectModule(module);
        }

        private void SaveSettings()
        {
            Settings.ControlBaseUrl = (SettingsBaseUrl ?? "").Trim();
            Settings.DesktopBaseUrl = (SettingsDesktopBaseUrl ?? "").Trim();
            Settings.ShopId = (SettingsShopId ?? "").Trim();
            Settings.ShopName = (SettingsShopName ?? "").Trim();
            Settings.WorkstationName = (SettingsWorkstationName ?? "").Trim();
            Settings.PairingCode = NormalizePairingCode(SettingsPairingCode);
            WorkstationStorageService.SaveSettings(Settings);
            OnPropertyChanged(nameof(ConnectivityLine));
            OnPropertyChanged(nameof(WorkstationIdentityLine));
            StatusText = "Workstation settings saved.";
        }

        private async Task ConnectControlAsync()
        {
            if (IsBusy)
                return;

            SaveSettings();
            if (string.IsNullOrWhiteSpace(Settings.ControlBaseUrl))
            {
                StatusText = "Control base URL is required.";
                return;
            }

            if (ControlSessionService.HasSession() && string.IsNullOrWhiteSpace(SettingsControlPassword))
            {
                ControlSessionStatus = ControlSessionService.GetStatusLabel();
                StatusText = "Supervisor session already active.";
                return;
            }

            await RunBusyAsync(() =>
            {
                var session = ControlSessionService.SignIn(Settings.ControlBaseUrl, SettingsControlEmail, SettingsControlPassword);
                SettingsControlPassword = "";
                ControlSessionStatus = ControlSessionService.GetStatusLabel();
                return Task.CompletedTask;
            });

            await RunBusyAsync(async () =>
            {
                await SyncCurrentShopAsync();
                await LoadRosterCoreAsync();
                StatusText = $"Supervisor session ready for {Settings.ShopName}.";
            });
        }

        private void DisconnectControl()
        {
            ControlSessionService.SignOut();
            SettingsControlPassword = "";
            ControlSessionStatus = ControlSessionService.GetStatusLabel();
            LoadCachedAuthCache();
            StatusText = "Supervisor session cleared. Cached Desktop employee auth remains available.";
        }

        private async Task EnsureRegistrationAsync()
        {
            SaveSettings();
            if (string.IsNullOrWhiteSpace(Settings.DesktopBaseUrl))
                throw new InvalidOperationException("Desktop base URL is required before workstation enrollment.");
            if (string.IsNullOrWhiteSpace(Settings.ShopId))
                throw new InvalidOperationException("Shop ID is required before workstation enrollment.");
            if (string.IsNullOrWhiteSpace(Settings.PairingCode))
                throw new InvalidOperationException("PAIRING_REQUIRED: Enter the Desktop pairing code before enrolling this workstation.");

            var registration = await _api.RegisterAsync(Settings, CancellationToken.None);
            Registration = registration;
            if (!string.IsNullOrWhiteSpace(registration.ShopId))
            {
                Settings.ShopId = registration.ShopId;
                SettingsShopId = registration.ShopId;
            }
            if (!string.IsNullOrWhiteSpace(registration.WorkstationId))
                Settings.WorkstationId = registration.WorkstationId;
            if (!string.IsNullOrWhiteSpace(registration.WorkstationName))
            {
                Settings.WorkstationName = registration.WorkstationName;
                SettingsWorkstationName = registration.WorkstationName;
            }
            WorkstationStorageService.SaveRegistration(registration);
            Settings.PairingCode = "";
            SettingsPairingCode = "";
            WorkstationStorageService.SaveSettings(Settings);
        }

        private void RestoreSessionState()
        {
            if (CurrentSession == null)
            {
                SessionCountdown = "Signed out";
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentSession.Token))
            {
                Logout();
                StatusText = "Previous workstation sign-in is no longer valid. Sign in again.";
                return;
            }

            if (!HasSessionEmployeeIdentity(CurrentSession))
            {
                Logout();
                StatusText = "Previous workstation sign-in is incomplete. Sign in again.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(CurrentSession.WorkstationId)
                && !string.Equals(CurrentSession.WorkstationId, Settings.WorkstationId, StringComparison.OrdinalIgnoreCase))
            {
                Logout();
                StatusText = "Previous workstation sign-in belongs to a different workstation. Sign in again.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(CurrentSession.ShopId)
                && !string.Equals(CurrentSession.ShopId, Settings.ShopId, StringComparison.OrdinalIgnoreCase))
            {
                Logout();
                StatusText = "Previous workstation sign-in belongs to a different shop. Sign in again.";
                return;
            }

            if (!DateTime.TryParse(CurrentSession.ExpiresAtUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiresUtc))
            {
                Logout();
                StatusText = "Previous workstation session timing is invalid. Sign in again.";
                return;
            }

            if (expiresUtc <= DateTime.UtcNow)
            {
                Logout();
                StatusText = "Previous session expired. Passcode required.";
                return;
            }

            EnsureSessionCapabilities();
            CurrentJob = CurrentSession.Capabilities.CurrentJob;
            RecentJob = CurrentSession.Capabilities.RecentJob;
            BuildVisibleModules();
            SelectedModule = VisibleModules.FirstOrDefault();
            StatusText = $"Session restored for {SessionEmployeeName}.";
            OfflineStatus = GetAuthCacheHealth().Message;
            UpdateSessionCountdown();

            if (HasTimeClockAccess)
                _ = RefreshTimeClockAsync();
            if (HasWorkOrdersAccess)
                _ = RefreshWorkOrdersAsync(false);
        }

        private void OpenRosterEmployee(WorkstationRosterEmployee? employee)
        {
            if (employee == null)
                return;

            SelectedRosterEmployee = employee;
            _passcode = "";
            IsPasscodeDialogOpen = true;
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            StatusText = employee.HasWorkstationPasscode
                ? $"Enter the passcode for {employee.DisplayName}."
                : $"{employee.DisplayName} may need an employee auth refresh if the passcode was just configured. Enter the passcode to try Desktop validation.";
        }

        private void AppendPasscodeDigit(string? digit)
        {
            if (string.IsNullOrWhiteSpace(digit) || !char.IsDigit(digit[0]) || _passcode.Length >= 6)
                return;

            _passcode += digit[0];
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            RaiseCommandStates();
        }

        private void RemovePasscodeDigit()
        {
            if (_passcode.Length == 0)
                return;

            _passcode = _passcode[..^1];
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            RaiseCommandStates();
        }

        private void ClearPasscode()
        {
            if (_passcode.Length == 0)
                return;

            _passcode = "";
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            RaiseCommandStates();
        }

        private void CancelPasscodeDialog()
        {
            _passcode = "";
            IsPasscodeDialogOpen = false;
            SelectedRosterEmployee = null;
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            StatusText = "Workstation ready.";
        }

        private async Task BackgroundSyncAsync()
        {
            if (IsBusy)
                return;

            if (_lastAuthRefreshAttemptUtc == DateTime.MinValue || DateTime.UtcNow - _lastAuthRefreshAttemptUtc >= AuthRefreshInterval)
                await RefreshAuthPackageAsyncCore(false);

            await SyncPendingQueueAsync(false);
        }

        private async Task RefreshCapabilityPayloadAsync(bool allowFallback)
        {
            if (CurrentSession == null)
                return;

            try
            {
                var response = await _api.GetSessionMeAsync(Settings, CurrentSession, CancellationToken.None);
                MarkDesktopRequestSuccess();
                ApplyCapabilityPayload(MapCapabilityPayload(response.Payload));
            }
            catch (Exception ex)
            {
                if (IsEmployeeSessionFailure(ex.Message ?? ""))
                    throw;

                if (!allowFallback)
                    throw;

                DebugLogService.WriteException("Capability payload refresh failed", ex);
                EnsureSessionCapabilities();
            }
        }

        private void EnsureSessionCapabilities()
        {
            if (CurrentSession == null)
                return;

            if (CurrentSession.Capabilities.Modules.Count == 0 && CurrentSession.Modules.Count > 0)
            {
                CurrentSession.Capabilities.Modules = CurrentSession.Modules
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(key => key, _ => true, StringComparer.OrdinalIgnoreCase);
            }

            if (CurrentSession.Capabilities.Actions.Count == 0)
            {
                CurrentSession.Capabilities.Actions = new Dictionary<string, bool>(CurrentSession.Actions ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(CurrentSession.Capabilities.Employee.EmployeeId))
            {
                CurrentSession.Capabilities.Employee = new WorkstationCapabilityEmployee
                {
                    EmployeeId = CurrentSession.Employee.EmployeeId,
                    EmployeeCode = CurrentSession.Employee.EmployeeCode,
                    DisplayName = CurrentSession.Employee.DisplayName,
                    Role = CurrentSession.Employee.Role,
                    SessionTimeoutMinutes = CurrentSession.SessionTimeoutMinutes,
                };
            }

            if (string.IsNullOrWhiteSpace(CurrentSession.Capabilities.Workstation.DeviceId))
            {
                CurrentSession.Capabilities.Workstation = new WorkstationCapabilityWorkstation
                {
                    ShopId = CurrentSession.ShopId,
                    ShopName = CurrentSession.ShopName,
                    DeviceId = CurrentSession.WorkstationId,
                    DeviceName = CurrentSession.WorkstationName,
                };
            }

            CurrentSession.Actions = new Dictionary<string, bool>(CurrentSession.Capabilities.Actions, StringComparer.OrdinalIgnoreCase);
            CurrentSession.Modules = CurrentSession.Capabilities.Modules
                .Where(entry => entry.Value)
                .Select(entry => entry.Key)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            WorkstationStorageService.SaveSession(CurrentSession);
        }

        private void ApplyCapabilityPayload(WorkstationCapabilityPayload payload)
        {
            if (CurrentSession == null)
                return;

            CurrentSession.Capabilities = payload;
            CurrentSession.Actions = new Dictionary<string, bool>(payload.Actions, StringComparer.OrdinalIgnoreCase);
            CurrentSession.Modules = payload.Modules
                .Where(entry => entry.Value)
                .Select(entry => entry.Key)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            CurrentSession.Employee.RemoteEmployeeId = payload.Employee.RemoteEmployeeId;
            CurrentSession.Employee.EmployeeId = payload.Employee.EmployeeId;
            CurrentSession.Employee.EmployeeCode = payload.Employee.EmployeeCode;
            CurrentSession.Employee.DisplayName = payload.Employee.DisplayName;
            CurrentSession.Employee.Role = payload.Employee.Role;
            CurrentSession.SessionTimeoutMinutes = payload.Employee.SessionTimeoutMinutes < 1 ? CurrentSession.SessionTimeoutMinutes : payload.Employee.SessionTimeoutMinutes;
            CurrentSession.WorkstationId = payload.Workstation.DeviceId;
            CurrentSession.WorkstationName = payload.Workstation.DeviceName;
            CurrentSession.ShopId = payload.Workstation.ShopId;
            CurrentSession.ShopName = payload.Workstation.ShopName;
            CurrentJob = payload.CurrentJob;
            RecentJob = payload.RecentJob;
            CurrentSession.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(CurrentSession.SessionTimeoutMinutes).ToString("O", CultureInfo.InvariantCulture);
            WorkstationStorageService.SaveSession(CurrentSession);
            OnPropertyChanged(nameof(SessionEmployeeName));
            OnPropertyChanged(nameof(SessionRole));
            OnPropertyChanged(nameof(SessionModulesLine));
            OnPropertyChanged(nameof(HasTimeClockAccess));
            OnPropertyChanged(nameof(HasWorkOrdersAccess));
            OnPropertyChanged(nameof(HasDrawingsAccess));
            OnPropertyChanged(nameof(HasOperationExecutionAccess));
            OnPropertyChanged(nameof(HasProductionQuantityAccess));
            OnPropertyChanged(nameof(HasProductionScrapAccess));
            OnPropertyChanged(nameof(HasProductionNoteAccess));
            OnPropertyChanged(nameof(HasInspectionViewAccess));
            OnPropertyChanged(nameof(HasInspectionEntryAccess));
            BuildVisibleModules();
            if (SelectedModule == null || !HasModule(SelectedModule.Key))
                SelectedModule = VisibleModules.FirstOrDefault();
        }

        private async Task RefreshWorkOrdersAsync(bool manual)
        {
            if (CurrentSession == null || !HasWorkOrdersAccess)
                return;

            await RunBusyAsync(async () =>
            {
                var response = await _api.GetWorkOrdersAsync(Settings, CurrentSession, CancellationToken.None);
                MarkDesktopRequestSuccess();
                CurrentJob = response.CurrentJob == null ? null : MapCurrentJobContext(response.CurrentJob);
                RecentJob = response.RecentJob == null ? null : MapCurrentJobContext(response.RecentJob);
                ApplyShopAwareness(response.ShopAwareness);
                _assignedWorkOrderCount = response.AssignedCount;
                _backupWorkOrderCount = response.BackupCount;
                var previousSelectedId = SelectedWorkOrder?.WorkOrderId ?? 0;
                WorkOrders.Clear();
                AssignedWorkOrders.Clear();
                AvailableWorkOrders.Clear();
                foreach (var workOrder in response.WorkOrders.Select(MapWorkOrderSummary))
                    WorkOrders.Add(workOrder);
                foreach (var workOrder in response.AssignedJobs.Select(MapWorkOrderSummary))
                    AssignedWorkOrders.Add(workOrder);
                foreach (var workOrder in response.AvailableJobs.Select(MapWorkOrderSummary))
                    AvailableWorkOrders.Add(workOrder);

                var preferredCurrentId = CurrentJob?.WorkOrderId ?? 0;
                SelectedWorkOrder = WorkOrders.FirstOrDefault(item => item.WorkOrderId == previousSelectedId)
                    ?? WorkOrders.FirstOrDefault(item => item.WorkOrderId == preferredCurrentId)
                    ?? AssignedWorkOrders.FirstOrDefault()
                    ?? AvailableWorkOrders.FirstOrDefault()
                    ?? WorkOrders.FirstOrDefault();
                if (SelectedWorkOrder != null)
                    await LoadWorkOrderDetailAsync(SelectedWorkOrder.WorkOrderId, IsDrawingsSelected);
                else
                {
                    WorkOrderDetail = null;
                    SelectedOperation = null;
                    _activeContext = null;
                    OnPropertyChanged(nameof(ActiveWorkContextLine));
                    InspectionPackage = null;
                    InspectionTasks.Clear();
                    SelectedInspectionTask = null;
                    OnPropertyChanged(nameof(InspectionSubtitle));
                    DrawingReferences.Clear();
                    SelectedDrawing = null;
                }

                WorkOrdersStatus = WorkOrders.Count == 0
                    ? "Desktop has no assigned or backup work orders available for this employee."
                    : $"Loaded {WorkOrders.Count} workstation jobs from Desktop. Assigned {_assignedWorkOrderCount}, backup {_backupWorkOrderCount}.";
                if (manual)
                    StatusText = WorkOrdersStatus;
            });
        }

        private async Task SelectWorkOrderAsync(WorkstationWorkOrderSummary? workOrder)
        {
            if (workOrder == null || CurrentSession == null || !HasWorkOrdersAccess)
                return;

            await RunBusyAsync(async () =>
            {
                SelectedWorkOrder = workOrder;
                await LoadWorkOrderDetailAsync(workOrder.WorkOrderId, true);
                StatusText = $"Loaded {workOrder.WorkOrderNumber} from Desktop.";
            });
        }

        private async Task ResumeCurrentJobAsync()
        {
            if (CurrentSession == null || !HasWorkOrdersAccess || CurrentJob == null)
                return;

            SelectedModule = VisibleModules.FirstOrDefault(module => string.Equals(module.Key, "workorders", StringComparison.OrdinalIgnoreCase)) ?? SelectedModule;
            var summary = WorkOrders.FirstOrDefault(item => item.WorkOrderId == CurrentJob.WorkOrderId);
            if (summary != null)
            {
                await SelectWorkOrderAsync(summary);
                return;
            }

            await RunBusyAsync(async () =>
            {
                await LoadWorkOrderDetailAsync(CurrentJob.WorkOrderId, true);
                SelectedWorkOrder = WorkOrders.FirstOrDefault(item => item.WorkOrderId == CurrentJob.WorkOrderId) ?? SelectedWorkOrder;
                StatusText = $"Resumed {CurrentJob.WorkOrderNumber} from Desktop.";
            });
        }

        private async Task ResumeRecentJobAsync()
        {
            if (CurrentSession == null || !HasWorkOrdersAccess || RecentJob == null)
                return;

            SelectedModule = VisibleModules.FirstOrDefault(module => string.Equals(module.Key, "workorders", StringComparison.OrdinalIgnoreCase)) ?? SelectedModule;
            var summary = WorkOrders.FirstOrDefault(item => item.WorkOrderId == RecentJob.WorkOrderId)
                ?? AssignedWorkOrders.FirstOrDefault(item => item.WorkOrderId == RecentJob.WorkOrderId)
                ?? AvailableWorkOrders.FirstOrDefault(item => item.WorkOrderId == RecentJob.WorkOrderId);
            if (summary != null)
            {
                await SelectWorkOrderAsync(summary);
                return;
            }

            await RunBusyAsync(async () =>
            {
                await LoadWorkOrderDetailAsync(RecentJob.WorkOrderId, true);
                StatusText = $"Loaded recent job {RecentJob.WorkOrderNumber} from Desktop.";
            });
        }

        private async Task LoadWorkOrderDetailAsync(int workOrderId, bool includeDrawings)
        {
            if (CurrentSession == null)
                return;

            var detailResponse = await _api.GetWorkOrderDetailAsync(Settings, CurrentSession, workOrderId, CancellationToken.None);
            WorkOrderDetail = MapWorkOrderDetail(detailResponse.WorkOrder);
            var preferredOperationId = SelectedOperation?.OperationId ?? 0;
            if (CurrentJob?.WorkOrderId == workOrderId && CurrentJob.OperationId > 0)
                preferredOperationId = CurrentJob.OperationId;
            SelectedOperation = WorkOrderDetail?.Operations.FirstOrDefault(operation => operation.OperationId == preferredOperationId)
                ?? WorkOrderDetail?.Operations.FirstOrDefault();
            UpdateActiveContext();

            if (HasInspectionViewAccess)
                await RefreshInspectionTasksAsync(false);
            else
            {
                InspectionPackage = null;
                InspectionTasks.Clear();
                SelectedInspectionTask = null;
                OnPropertyChanged(nameof(InspectionSubtitle));
            }

            if (includeDrawings && HasDrawingsAccess)
                await LoadDrawingPackageAsync(workOrderId);
        }

        private async Task SelectOperationAsync(WorkstationWorkOrderOperationSummary? operation)
        {
            if (operation == null)
                return;

            SelectedOperation = operation;
            if (HasInspectionViewAccess)
                await RefreshInspectionTasksAsync(false);
        }

        private async Task LoadDrawingPackageAsync(int workOrderId)
        {
            if (CurrentSession == null || !HasDrawingsAccess)
                return;

            var response = await _api.GetWorkOrderDrawingsAsync(Settings, CurrentSession, workOrderId, CancellationToken.None);
            DrawingReferences.Clear();
            foreach (var drawing in response.DrawingPackage?.Drawings?.Select(MapDrawingReference) ?? Enumerable.Empty<WorkstationDrawingReference>())
                DrawingReferences.Add(drawing);
            SelectedDrawing = DrawingReferences.FirstOrDefault(drawing => drawing.IsPrimary) ?? DrawingReferences.FirstOrDefault();
        }

        private async Task OpenDrawingAsync(WorkstationDrawingReference? drawing)
        {
            if (drawing == null || CurrentSession == null || !HasDrawingsAccess)
                return;

            await RunBusyAsync(async () =>
            {
                var response = await _api.DownloadDrawingAsync(Settings, CurrentSession, drawing.DownloadRoute, CancellationToken.None);
                var bytes = response.GetBytes();
                if (bytes.Length == 0)
                    throw new InvalidOperationException("Desktop returned an empty drawing payload.");

                var tempFolder = Path.Combine(WorkstationStorageService.TempFolder, "drawings");
                Directory.CreateDirectory(tempFolder);
                var fileName = string.IsNullOrWhiteSpace(response.FileName) ? $"{drawing.DrawingId}.bin" : response.FileName;
                var localPath = Path.Combine(tempFolder, fileName);
                File.WriteAllBytes(localPath, bytes);
                Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
                StatusText = $"Opened {drawing.Label} in the default viewer.";
            });
        }

        private async Task ExecuteOperationAsync(string action)
        {
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null || !HasOperationExecutionAccess)
                return;

            await RunBusyAsync(async () =>
            {
                RunBookWorkstationApiClient.WorkOrderDetailResponse response = (action ?? "").Trim().ToLowerInvariant() switch
                {
                    "start" => await _api.StartOperationAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, OperationActionNoteText, CancellationToken.None),
                    "stop" => await _api.StopOperationAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, OperationActionNoteText, CancellationToken.None),
                    "complete" => await _api.CompleteOperationAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, OperationActionNoteText, CancellationToken.None),
                    _ => throw new InvalidOperationException("Unsupported operation action.")
                };

                ApplyWorkOrderMutationResponse(response, SelectedOperation.OperationId);
                OperationActionNoteText = "";
                StatusText = string.IsNullOrWhiteSpace(response.Message)
                    ? $"Operation {action} saved."
                    : response.Message;
            });

            if (CurrentSession != null && HasWorkOrdersAccess)
                await RefreshWorkOrdersAsync(false);
        }

        private async Task SubmitQuantityAsync()
        {
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null || !HasProductionQuantityAccess)
                return;

            if (!int.TryParse((QuantityReportText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
            {
                StatusText = "Enter a non-negative completed quantity.";
                return;
            }
            if (quantity > MaxProductionQuantityValue)
            {
                StatusText = $"Completed quantity must be {MaxProductionQuantityValue:N0} or less.";
                return;
            }

            await RunBusyAsync(async () =>
            {
                var response = await _api.ReportQuantityAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, quantity, OperationActionNoteText, CancellationToken.None);
                ApplyWorkOrderMutationResponse(response, SelectedOperation.OperationId);
                QuantityReportText = "";
                OperationActionNoteText = "";
                StatusText = string.IsNullOrWhiteSpace(response.Message) ? "Quantity saved." : response.Message;
            });
        }

        private async Task SubmitScrapAsync()
        {
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null || !HasProductionScrapAccess)
                return;

            if (!int.TryParse((ScrapReportText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
            {
                StatusText = "Enter a non-negative scrap quantity.";
                return;
            }
            if (quantity > MaxProductionQuantityValue)
            {
                StatusText = $"Scrap quantity must be {MaxProductionQuantityValue:N0} or less.";
                return;
            }

            await RunBusyAsync(async () =>
            {
                var response = await _api.ReportScrapAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, quantity, OperationActionNoteText, CancellationToken.None);
                ApplyWorkOrderMutationResponse(response, SelectedOperation.OperationId);
                ScrapReportText = "";
                OperationActionNoteText = "";
                StatusText = string.IsNullOrWhiteSpace(response.Message) ? "Scrap saved." : response.Message;
            });
        }

        private async Task SubmitOperationNoteAsync()
        {
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null || !HasProductionNoteAccess)
                return;

            var note = (OperationNoteText ?? "").Trim();
            if (note.Length == 0)
            {
                StatusText = "Enter a note before submitting.";
                return;
            }
            if (note.Length > MaxWorkstationNoteLength)
            {
                StatusText = $"Notes must be {MaxWorkstationNoteLength:N0} characters or less.";
                return;
            }

            await RunBusyAsync(async () =>
            {
                var response = await _api.AddOperationNoteAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, note, CancellationToken.None);
                ApplyWorkOrderMutationResponse(response, SelectedOperation.OperationId);
                OperationNoteText = "";
                StatusText = string.IsNullOrWhiteSpace(response.Message) ? "Note saved." : response.Message;
            });
        }

        private async Task SubmitOperationHelpAsync()
        {
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null || !HasWorkOrdersAccess)
                return;

            var note = (OperationActionNoteText ?? "").Trim();
            if (note.Length == 0)
            {
                StatusText = "Enter what help you need before sending the request.";
                return;
            }
            if (note.Length > MaxWorkstationNoteLength)
            {
                StatusText = $"Help requests must be {MaxWorkstationNoteLength:N0} characters or less.";
                return;
            }

            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            PendingSyncItems.Add(new WorkstationSyncQueueItem
            {
                QueueId = Guid.NewGuid().ToString("D"),
                ClientEventId = $"{Settings.WorkstationId}-{Guid.NewGuid():D}",
                ItemType = "help_request",
                RemoteEmployeeId = CurrentSession.Employee.RemoteEmployeeId,
                EmployeeId = CurrentSession.Employee.EmployeeId,
                EmployeeCode = CurrentSession.Employee.EmployeeCode,
                EmployeeName = CurrentSession.Employee.DisplayName,
                ShopId = Settings.ShopId,
                WorkstationId = Settings.WorkstationId,
                DeviceId = Settings.WorkstationId,
                EffectiveUtc = now,
                CreatedLocalUtc = now,
                Note = note,
                WorkOrderId = SelectedWorkOrder.WorkOrderId,
                WorkOrderNumber = SelectedWorkOrder.WorkOrderNumber,
                OperationId = SelectedOperation.OperationId,
                OperationNumber = SelectedOperation.OperationNumber,
                OperationTitle = SelectedOperation.Title,
                SyncStatus = "pending"
            });

            SaveQueue();
            OperationActionNoteText = "";
            StatusText = "Help request queued for foreman review.";
            await SyncPendingQueueAsync(false);
        }

        private async Task RefreshInspectionTasksAsync(bool manual)
        {
            if (CurrentSession == null || SelectedWorkOrder == null || !HasInspectionViewAccess)
                return;

            try
            {
                var operationId = SelectedOperation?.OperationId ?? 0;
                var response = await _api.GetInspectionTasksAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, operationId, CancellationToken.None);
                InspectionPackage = MapInspectionTaskPackage(response.Inspection);
                InspectionTasks.Clear();
                foreach (var task in InspectionPackage?.Tasks ?? Enumerable.Empty<WorkstationInspectionTask>())
                    InspectionTasks.Add(task);
                OnPropertyChanged(nameof(InspectionSubtitle));
                SelectedInspectionTask = InspectionTasks.FirstOrDefault(task => task.FeatureId == SelectedInspectionTask?.FeatureId) ?? InspectionTasks.FirstOrDefault();
                if (SelectedInspectionTask != null)
                {
                    InspectionActualValue = SelectedInspectionTask.ActualValue;
                    InspectionResultNoteText = SelectedInspectionTask.ResultNotes;
                }
                else
                {
                    InspectionActualValue = "";
                    InspectionResultNoteText = "";
                }

                if (manual)
                    StatusText = string.IsNullOrWhiteSpace(response.Message) ? InspectionSubtitle : response.Message;
            }
            catch (Exception ex)
            {
                InspectionPackage = null;
                InspectionTasks.Clear();
                SelectedInspectionTask = null;
                OnPropertyChanged(nameof(InspectionSubtitle));
                InspectionActualValue = "";
                InspectionResultNoteText = "";
                if (manual)
                    StatusText = ex.Message;
            }
        }

        private void SelectInspectionTask(WorkstationInspectionTask? task)
        {
            if (task == null)
                return;

            SelectedInspectionTask = task;
            InspectionActualValue = task.ActualValue;
            InspectionResultNoteText = task.ResultNotes;
        }

        private async Task SubmitInspectionResultAsync()
        {
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedInspectionTask == null || !HasInspectionEntryAccess)
                return;

            var value = (InspectionActualValue ?? "").Trim();
            if (value.Length == 0)
            {
                StatusText = "Enter an inspection value before submitting.";
                return;
            }
            if (value.Length > MaxInspectionActualValueLength)
            {
                StatusText = $"Inspection values must be {MaxInspectionActualValueLength:N0} characters or less.";
                return;
            }
            if ((InspectionResultNoteText ?? "").Trim().Length > MaxWorkstationNoteLength)
            {
                StatusText = $"Inspection notes must be {MaxWorkstationNoteLength:N0} characters or less.";
                return;
            }

            await RunBusyAsync(async () =>
            {
                var response = await _api.SubmitInspectionResultAsync(
                    Settings,
                    CurrentSession,
                    SelectedWorkOrder.WorkOrderId,
                    SelectedOperation?.OperationId ?? 0,
                    InspectionPackage?.FeatureSetId ?? 0,
                    SelectedInspectionTask.FeatureId,
                    SelectedInspectionTask.SampleIndex < 1 ? 1 : SelectedInspectionTask.SampleIndex,
                    value,
                    InspectionResultNoteText ?? "",
                    CancellationToken.None);

                InspectionPackage = MapInspectionTaskPackage(response.Inspection);
                InspectionTasks.Clear();
                foreach (var task in InspectionPackage?.Tasks ?? Enumerable.Empty<WorkstationInspectionTask>())
                    InspectionTasks.Add(task);
                OnPropertyChanged(nameof(InspectionSubtitle));
                SelectedInspectionTask = InspectionTasks.FirstOrDefault(task => task.FeatureId == SelectedInspectionTask.FeatureId) ?? InspectionTasks.FirstOrDefault();
                if (SelectedInspectionTask != null)
                {
                    InspectionActualValue = SelectedInspectionTask.ActualValue;
                    InspectionResultNoteText = SelectedInspectionTask.ResultNotes;
                }
                else
                {
                    InspectionActualValue = "";
                    InspectionResultNoteText = "";
                }
                StatusText = string.IsNullOrWhiteSpace(response.Message) ? "Inspection result saved." : response.Message;
            });
        }

        private void ApplyWorkOrderMutationResponse(RunBookWorkstationApiClient.WorkOrderDetailResponse response, int preferredOperationId)
        {
            WorkOrderDetail = MapWorkOrderDetail(response.WorkOrder);
            if (WorkOrderDetail != null)
            {
                var existingIndex = WorkOrders.ToList().FindIndex(item => item.WorkOrderId == WorkOrderDetail.WorkOrderId);
                if (existingIndex >= 0)
                {
                    var replacement = new WorkstationWorkOrderSummary
                    {
                        WorkOrderId = WorkOrderDetail.WorkOrderId,
                        WorkOrderNumber = WorkOrderDetail.WorkOrderNumber,
                        PartNumber = WorkOrderDetail.PartNumber,
                        PartDescription = WorkOrderDetail.PartDescription,
                        Revision = WorkOrderDetail.Revision,
                        Status = WorkOrderDetail.Status,
                        Quantity = WorkOrderDetail.Quantity,
                        DueDate = WorkOrderDetail.DueDate,
                        OperationCount = WorkOrderDetail.Operations.Count,
                        HasDrawings = WorkOrderDetail.HasDrawings,
                        ReleasedUtc = WorkOrderDetail.ReleasedUtc,
                        VisibilitySource = WorkOrderDetail.VisibilitySource,
                        IsCurrentJob = WorkOrderDetail.IsCurrentJob,
                        IsAssigned = WorkOrderDetail.IsAssigned,
                        AssignmentLabel = WorkOrderDetail.AssignmentLabel,
                        ProgressPercent = WorkOrderDetail.ProgressPercent,
                        CompletedOperations = WorkOrderDetail.CompletedOperations,
                        TotalOperations = WorkOrderDetail.TotalOperations,
                        OperationSummary = WorkOrderDetail.OperationSummary,
                        ActiveOperators = WorkOrderDetail.ActiveOperators.ToList(),
                    };
                    WorkOrders[existingIndex] = replacement;
                    SyncGroupedWorkOrderCollections(replacement);
                    SelectedWorkOrder = replacement;
                }
            }

            SelectedOperation = WorkOrderDetail?.Operations.FirstOrDefault(operation => operation.OperationId == preferredOperationId)
                ?? WorkOrderDetail?.Operations.FirstOrDefault();
            if (WorkOrderDetail != null && SelectedOperation != null && string.Equals(SelectedOperation.Status, "In Progress", StringComparison.OrdinalIgnoreCase))
            {
                CurrentJob = new WorkstationCurrentJobContext
                {
                    WorkOrderId = WorkOrderDetail.WorkOrderId,
                    WorkOrderNumber = WorkOrderDetail.WorkOrderNumber,
                    PartNumber = WorkOrderDetail.PartNumber,
                    PartDescription = WorkOrderDetail.PartDescription,
                    VisibilitySource = WorkOrderDetail.VisibilitySource,
                    OperationId = SelectedOperation.OperationId,
                    OperationNumber = SelectedOperation.OperationNumber,
                    OperationTitle = SelectedOperation.Title,
                    OperationStatus = SelectedOperation.Status,
                    StartedUtc = SelectedOperation.StartedUtc,
                    AssignmentLabel = WorkOrderDetail.AssignmentLabel,
                };
                if (RecentJob?.WorkOrderId == CurrentJob.WorkOrderId && RecentJob.OperationId == CurrentJob.OperationId)
                    RecentJob = null;
            }
            else if (CurrentJob?.WorkOrderId == WorkOrderDetail?.WorkOrderId)
            {
                if (WorkOrderDetail != null && SelectedOperation != null &&
                    (!string.IsNullOrWhiteSpace(SelectedOperation.StartedUtc) || !string.IsNullOrWhiteSpace(SelectedOperation.CompletedUtc)))
                {
                    RecentJob = new WorkstationCurrentJobContext
                    {
                        WorkOrderId = WorkOrderDetail.WorkOrderId,
                        WorkOrderNumber = WorkOrderDetail.WorkOrderNumber,
                        PartNumber = WorkOrderDetail.PartNumber,
                        PartDescription = WorkOrderDetail.PartDescription,
                        VisibilitySource = WorkOrderDetail.VisibilitySource,
                        OperationId = SelectedOperation.OperationId,
                        OperationNumber = SelectedOperation.OperationNumber,
                        OperationTitle = SelectedOperation.Title,
                        OperationStatus = SelectedOperation.Status,
                        StartedUtc = string.IsNullOrWhiteSpace(SelectedOperation.StartedUtc) ? SelectedOperation.CompletedUtc : SelectedOperation.StartedUtc,
                        AssignmentLabel = WorkOrderDetail.AssignmentLabel,
                    };
                }
                CurrentJob = null;
            }
            UpdateActiveContext();
            if (CurrentSession != null && HasInspectionViewAccess && SelectedWorkOrder != null)
                _ = RefreshInspectionTasksAsync(false);
        }

        private void UpdateActiveContext()
        {
            if (SelectedWorkOrder == null)
            {
                _activeContext = null;
            }
            else
            {
                _activeContext = new WorkstationActiveContext
                {
                    WorkOrderId = SelectedWorkOrder.WorkOrderId,
                    WorkOrderNumber = SelectedWorkOrder.WorkOrderNumber,
                    PartNumber = SelectedWorkOrder.PartNumber,
                    OperationId = SelectedOperation?.OperationId ?? 0,
                    OperationNumber = SelectedOperation?.OperationNumber ?? 0,
                    OperationTitle = SelectedOperation?.Title ?? "",
                };
            }

            OnPropertyChanged(nameof(ActiveWorkContextLine));
        }

        private void ApplyShopAwareness(RunBookWorkstationApiClient.ShopAwareness? awareness)
        {
            ShopAwareness = MapShopAwareness(awareness);
            ShopOperators.Clear();
            foreach (var operatorRow in ShopAwareness.Operators.Where(candidate => candidate.IsActive))
                ShopOperators.Add(operatorRow);
            OnPropertyChanged(nameof(HasShopAwareness));
            OnPropertyChanged(nameof(ShopSummaryLine));
            OnPropertyChanged(nameof(ShopSummaryCountsLine));
            OnPropertyChanged(nameof(ShopOperatorAwarenessLine));
            OnPropertyChanged(nameof(ShopActiveOperatorCountLine));
        }

        private void SyncGroupedWorkOrderCollections(WorkstationWorkOrderSummary replacement)
        {
            SyncWorkOrderGroupMembership(AssignedWorkOrders, replacement, replacement.IsAssigned && !replacement.IsCurrentJob);
            SyncWorkOrderGroupMembership(AvailableWorkOrders, replacement, replacement.IsDepartmentMatch || replacement.IsBackupJob);
        }

        private static void SyncWorkOrderGroupMembership(ObservableCollection<WorkstationWorkOrderSummary> collection, WorkstationWorkOrderSummary replacement, bool shouldContain)
        {
            if (collection == null || replacement == null)
                return;

            var index = collection
                .Select((item, idx) => new { item, idx })
                .FirstOrDefault(entry => entry.item.WorkOrderId == replacement.WorkOrderId)?
                .idx ?? -1;
            if (shouldContain)
            {
                if (index >= 0)
                    collection[index] = replacement;
                else
                    collection.Add(replacement);
            }
            else if (index >= 0)
            {
                collection.RemoveAt(index);
            }
        }

        private static void ReplaceWorkOrderInCollection(ObservableCollection<WorkstationWorkOrderSummary> collection, WorkstationWorkOrderSummary replacement)
        {
            if (collection == null || replacement == null)
                return;

            var index = collection
                .Select((item, idx) => new { item, idx })
                .FirstOrDefault(entry => entry.item.WorkOrderId == replacement.WorkOrderId)?
                .idx ?? -1;
            if (index >= 0)
                collection[index] = replacement;
        }

        private bool HasModule(string key)
            => CurrentSession?.Capabilities?.Modules?.TryGetValue(key, out var allowed) == true && allowed;

        private bool HasAction(string key)
            => CurrentSession?.Capabilities?.Actions?.TryGetValue(key, out var allowed) == true && allowed;

        private static bool HasUsableEmployeeSession(WorkstationSessionSnapshot? session)
            => session != null
               && !string.IsNullOrWhiteSpace(session.Token)
               && HasSessionEmployeeIdentity(session)
               && DateTime.TryParse(session.ExpiresAtUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiresUtc)
               && expiresUtc > DateTime.UtcNow;

        private static bool HasSessionEmployeeIdentity(WorkstationSessionSnapshot session)
            => session.Employee != null
               && (!string.IsNullOrWhiteSpace(session.Employee.RemoteEmployeeId) || !string.IsNullOrWhiteSpace(session.Employee.EmployeeId))
               && !string.IsNullOrWhiteSpace(session.Employee.DisplayName);

        private void BuildVisibleModules()
        {
            VisibleModules.Clear();
            if (!IsLoggedIn)
            {
                OnPropertyChanged(nameof(ShowNoAccessAssigned));
                OnPropertyChanged(nameof(ShowModulesShell));
                return;
            }

            AddModuleIfAllowed("home", "Home", "Jump into the modules this employee is allowed to use.", "HM");
            AddModuleIfAllowed("timeclock", "Time Clock", "Clock in, clock out, breaks, and recent punches.", "TC");
            AddModuleIfAllowed("workorders", "Work Orders", "Execute released operations, report production, and enter inspection results.", "WO");
            AddModuleIfAllowed("drawings", "Drawings", "Open released drawings tied to the selected work order.", "DW");
            OnPropertyChanged(nameof(ShowNoAccessAssigned));
            OnPropertyChanged(nameof(ShowModulesShell));
        }

        private void AddModuleIfAllowed(string key, string title, string subtitle, string glyph)
        {
            if (!HasModule(key))
                return;

            VisibleModules.Add(new WorkstationModuleCard { Key = key, Title = title, Subtitle = subtitle, Glyph = glyph });
        }

        private void LoadCachedPunches()
        {
            RecentPunches.Clear();
            foreach (var punch in WorkstationStorageService.LoadTimeClockCache())
                RecentPunches.Add(punch);
            UpdatePendingSummary();
        }

        private void ReplacePunches(System.Collections.Generic.IEnumerable<RunBookWorkstationApiClient.TimeClockPunch>? punches)
        {
            RecentPunches.Clear();
            if (punches != null)
            {
                foreach (var punch in punches)
                {
                    RecentPunches.Add(new WorkstationPunchRecord
                    {
                        Id = punch.Id,
                        EventType = punch.EventType,
                        ClientTs = punch.ClientTs,
                        ServerTs = punch.ServerTs,
                        Source = punch.Source,
                        Note = punch.Note,
                        IsPendingSync = punch.IsPendingSync,
                        SyncState = string.IsNullOrWhiteSpace(punch.SyncState) ? "synced" : punch.SyncState
                    });
                }
            }

            WorkstationStorageService.SaveTimeClockCache(RecentPunches.ToArray());
            OnPropertyChanged(nameof(RecentPunches));
            UpdatePendingSummary();
        }

        private void LoadCachedSnapshot()
        {
            var snapshot = WorkstationStorageService.LoadTimeclockState();
            if (snapshot != null)
                ApplySnapshot(snapshot, false);

            LoadQueue();
        }

        private void LoadCachedAuthCache()
        {
            _authCache = WorkstationStorageService.LoadAuthCache();
            LoadRosterFromAuthCache();
            UpdateAuthCacheStatus();
            if (CurrentSession == null)
                OfflineStatus = GetAuthCacheHealth().Message;
        }

        private void LoadQueue()
        {
            PendingSyncItems.Clear();
            foreach (var item in WorkstationStorageService.LoadTimeclockQueue())
                PendingSyncItems.Add(item);
            UpdatePendingSummary();
        }

        private void SaveQueue()
        {
            WorkstationStorageService.SaveTimeclockQueue(PendingSyncItems.ToArray());
            UpdatePendingSummary();
        }

        private async Task RefreshDesktopTimeclockStateAsync()
        {
            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            var response = await _api.GetDesktopTimeclockStateAsync(Settings, CurrentSession, CancellationToken.None);
            MarkDesktopRequestSuccess();
            var snapshot = MapSnapshot(response.Snapshot);
            ApplySnapshot(snapshot, true);
            OfflineStatus = "Desktop connected and authoritative.";
            StatusText = $"Loaded Desktop state for {snapshot.EmployeeName}.";
        }

        private async Task<bool> RefreshAuthPackageAsyncCore(bool manual)
        {
            NormalizeLocalShopScope();
            SaveSettings();
            if (string.IsNullOrWhiteSpace(Settings.DesktopBaseUrl))
            {
                if (manual)
                    StatusText = "Desktop base URL is required before employee auth can sync.";
                UpdateAuthCacheStatus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(Settings.ShopId))
            {
                if (manual)
                    StatusText = "Shop ID is required before employee auth can sync.";
                UpdateAuthCacheStatus();
                return false;
            }

            if (Registration == null ||
                !string.Equals(Registration.ShopId, Settings.ShopId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Registration.WorkstationId, Settings.WorkstationId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(Registration.DeviceToken))
            {
                if (manual || !string.IsNullOrWhiteSpace(Settings.PairingCode))
                {
                    await EnsureRegistrationAsync();
                }
                else
                {
                    var message = "Desktop enrollment is required. Enter a current Desktop pairing code and refresh registration.";
                    _authCache ??= new WorkstationEmployeeAuthCache();
                    _authCache.LastRefreshError = message;
                    WorkstationStorageService.SaveAuthCache(_authCache);
                    UpdateAuthCacheStatus();
                    if (manual)
                        StatusText = message;
                    return false;
                }
            }

            _lastAuthRefreshAttemptUtc = DateTime.UtcNow;
            _authCache ??= new WorkstationEmployeeAuthCache();
            _authCache.LastRefreshAttemptUtc = _lastAuthRefreshAttemptUtc.ToString("O", CultureInfo.InvariantCulture);

            try
            {
                var response = await _api.GetLocalAuthPackageAsync(Settings, CancellationToken.None);
                MarkDesktopRequestSuccess();
                var package = MapAuthPackage(response.Package);
                _authCache.Package = package;
                _authCache.LastRefreshSuccessUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                _authCache.LastRefreshError = "";
                _authCache.LastSuccessfulPackageHash = package.PackageHash;
                if (!string.IsNullOrWhiteSpace(package.ShopId) && !string.Equals(Settings.ShopId, package.ShopId, StringComparison.OrdinalIgnoreCase))
                {
                    Settings.ShopId = package.ShopId;
                    SettingsShopId = package.ShopId;
                    WorkstationStorageService.SaveSettings(Settings);
                    OnPropertyChanged(nameof(ConnectivityLine));
                }
                if (!string.IsNullOrWhiteSpace(package.WorkstationId) && !string.Equals(Settings.WorkstationId, package.WorkstationId, StringComparison.OrdinalIgnoreCase))
                {
                    Settings.WorkstationId = package.WorkstationId;
                    WorkstationStorageService.SaveSettings(Settings);
                    OnPropertyChanged(nameof(ConnectivityLine));
                    OnPropertyChanged(nameof(WorkstationIdentityLine));
                }
                WorkstationStorageService.SaveAuthCache(_authCache);
                LoadRosterFromAuthCache();
                UpdateAuthCacheStatus();
                OfflineStatus = "Desktop employee auth connected.";
                if (manual)
                    StatusText = $"Employee auth refreshed from Desktop for {package.Employees.Count} employees.";
                return true;
            }
            catch (Exception ex)
            {
                if (IsTrustFailure(ex.Message))
                {
                    HandleTrustFailure(ex.Message);
                    if (manual)
                        StatusText = ex.Message;
                    return false;
                }

                _authCache.LastRefreshError = ex.Message;
                WorkstationStorageService.SaveAuthCache(_authCache);
                LoadRosterFromAuthCache();
                if (IsConnectivityFailure(ex))
                    MarkDesktopRequestFailure(ex.Message);
                var authHealth = GetAuthCacheHealth();
                UpdateAuthCacheStatus(authHealth);
                OfflineStatus = authHealth.AllowsOfflineLogin
                    ? "Desktop unavailable. Using cached employee auth."
                    : "Desktop employee auth unavailable.";
                if (manual)
                    StatusText = authHealth.AllowsOfflineLogin ? authHealth.Message : ex.Message;
                return false;
            }
        }

        private async Task SyncPendingQueueAsync(bool manual)
        {
            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            var pendingItems = PendingSyncItems
                .Where(item => string.Equals(item.SyncStatus, "pending", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => ParseUtc(item.EffectiveUtc))
                .ThenBy(item => ParseUtc(item.CreatedLocalUtc))
                .ThenBy(item => item.QueueId, StringComparer.Ordinal)
                .ToList();

            if (pendingItems.Count == 0)
            {
                if (manual)
                    StatusText = "No pending workstation items to sync.";
                return;
            }

            try
            {
                var response = await _api.SyncDesktopTimeclockAsync(Settings, CurrentSession, pendingItems, CancellationToken.None);
                foreach (var result in response.Results)
                {
                    var item = PendingSyncItems.FirstOrDefault(entry => string.Equals(entry.QueueId, result.QueueId, StringComparison.Ordinal));
                    if (item == null)
                        continue;

                    item.RemoteReceiptId = result.RemoteReceiptId ?? "";
                    item.LastError = result.Message ?? "";
                    if (string.Equals(result.Outcome, "accepted", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(result.Outcome, "already_processed", StringComparison.OrdinalIgnoreCase))
                    {
                        item.SyncStatus = "synced";
                    }
                    else if (string.Equals(result.Outcome, "rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        item.SyncStatus = "failed";
                    }
                    else
                    {
                        item.SyncStatus = "pending";
                        item.RetryCount++;
                    }
                }

                SaveQueue();
                MarkDesktopRequestSuccess();
                var snapshot = MapSnapshot(response.Snapshot);
                ApplySnapshot(snapshot, true);
                RemoveSyncedQueueItems();
                OfflineStatus = "Desktop connected and sync complete.";
                TimeClockStatus = HasPendingSyncItems ? "Some items still need attention." : "Desktop is fully synced.";
                StatusText = HasPendingSyncItems ? "Some workstation items were rejected and need review." : "Workstation items synced to Desktop.";
            }
            catch (Exception ex)
            {
                foreach (var item in pendingItems)
                {
                    item.RetryCount++;
                    item.LastError = ex.Message;
                }

                SaveQueue();
                RebuildLocalProjection();
                if (IsConnectivityFailure(ex))
                    MarkDesktopRequestFailure(ex.Message);
                OfflineStatus = "Desktop unavailable. Pending items will sync later.";
                if (manual)
                    StatusText = ex.Message;
            }
        }

        private void RemoveSyncedQueueItems()
        {
            var synced = PendingSyncItems
                .Where(item => string.Equals(item.SyncStatus, "synced", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var item in synced)
                PendingSyncItems.Remove(item);

            SaveQueue();
            RebuildLocalProjection();
        }

        private void EnqueuePunch(string eventType, string note)
        {
            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var item = new WorkstationSyncQueueItem
            {
                QueueId = Guid.NewGuid().ToString("D"),
                ClientEventId = $"{Settings.WorkstationId}-{Guid.NewGuid():D}",
                ItemType = "punch",
                RemoteEmployeeId = CurrentSession?.Employee.RemoteEmployeeId ?? "",
                EmployeeId = CurrentSession?.Employee.EmployeeId ?? "",
                EmployeeCode = CurrentSession?.Employee.EmployeeCode ?? "",
                EmployeeName = CurrentSession?.Employee.DisplayName ?? "",
                ShopId = Settings.ShopId,
                WorkstationId = Settings.WorkstationId,
                DeviceId = Settings.WorkstationId,
                ActionType = eventType,
                EffectiveUtc = now,
                CreatedLocalUtc = now,
                Note = note ?? "",
                SyncStatus = "pending"
            };

            PendingSyncItems.Add(item);
            SaveQueue();
            RebuildLocalProjection();
            TimeClockStatus = $"{ToEventLabel(eventType)} pending Desktop sync.";
        }

        private async Task SubmitTimeOffAsync()
        {
            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            var startDate = NormalizeDate(TimeOffStartDate);
            var endDate = NormalizeDate(TimeOffEndDate);
            if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
            {
                StatusText = "Choose valid start and end dates.";
                return;
            }

            if (string.CompareOrdinal(endDate, startDate) < 0)
            {
                StatusText = "Time-off end date must be on or after the start date.";
                return;
            }

            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            PendingSyncItems.Add(new WorkstationSyncQueueItem
            {
                QueueId = Guid.NewGuid().ToString("D"),
                ClientEventId = $"{Settings.WorkstationId}-{Guid.NewGuid():D}",
                ItemType = "time_off_request",
                RemoteEmployeeId = CurrentSession.Employee.RemoteEmployeeId,
                EmployeeId = CurrentSession.Employee.EmployeeId,
                EmployeeCode = CurrentSession.Employee.EmployeeCode,
                EmployeeName = CurrentSession.Employee.DisplayName,
                ShopId = Settings.ShopId,
                WorkstationId = Settings.WorkstationId,
                DeviceId = Settings.WorkstationId,
                EffectiveUtc = now,
                CreatedLocalUtc = now,
                Note = TimeOffNote ?? "",
                RequestType = string.IsNullOrWhiteSpace(TimeOffType) ? "VACATION" : TimeOffType.Trim().ToUpperInvariant(),
                StartDate = startDate,
                EndDate = endDate,
                HoursRequested = null,
                SyncStatus = "pending",
            });

            SaveQueue();
            RebuildLocalProjection();
            TimeOffStartDate = "";
            TimeOffEndDate = "";
            OnPropertyChanged(nameof(TimeOffStartDateValue));
            OnPropertyChanged(nameof(TimeOffEndDateValue));
            TimeOffHoursText = "";
            TimeOffNote = "";
            StatusText = "Time-off request queued.";
            await SyncPendingQueueAsync(false);
        }

        private void ApplySnapshot(WorkstationTimeclockSnapshot snapshot, bool persist)
        {
            _timeclockSnapshot = snapshot;
            ReplacePunches(snapshot.RecentPunches.Select(punch => new RunBookWorkstationApiClient.TimeClockPunch
            {
                Id = punch.Id,
                EventType = punch.EventType,
                ClientTs = punch.ClientTs,
                ServerTs = punch.ServerTs,
                Source = punch.Source,
                Note = punch.Note,
                IsPendingSync = punch.IsPendingSync,
                SyncState = punch.SyncState
            }));

            RecentTimeOffRequests.Clear();
            foreach (var request in snapshot.RecentTimeOffRequests)
                RecentTimeOffRequests.Add(request);

            CurrentShiftStatus = ToShiftTitle(snapshot.CurrentStatus);
            CurrentShiftDetail = BuildShiftDetail(snapshot);
            TimeClockStatus = RecentPunches.Count == 0 ? "No recent punches yet." : $"Loaded {RecentPunches.Count} recent punches.";
            if (persist)
                WorkstationStorageService.SaveTimeclockState(snapshot);

            OnPropertyChanged(nameof(SupportsLunch));
            RebuildLocalProjection();
        }

        private void RebuildLocalProjection()
        {
            var basePunches = (_timeclockSnapshot?.RecentPunches ?? new List<WorkstationPunchRecord>())
                .Select(ClonePunch)
                .ToList();
            foreach (var item in PendingSyncItems.Where(entry => string.Equals(entry.ItemType, "punch", StringComparison.OrdinalIgnoreCase)))
            {
                basePunches.Add(new WorkstationPunchRecord
                {
                    Id = item.ClientEventId,
                    EventType = item.ActionType.ToUpperInvariant(),
                    ClientTs = item.EffectiveUtc,
                    ServerTs = "",
                    Source = "workstation-pending",
                    Note = item.Note,
                    IsPendingSync = true,
                    SyncState = item.SyncStatus
                });
            }

            var orderedPunches = basePunches
                .OrderByDescending(entry => ParseUtc(entry.ClientTs))
                .ThenByDescending(entry => entry.Id, StringComparer.Ordinal)
                .Take(20)
                .ToArray();
            WorkstationStorageService.SaveTimeClockCache(orderedPunches);
            RecentPunches.Clear();
            foreach (var punch in orderedPunches)
                RecentPunches.Add(punch);

            var requests = (_timeclockSnapshot?.RecentTimeOffRequests ?? new List<WorkstationTimeOffRequestRecord>())
                .Select(CloneRequest)
                .ToList();
            foreach (var item in PendingSyncItems.Where(entry => string.Equals(entry.ItemType, "time_off_request", StringComparison.OrdinalIgnoreCase)))
            {
                requests.Insert(0, new WorkstationTimeOffRequestRecord
                {
                    Id = item.ClientEventId,
                    RequestType = item.RequestType,
                    StartDate = item.StartDate,
                    EndDate = item.EndDate,
                    HoursRequested = item.HoursRequested,
                    EmployeeNote = item.Note,
                    ManagerNote = "",
                    Status = "PENDING",
                    SubmittedUtc = item.CreatedLocalUtc,
                    IsPendingSync = true,
                    SyncState = item.SyncStatus,
                });
            }

            RecentTimeOffRequests.Clear();
            foreach (var request in requests.Take(12))
                RecentTimeOffRequests.Add(request);

            var derived = DeriveShiftState(basePunches);
            CurrentShiftStatus = ToShiftTitle(derived.Status);
            CurrentShiftDetail = BuildShiftDetail(derived.Status, derived.SinceUtc, derived.LastPunchUtc);
            if (_timeclockSnapshot != null)
            {
                _timeclockSnapshot.CurrentStatus = derived.Status;
                _timeclockSnapshot.StatusSinceUtc = derived.SinceUtc;
                _timeclockSnapshot.LastPunchUtc = derived.LastPunchUtc;
                _timeclockSnapshot.RecentPunches = RecentPunches.Select(ClonePunch).ToList();
                _timeclockSnapshot.RecentTimeOffRequests = RecentTimeOffRequests.Select(CloneRequest).ToList();
                WorkstationStorageService.SaveTimeclockState(_timeclockSnapshot);
            }
            OnPropertyChanged(nameof(RecentTimeOffRequests));
            OnPropertyChanged(nameof(RecentPunches));
            OnPropertyChanged(nameof(HasPendingSyncItems));
            UpdatePendingSummary();
            RefreshTimeClockPresentation();
        }

        private void UpdatePendingSummary()
        {
            var pending = PendingSyncItems.Count(item => string.Equals(item.SyncStatus, "pending", StringComparison.OrdinalIgnoreCase));
            var failed = PendingSyncItems.Count(item => string.Equals(item.SyncStatus, "failed", StringComparison.OrdinalIgnoreCase));
            PendingSyncSummary = pending == 0 && failed == 0
                ? "No pending sync items."
                : $"{pending} pending sync, {failed} failed.";
            RefreshTimeClockPresentation();
        }

        private void UpdateSessionCountdown()
        {
            OnPropertyChanged(nameof(HeaderDateText));
            OnPropertyChanged(nameof(HeaderTimeText));
            if (_timeclockSnapshot != null)
                RefreshTimeClockPresentation();

            if (CurrentSession == null)
            {
                SessionCountdown = "Signed out";
                RefreshConnectionStatuses();
                return;
            }

            if (!DateTime.TryParse(CurrentSession.ExpiresAtUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiresUtc))
            {
                SessionCountdown = "Session timing unavailable";
                RefreshConnectionStatuses();
                return;
            }

            var remaining = expiresUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                Logout();
                StatusText = "Session timed out. Passcode required.";
                return;
            }

            SessionCountdown = $"Session ends in {remaining:mm\\:ss}";
            OnPropertyChanged(nameof(TimeClockHeroSecondaryLine));
            RefreshConnectionStatuses();
        }

        private void RefreshTimeClockPresentation()
        {
            TodaySummaryRows.Clear();
            foreach (var row in BuildTodaySummaryRows())
                TodaySummaryRows.Add(row);

            WeeklySummaryRows.Clear();
            foreach (var row in BuildWeeklySummaryRows())
                WeeklySummaryRows.Add(row);

            CurrentStatusSummaryRows.Clear();
            foreach (var row in BuildCurrentStatusSummaryRows())
                CurrentStatusSummaryRows.Add(row);

            TimeClockActivityRows.Clear();
            foreach (var row in BuildTimeClockActivityRows())
                TimeClockActivityRows.Add(row);

            OnPropertyChanged(nameof(TimeClockStateKey));
            OnPropertyChanged(nameof(TimeClockHeroTitle));
            OnPropertyChanged(nameof(TimeClockHeroStatusLabel));
            OnPropertyChanged(nameof(TimeClockHeroHelperText));
            OnPropertyChanged(nameof(TimeClockHeroPrimaryLine));
            OnPropertyChanged(nameof(TimeClockHeroSecondaryLine));
            OnPropertyChanged(nameof(TimeClockHeroAccentBrush));
            OnPropertyChanged(nameof(TimeClockHeroBorderBrush));
            OnPropertyChanged(nameof(TimeClockHeroBackgroundBrush));
            OnPropertyChanged(nameof(ShowClockInAction));
            OnPropertyChanged(nameof(ShowWorkingActions));
            OnPropertyChanged(nameof(ShowBreakEndAction));
            OnPropertyChanged(nameof(ShowLunchEndAction));
            OnPropertyChanged(nameof(ShowLunchStartAction));
            OnPropertyChanged(nameof(HasRecentTimeOffRequests));
            OnPropertyChanged(nameof(ShowNoRecentTimeOffRequests));
            OnPropertyChanged(nameof(HasTimeClockActivity));
            OnPropertyChanged(nameof(ShowEmptyTimeClockActivity));
            OnPropertyChanged(nameof(TimeOffSectionSubtitle));
        }

        private IEnumerable<WorkstationTimeClockSummaryRow> BuildTimeClockSummaryRows()
        {
            foreach (var row in BuildTodaySummaryRows())
                yield return row;
        }

        private IEnumerable<WorkstationTimeClockSummaryRow> BuildTodaySummaryRows()
        {
            var today = DateTime.Now.Date;
            var summary = SummarizePunchRange(today, today.AddDays(1));

            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Clock In",
                Value = summary.FirstClockIn.HasValue ? summary.FirstClockIn.Value.ToString("h:mm tt", CultureInfo.InvariantCulture) : "--",
                AccentBrush = "#7FAEEA"
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Break Total",
                Value = FormatDuration(summary.BreakTotal),
                AccentBrush = "#E5B05F"
            };
            if (SupportsLunch)
            {
                yield return new WorkstationTimeClockSummaryRow
                {
                    Label = "Lunch Total",
                    Value = FormatDuration(summary.LunchTotal),
                    AccentBrush = "#7FAEEA"
                };
            }
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Total Worked",
                Value = FormatDuration(summary.WorkTotal),
                AccentBrush = "#62D89B"
            };
        }

        private IEnumerable<WorkstationTimeClockSummaryRow> BuildWeeklySummaryRows()
        {
            var now = DateTime.Now;
            var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
            var summary = SummarizePunchRange(startOfWeek, startOfWeek.AddDays(7));

            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Shifts",
                Value = summary.ClockInCount.ToString(CultureInfo.InvariantCulture),
                AccentBrush = "#7FAEEA"
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Worked",
                Value = FormatDuration(summary.WorkTotal),
                AccentBrush = "#62D89B"
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Break",
                Value = FormatDuration(summary.BreakTotal),
                AccentBrush = "#E5B05F"
            };
            if (SupportsLunch)
            {
                yield return new WorkstationTimeClockSummaryRow
                {
                    Label = "Lunch",
                    Value = FormatDuration(summary.LunchTotal),
                    AccentBrush = "#7FAEEA"
                };
            }
        }

        private IEnumerable<WorkstationTimeClockSummaryRow> BuildCurrentStatusSummaryRows()
        {
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Status",
                Value = CurrentShiftStatus,
                AccentBrush = TimeClockHeroAccentBrush
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = TimeClockStateKey == "out" ? "Last Action" : "Since",
                Value = BuildStatusTimeValue(),
                AccentBrush = TimeClockHeroAccentBrush
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Elapsed",
                Value = BuildElapsedSummaryValue(),
                AccentBrush = TimeClockHeroAccentBrush
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Sync",
                Value = BuildSyncSummaryValue(),
                AccentBrush = HasPendingSyncItems ? "#E5B05F" : "#62D89B"
            };
        }

        private TimeClockRangeSummary SummarizePunchRange(DateTime rangeStartLocal, DateTime rangeEndLocal)
        {
            var summary = new TimeClockRangeSummary();
            var orderedPunches = RecentPunches
                .Select(entry => new
                {
                    Punch = entry,
                    LocalTime = ToLocalTime(entry.ClientTs)
                })
                .Where(entry => entry.LocalTime.HasValue)
                .OrderBy(entry => entry.LocalTime!.Value)
                .ToList();

            var activeSegment = TimeClockSegment.None;
            DateTime? activeSegmentStart = null;
            var now = DateTime.Now < rangeEndLocal ? DateTime.Now : rangeEndLocal;

            foreach (var entry in orderedPunches)
            {
                var eventTime = entry.LocalTime!.Value;
                CloseSummarySegment(summary, activeSegment, activeSegmentStart, eventTime, rangeStartLocal, rangeEndLocal);

                var eventType = (entry.Punch.EventType ?? "").Trim().ToUpperInvariant();
                switch (eventType)
                {
                    case "CLOCK_IN":
                        if (eventTime >= rangeStartLocal && eventTime < rangeEndLocal)
                        {
                            summary.ClockInCount += 1;
                            summary.FirstClockIn ??= eventTime;
                        }

                        activeSegment = TimeClockSegment.Work;
                        activeSegmentStart = eventTime;
                        break;
                    case "BREAK_START":
                        activeSegment = TimeClockSegment.Break;
                        activeSegmentStart = eventTime;
                        break;
                    case "BREAK_END":
                        activeSegment = TimeClockSegment.Work;
                        activeSegmentStart = eventTime;
                        break;
                    case "LUNCH_START":
                        activeSegment = TimeClockSegment.Lunch;
                        activeSegmentStart = eventTime;
                        break;
                    case "LUNCH_END":
                        activeSegment = TimeClockSegment.Work;
                        activeSegmentStart = eventTime;
                        break;
                    case "CLOCK_OUT":
                        activeSegment = TimeClockSegment.None;
                        activeSegmentStart = null;
                        break;
                }
            }

            CloseSummarySegment(summary, activeSegment, activeSegmentStart, now, rangeStartLocal, rangeEndLocal);
            return summary;
        }

        private static void CloseSummarySegment(
            TimeClockRangeSummary summary,
            TimeClockSegment activeSegment,
            DateTime? segmentStart,
            DateTime segmentEnd,
            DateTime rangeStartLocal,
            DateTime rangeEndLocal)
        {
            if (!segmentStart.HasValue || activeSegment == TimeClockSegment.None || segmentEnd <= segmentStart.Value)
                return;

            var overlap = GetSegmentOverlap(segmentStart.Value, segmentEnd, rangeStartLocal, rangeEndLocal);
            if (overlap <= TimeSpan.Zero)
                return;

            switch (activeSegment)
            {
                case TimeClockSegment.Work:
                    summary.WorkTotal += overlap;
                    break;
                case TimeClockSegment.Break:
                    summary.BreakTotal += overlap;
                    break;
                case TimeClockSegment.Lunch:
                    summary.LunchTotal += overlap;
                    break;
            }
        }

        private static TimeSpan GetSegmentOverlap(DateTime start, DateTime end, DateTime rangeStart, DateTime rangeEnd)
        {
            var overlapStart = start > rangeStart ? start : rangeStart;
            var overlapEnd = end < rangeEnd ? end : rangeEnd;
            return overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
        }

        private string BuildStatusTimeValue()
        {
            if (TimeClockStateKey == "out")
                return string.IsNullOrWhiteSpace(_timeclockSnapshot?.LastPunchUtc) ? "--" : FormatLocalTime(_timeclockSnapshot?.LastPunchUtc ?? "");

            return string.IsNullOrWhiteSpace(_timeclockSnapshot?.StatusSinceUtc) ? "--" : FormatLocalTime(_timeclockSnapshot?.StatusSinceUtc ?? "");
        }

        private string BuildElapsedSummaryValue()
        {
            var elapsed = GetElapsedSince(_timeclockSnapshot?.StatusSinceUtc);
            return elapsed.HasValue && TimeClockStateKey != "out"
                ? FormatElapsed(elapsed.Value)
                : "--";
        }

        private string BuildSyncSummaryValue()
        {
            var pending = PendingSyncItems.Count(item => string.Equals(item.SyncStatus, "pending", StringComparison.OrdinalIgnoreCase));
            var failed = PendingSyncItems.Count(item => string.Equals(item.SyncStatus, "failed", StringComparison.OrdinalIgnoreCase));

            if (failed > 0)
                return failed == 1 ? "1 failed" : $"{failed} failed";
            if (pending > 0)
                return pending == 1 ? "1 pending" : $"{pending} pending";

            return "Synced";
        }

        private sealed class TimeClockRangeSummary
        {
            public DateTime? FirstClockIn { get; set; }
            public TimeSpan WorkTotal { get; set; }
            public TimeSpan BreakTotal { get; set; }
            public TimeSpan LunchTotal { get; set; }
            public int ClockInCount { get; set; }
        }

        private enum TimeClockSegment
        {
            None,
            Work,
            Break,
            Lunch
        }

        private IEnumerable<WorkstationTimeClockActivityRow> BuildTimeClockActivityRows()
        {
            foreach (var punch in RecentPunches.Take(8))
            {
                var syncState = string.IsNullOrWhiteSpace(punch.SyncState) ? "synced" : punch.SyncState.Trim();
                yield return new WorkstationTimeClockActivityRow
                {
                    Title = ToEventLabel((punch.EventType ?? "").Trim().ToLowerInvariant()),
                    TimeText = ToFriendlyPunchTime(punch.ClientTs),
                    StateText = ToFriendlySyncState(syncState),
                    DetailText = BuildPunchDetail(punch),
                    AccentBrush = GetActivityAccentBrush((punch.EventType ?? "").Trim(), syncState)
                };
            }
        }

        private string BuildHeroPrimaryLine()
        {
            return TimeClockStateKey switch
            {
                "working" => BuildSinceLine(_timeclockSnapshot?.StatusSinceUtc, "Clocked in"),
                "break" => BuildSinceLine(_timeclockSnapshot?.StatusSinceUtc, "Break started"),
                "lunch" => BuildSinceLine(_timeclockSnapshot?.StatusSinceUtc, "Lunch started"),
                _ => string.IsNullOrWhiteSpace(_timeclockSnapshot?.LastPunchUtc)
                    ? "No shift activity loaded yet."
                    : $"Last action {FormatDisplayDateTime(_timeclockSnapshot?.LastPunchUtc ?? "")}",
            };
        }

        private string BuildHeroSecondaryLine()
        {
            if (TimeClockStateKey == "out")
                return HasPendingSyncItems ? PendingSyncSummary : "Ready to start your shift.";

            var elapsed = GetElapsedSince(_timeclockSnapshot?.StatusSinceUtc);
            var elapsedLine = elapsed.HasValue ? $"Elapsed {FormatElapsed(elapsed.Value)}" : "";
            if (!string.IsNullOrWhiteSpace(PendingSyncSummary) && PendingSyncSummary != "No pending sync items.")
                return string.IsNullOrWhiteSpace(elapsedLine) ? PendingSyncSummary : $"{elapsedLine}  |  {PendingSyncSummary}";

            return string.IsNullOrWhiteSpace(elapsedLine) ? OfflineStatus : elapsedLine;
        }

        private static string GetShiftStateKey(string status)
        {
            return (status ?? "").Trim().ToUpperInvariant() switch
            {
                "CLOCKED IN" => "working",
                "ON BREAK" => "break",
                "ON LUNCH" => "lunch",
                _ => "out"
            };
        }

        private static string BuildSinceLine(string? utc, string prefix)
        {
            var local = ToLocalTime(utc ?? "");
            return local.HasValue
                ? $"{prefix} at {local.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)}"
                : $"{prefix} time unavailable";
        }

        private static DateTime? ToLocalTime(string utc)
        {
            return DateTime.TryParse(utc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToLocalTime()
                : null;
        }

        private static TimeSpan? GetElapsedSince(string? utc)
        {
            var local = ToLocalTime(utc ?? "");
            if (!local.HasValue)
                return null;

            var elapsed = DateTime.Now - local.Value;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        private static string FormatElapsed(TimeSpan value)
        {
            var totalHours = (int)Math.Floor(value.TotalHours);
            return $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
        }

        private static string FormatDuration(TimeSpan value)
        {
            var totalHours = (int)Math.Floor(value.TotalHours);
            return $"{totalHours:00}:{value.Minutes:00}";
        }

        private static string FormatDisplayDateTime(string utc)
        {
            var local = ToLocalTime(utc);
            return local.HasValue
                ? local.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)
                : "time unavailable";
        }

        private static string ToFriendlyPunchTime(string utc)
        {
            var local = ToLocalTime(utc);
            return local.HasValue
                ? local.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)
                : "Time unavailable";
        }

        private static string ToFriendlySyncState(string syncState)
        {
            return (syncState ?? "").Trim().ToLowerInvariant() switch
            {
                "failed" => "Sync failed",
                "pending" => "Pending sync",
                "synced" => "Synced",
                _ => string.IsNullOrWhiteSpace(syncState) ? "Unknown state" : syncState
            };
        }

        private static string BuildPunchDetail(WorkstationPunchRecord punch)
        {
            var source = string.IsNullOrWhiteSpace(punch.Source) ? "Workstation" : punch.Source.Trim();
            return string.IsNullOrWhiteSpace(punch.Note)
                ? source
                : $"{source}  |  {punch.Note.Trim()}";
        }

        private static string GetActivityAccentBrush(string eventType, string syncState)
        {
            if (string.Equals(syncState, "failed", StringComparison.OrdinalIgnoreCase))
                return "#E77D87";
            if (string.Equals(syncState, "pending", StringComparison.OrdinalIgnoreCase))
                return "#E5B05F";

            return (eventType ?? "").Trim().ToUpperInvariant() switch
            {
                "BREAK_START" or "BREAK_END" => "#E5B05F",
                "LUNCH_START" or "LUNCH_END" => "#7FAEEA",
                "CLOCK_IN" => "#62D89B",
                "CLOCK_OUT" => "#E77D87",
                _ => "#7EABD9"
            };
        }

        private bool ValidateControlSettings()
        {
            SaveSettings();
            if (string.IsNullOrWhiteSpace(Settings.ControlBaseUrl))
            {
                StatusText = "Control base URL is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Settings.ShopId))
            {
                StatusText = "Shop ID is required before workstation login.";
                return false;
            }

            if (!ControlSessionService.CanAuthenticate(Settings, out var reason))
            {
                StatusText = reason;
                return false;
            }

            return true;
        }

        private bool ValidateLocalAuthSettings()
        {
            SaveSettings();
            if (string.IsNullOrWhiteSpace(Settings.ShopId))
            {
                StatusText = "Shop ID is required before workstation login.";
                return false;
            }

            if (_authCache?.Package == null || _authCache.Package.Employees.Count == 0)
            {
                StatusText = "No Desktop-issued employee auth cache is available yet.";
                return false;
            }

            return true;
        }

        private void NormalizeLocalShopScope()
        {
            var preferredShopId = !string.IsNullOrWhiteSpace(Registration?.ShopId)
                ? (Registration?.ShopId ?? "").Trim()
                : (_authCache?.Package?.ShopId ?? "").Trim();
            var preferredWorkstationId = !string.IsNullOrWhiteSpace(Registration?.WorkstationId)
                ? (Registration?.WorkstationId ?? "").Trim()
                : (_authCache?.Package?.WorkstationId ?? "").Trim();
            var preferredWorkstationName = !string.IsNullOrWhiteSpace(Registration?.WorkstationName)
                ? (Registration?.WorkstationName ?? "").Trim()
                : Settings.WorkstationName;
            var changed = false;

            if (!string.IsNullOrWhiteSpace(preferredShopId) && !string.Equals(Settings.ShopId, preferredShopId, StringComparison.OrdinalIgnoreCase))
            {
                Settings.ShopId = preferredShopId;
                SettingsShopId = preferredShopId;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(preferredWorkstationId) && !string.Equals(Settings.WorkstationId, preferredWorkstationId, StringComparison.OrdinalIgnoreCase))
            {
                Settings.WorkstationId = preferredWorkstationId;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(preferredWorkstationName) && !string.Equals(Settings.WorkstationName, preferredWorkstationName, StringComparison.Ordinal))
            {
                Settings.WorkstationName = preferredWorkstationName;
                SettingsWorkstationName = preferredWorkstationName;
                changed = true;
            }

            if (!changed)
                return;

            WorkstationStorageService.SaveSettings(Settings);
            OnPropertyChanged(nameof(ConnectivityLine));
            OnPropertyChanged(nameof(WorkstationIdentityLine));
        }

        private void HandleTrustFailure(string message)
        {
            MarkDesktopRequestSuccess();
            Registration = null;
            WorkstationStorageService.SaveRegistration(null);
            _authCache = null;
            WorkstationStorageService.SaveAuthCache(null);
            RosterEmployees.Clear();
            if (CurrentSession != null)
                Logout();
            OfflineStatus = "Desktop enrollment is no longer valid for this workstation.";
            UpdateAuthCacheStatus(new WorkstationAuthCacheHealth
            {
                State = WorkstationAuthCacheState.Expired,
                Message = "Desktop workstation trust was revoked or replaced. Re-enroll with a new pairing code."
            });
            StatusText = message;
        }

        private static bool IsTrustFailure(string message)
        {
            return StartsWithErrorCode(message, "WORKSTATION_REVOKED")
                || StartsWithErrorCode(message, "WORKSTATION_TOKEN_INVALID")
                || StartsWithErrorCode(message, "WORKSTATION_NOT_ENROLLED")
                || StartsWithErrorCode(message, "WORKSTATION_IDENTITY_MISMATCH");
        }

        private static bool IsEmployeeSessionFailure(string message)
        {
            return StartsWithErrorCode(message, "WORKSTATION_SESSION_REQUIRED")
                || StartsWithErrorCode(message, "WORKSTATION_SESSION_INVALID")
                || StartsWithErrorCode(message, "WORKSTATION_SESSION_EXPIRED")
                || StartsWithErrorCode(message, "WORKSTATION_SESSION_MISMATCH");
        }

        private static bool StartsWithErrorCode(string message, string code)
        {
            return !string.IsNullOrWhiteSpace(message)
                && message.StartsWith(code + ":", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePairingCode(string value)
            => new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).Trim();

        private async Task InitializeFromControlSessionAsync()
        {
            if (ControlSessionService.HasSession())
            {
                await RunBusyAsync(async () =>
                {
                    await SyncCurrentShopAsync();
                    await RefreshAuthPackageAsyncCore(false);
                    await LoadRosterCoreAsync();
                });
                return;
            }

            await RefreshAuthPackageAsyncCore(false);
        }

        private async Task SyncCurrentShopAsync()
        {
            if (!ControlSessionService.HasSession() || string.IsNullOrWhiteSpace(Settings.ControlBaseUrl))
                return;

            var currentShop = await _api.GetCurrentShopAsync(Settings.ControlBaseUrl, CancellationToken.None);
            var desktopShopId = GetTrustedDesktopShopId();
            if (!currentShop.Found || string.IsNullOrWhiteSpace(currentShop.ShopId))
            {
                if (!string.IsNullOrWhiteSpace(desktopShopId))
                    return;

                Settings.ShopId = "";
                Settings.ShopName = "Unassigned shop";
                SettingsShopId = "";
                SettingsShopName = Settings.ShopName;
                WorkstationStorageService.SaveSettings(Settings);
                OnPropertyChanged(nameof(ConnectivityLine));
                return;
            }

            if (!string.IsNullOrWhiteSpace(desktopShopId) &&
                !string.Equals(currentShop.ShopId, desktopShopId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Settings.ShopId = currentShop.ShopId;
            Settings.ShopName = string.IsNullOrWhiteSpace(currentShop.ShopName) ? Settings.ShopName : currentShop.ShopName;
            SettingsShopId = Settings.ShopId;
            SettingsShopName = Settings.ShopName;
            WorkstationStorageService.SaveSettings(Settings);
            OnPropertyChanged(nameof(ConnectivityLine));
        }

        private string GetTrustedDesktopShopId()
        {
            if (!string.IsNullOrWhiteSpace(Registration?.ShopId))
                return (Registration?.ShopId ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(_authCache?.Package?.ShopId))
                return (_authCache?.Package?.ShopId ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(CurrentSession?.ShopId))
                return (CurrentSession?.ShopId ?? "").Trim();

            return "";
        }

        private async Task LoadRosterAsync()
        {
            if (IsBusy)
                return;

            if (!CanLoadRoster())
                return;

            await RunBusyAsync(LoadRosterCoreAsync);
        }

        private Task LoadRosterCoreAsync()
        {
            LoadRosterFromAuthCache();
            if (RosterEmployees.Count == 0)
                StatusText = "No workstation-ready employees found in the Desktop auth cache.";
            return Task.CompletedTask;
        }

        private bool CanLoadRoster()
        {
            return !string.IsNullOrWhiteSpace(Settings.ShopId)
                && _authCache?.Package != null
                && string.Equals(_authCache.Package.ShopId, Settings.ShopId, StringComparison.OrdinalIgnoreCase);
        }

        private void LoadRosterFromAuthCache()
        {
            RosterEmployees.Clear();
            if (!CanLoadRoster())
                return;

            var employees = _authCache!.Package.Employees
                .Where(employee => employee.IsActive)
                .Where(employee => employee.WorkstationAccessEnabled)
                .Take(20)
                .Select(MapRosterEmployee)
                .ToList();

            foreach (var employee in employees)
                RosterEmployees.Add(employee);
        }

        private WorkstationRosterEmployee? FindRosterEmployee(string remoteEmployeeId, string employeeId, string employeeCode)
        {
            return RosterEmployees.FirstOrDefault(employee =>
                (!string.IsNullOrWhiteSpace(remoteEmployeeId) && string.Equals(employee.RemoteEmployeeId, remoteEmployeeId, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(employee.EmployeeId, employeeId, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(employeeCode) && string.Equals(employee.EmployeeCode, employeeCode, StringComparison.OrdinalIgnoreCase)));
        }

        private WorkstationAuthCacheHealth GetAuthCacheHealth()
            => WorkstationEmployeeAuthService.EvaluateCache(_authCache, DateTime.UtcNow);

        private void UpdateAuthCacheStatus(WorkstationAuthCacheHealth? health = null)
        {
            health ??= GetAuthCacheHealth();
            var suffix = string.IsNullOrWhiteSpace(_authCache?.LastRefreshError)
                ? ""
                : $" Last error: {_authCache.LastRefreshError}";
            AuthCacheStatus = health.Message + suffix;
        }

        private static WorkstationEmployeeAuthPackage MapAuthPackage(RunBookWorkstationApiClient.LocalAuthPackage? package)
        {
            var model = new WorkstationEmployeeAuthPackage();
            if (package == null)
                return model;

            model.ShopId = package.ShopId;
            model.WorkstationId = package.WorkstationId;
            model.AuthPackageVersion = package.AuthPackageVersion;
            model.PackageHash = package.PackageHash;
            model.LastSyncedUtc = package.LastSyncedUtc;
            model.SourceState = package.SourceState;
            model.Policy = new WorkstationEmployeeAuthPolicy
            {
                WarningAfterHours = package.Policy?.WarningAfterHours ?? 24,
                HardStopAfterHours = package.Policy?.HardStopAfterHours ?? 168,
            };
            model.Employees = package.Employees.Select(MapAuthEmployee).ToList();
            return model;
        }

        private static WorkstationCapabilityPayload MapCapabilityPayload(RunBookWorkstationApiClient.CapabilityPayload? payload)
        {
            var model = new WorkstationCapabilityPayload();
            if (payload == null)
                return model;

            model.Employee = new WorkstationCapabilityEmployee
            {
                RemoteEmployeeId = payload.Employee?.RemoteEmployeeId ?? "",
                EmployeeId = payload.Employee?.EmployeeId ?? "",
                EmployeeCode = payload.Employee?.EmployeeCode ?? "",
                DisplayName = payload.Employee?.DisplayName ?? "",
                Role = payload.Employee?.Role ?? "",
                SessionTimeoutMinutes = payload.Employee?.SessionTimeoutMinutes ?? 15,
            };
            model.Workstation = new WorkstationCapabilityWorkstation
            {
                ShopId = payload.Workstation?.ShopId ?? "",
                ShopName = payload.Workstation?.ShopName ?? "",
                DeviceId = payload.Workstation?.DeviceId ?? "",
                DeviceName = payload.Workstation?.DeviceName ?? "",
            };
            model.Modules = new Dictionary<string, bool>(payload.Modules ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase);
            model.Actions = new Dictionary<string, bool>(payload.Actions ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase);
            model.CurrentJob = payload.CurrentJob == null
                ? null
                : new WorkstationCurrentJobContext
                {
                    WorkOrderId = payload.CurrentJob.WorkOrderId,
                    WorkOrderNumber = payload.CurrentJob.WorkOrderNumber,
                    PartNumber = payload.CurrentJob.PartNumber,
                    PartDescription = payload.CurrentJob.PartDescription,
                    VisibilitySource = payload.CurrentJob.VisibilitySource,
                    OperationId = payload.CurrentJob.OperationId,
                    OperationNumber = payload.CurrentJob.OperationNumber,
                    OperationTitle = payload.CurrentJob.OperationTitle,
                    OperationStatus = payload.CurrentJob.OperationStatus,
                    StartedUtc = payload.CurrentJob.StartedUtc,
                    AssignmentLabel = payload.CurrentJob.AssignmentLabel,
                };
            model.RecentJob = payload.RecentJob == null
                ? null
                : new WorkstationCurrentJobContext
                {
                    WorkOrderId = payload.RecentJob.WorkOrderId,
                    WorkOrderNumber = payload.RecentJob.WorkOrderNumber,
                    PartNumber = payload.RecentJob.PartNumber,
                    PartDescription = payload.RecentJob.PartDescription,
                    VisibilitySource = payload.RecentJob.VisibilitySource,
                    OperationId = payload.RecentJob.OperationId,
                    OperationNumber = payload.RecentJob.OperationNumber,
                    OperationTitle = payload.RecentJob.OperationTitle,
                    OperationStatus = payload.RecentJob.OperationStatus,
                    StartedUtc = payload.RecentJob.StartedUtc,
                    AssignmentLabel = payload.RecentJob.AssignmentLabel,
                };
            return model;
        }

        private static WorkstationAuthPackageEmployee MapAuthEmployee(RunBookWorkstationApiClient.LocalAuthEmployee employee)
        {
            return new WorkstationAuthPackageEmployee
            {
                EmployeeId = employee.EmployeeId,
                RemoteEmployeeId = employee.RemoteEmployeeId,
                EmployeeCode = employee.EmployeeCode,
                DisplayName = employee.DisplayName,
                Role = employee.Role,
                Status = employee.Status,
                IsActive = employee.IsActive,
                WorkstationAccessEnabled = employee.WorkstationAccessEnabled,
                CanTimeClock = employee.CanTimeClock,
                CanDashboardView = employee.CanDashboardView,
                CanJobsModule = employee.CanJobsModule,
                CanInspectionEntry = employee.CanInspectionEntry,
                CanCameraView = employee.CanCameraView,
                HasWorkstationPasscode = employee.HasWorkstationPasscode,
                SessionTimeoutMinutes = employee.SessionTimeoutMinutes < 1 ? 15 : employee.SessionTimeoutMinutes,
                AvatarDisplayUrl = employee.AvatarDisplayUrl,
                UpdatedUtc = employee.UpdatedUtc,
            };
        }

        private static WorkstationRosterEmployee MapRosterEmployee(WorkstationAuthPackageEmployee employee)
        {
            var displayName = string.IsNullOrWhiteSpace(employee.DisplayName) ? employee.EmployeeCode : employee.DisplayName;
            return new WorkstationRosterEmployee
            {
                RemoteEmployeeId = employee.RemoteEmployeeId,
                EmployeeId = employee.EmployeeId,
                EmployeeCode = employee.EmployeeCode,
                DisplayName = displayName,
                Role = ToRosterRole(employee.Role, employee),
                Initials = BuildInitials(displayName),
                AvatarDisplayUrl = employee.AvatarDisplayUrl,
                AvatarBrush = PickAvatarBrush(displayName),
                AccentBrush = PickAccentBrush(displayName),
                AccessSummary = BuildAccessSummary(employee),
                SessionTimeoutMinutes = employee.SessionTimeoutMinutes < 1 ? 15 : employee.SessionTimeoutMinutes,
                CanTimeClock = employee.CanTimeClock,
                CanDashboardView = employee.CanDashboardView,
                CanJobsModule = employee.CanJobsModule,
                CanInspectionEntry = employee.CanInspectionEntry,
                CanCameraView = employee.CanCameraView,
                HasWorkstationPasscode = employee.HasWorkstationPasscode,
            };
        }

        private static WorkstationWorkOrderSummary MapWorkOrderSummary(RunBookWorkstationApiClient.WorkOrderSummary summary)
        {
            return new WorkstationWorkOrderSummary
            {
                WorkOrderId = summary.WorkOrderId,
                WorkOrderNumber = summary.WorkOrderNumber,
                PartNumber = summary.PartNumber,
                PartDescription = summary.PartDescription,
                Revision = summary.Revision,
                Status = summary.Status,
                Quantity = summary.Quantity,
                DueDate = summary.DueDate,
                OperationCount = summary.OperationCount,
                HasDrawings = summary.HasDrawings,
                ReleasedUtc = summary.ReleasedUtc,
                VisibilitySource = summary.VisibilitySource,
                IsCurrentJob = summary.IsCurrentJob,
                IsAssigned = summary.IsAssigned,
                IsDepartmentMatch = summary.IsDepartmentMatch,
                IsBackupJob = summary.IsBackupJob,
                ActiveOperationId = summary.ActiveOperationId,
                ActiveOperationNumber = summary.ActiveOperationNumber,
                ActiveOperationTitle = summary.ActiveOperationTitle,
                AssignmentLabel = summary.AssignmentLabel,
                ProgressPercent = summary.ProgressPercent,
                CompletedOperations = summary.CompletedOperations,
                TotalOperations = summary.TotalOperations,
                OperationSummary = MapOperationStatusSummary(summary.OperationSummary),
                ActiveOperators = summary.ActiveOperators.Select(MapActiveOperator).ToList(),
            };
        }

        private static WorkstationWorkOrderDetail? MapWorkOrderDetail(RunBookWorkstationApiClient.WorkOrderDetail? detail)
        {
            if (detail == null)
                return null;

            return new WorkstationWorkOrderDetail
            {
                WorkOrderId = detail.WorkOrderId,
                WorkOrderNumber = detail.WorkOrderNumber,
                PartNumber = detail.PartNumber,
                PartDescription = detail.PartDescription,
                Revision = detail.Revision,
                Status = detail.Status,
                Quantity = detail.Quantity,
                DueDate = detail.DueDate,
                CustomerName = detail.CustomerName,
                PoNumber = detail.PoNumber,
                SourceComponentId = detail.SourceComponentId,
                ReleaseState = detail.ReleaseState,
                ReleasedUtc = detail.ReleasedUtc,
                SnapshotLoadSource = detail.SnapshotLoadSource,
                HasDrawings = detail.HasDrawings,
                VisibilitySource = detail.VisibilitySource,
                IsCurrentJob = detail.IsCurrentJob,
                IsAssigned = detail.IsAssigned,
                AssignmentLabel = detail.AssignmentLabel,
                ProgressPercent = detail.ProgressPercent,
                CompletedOperations = detail.CompletedOperations,
                TotalOperations = detail.TotalOperations,
                OperationSummary = MapOperationStatusSummary(detail.OperationSummary),
                ActiveOperators = detail.ActiveOperators.Select(MapActiveOperator).ToList(),
                Operations = detail.Operations.Select(operation => new WorkstationWorkOrderOperationSummary
                {
                    OperationId = operation.OperationId,
                    OperationNumber = operation.OperationNumber,
                    Title = operation.Title,
                    Department = operation.Department,
                    WorkCenter = operation.WorkCenter,
                    Status = operation.Status,
                    OperatorName = operation.OperatorName,
                    MachineName = operation.MachineName,
                    StartedUtc = operation.StartedUtc,
                    CompletedUtc = operation.CompletedUtc,
                    CanStart = operation.CanStart,
                    CanStop = operation.CanStop,
                    CanComplete = operation.CanComplete,
                }).ToList(),
            };
        }

        private static WorkstationCurrentJobContext MapCurrentJobContext(RunBookWorkstationApiClient.CurrentJobContext currentJob)
        {
            return new WorkstationCurrentJobContext
            {
                WorkOrderId = currentJob.WorkOrderId,
                WorkOrderNumber = currentJob.WorkOrderNumber,
                PartNumber = currentJob.PartNumber,
                PartDescription = currentJob.PartDescription,
                VisibilitySource = currentJob.VisibilitySource,
                OperationId = currentJob.OperationId,
                OperationNumber = currentJob.OperationNumber,
                OperationTitle = currentJob.OperationTitle,
                OperationStatus = currentJob.OperationStatus,
                StartedUtc = currentJob.StartedUtc,
                AssignmentLabel = currentJob.AssignmentLabel,
            };
        }

        private static WorkstationOperationStatusSummary MapOperationStatusSummary(RunBookWorkstationApiClient.OperationStatusSummary? summary)
        {
            return new WorkstationOperationStatusSummary
            {
                NotStarted = summary?.NotStarted ?? 0,
                InProgress = summary?.InProgress ?? 0,
                Completed = summary?.Completed ?? 0,
            };
        }

        private static WorkstationActiveOperator MapActiveOperator(RunBookWorkstationApiClient.ActiveOperator activeOperator)
        {
            return new WorkstationActiveOperator
            {
                EmployeeId = activeOperator.EmployeeId,
                EmployeeCode = activeOperator.EmployeeCode,
                EmployeeName = activeOperator.EmployeeName,
                OperationId = activeOperator.OperationId,
                OperationNumber = activeOperator.OperationNumber,
                OperationTitle = activeOperator.OperationTitle,
                WorkstationId = activeOperator.WorkstationId,
                WorkstationName = activeOperator.WorkstationName,
                StartedUtc = activeOperator.StartedUtc,
            };
        }

        private static WorkstationShopAwareness MapShopAwareness(RunBookWorkstationApiClient.ShopAwareness? awareness)
        {
            return new WorkstationShopAwareness
            {
                Summary = new WorkstationShopSummary
                {
                    ActiveJobs = awareness?.Summary?.ActiveJobs ?? 0,
                    ActiveOperators = awareness?.Summary?.ActiveOperators ?? 0,
                    WaitingJobs = awareness?.Summary?.WaitingJobs ?? 0,
                    CompletedJobs = awareness?.Summary?.CompletedJobs ?? 0,
                    IdleOperators = awareness?.Summary?.IdleOperators ?? 0,
                    VisibleJobs = awareness?.Summary?.VisibleJobs ?? 0,
                },
                Operators = awareness?.Operators?.Select(candidate => new WorkstationOperatorAwareness
                {
                    OperatorId = candidate.OperatorId,
                    OperatorName = candidate.OperatorName,
                    EmployeeCode = candidate.EmployeeCode,
                    Status = candidate.Status,
                    WorkOrderId = candidate.WorkOrderId,
                    WorkOrderNumber = candidate.WorkOrderNumber,
                    OperationId = candidate.OperationId,
                    OperationNumber = candidate.OperationNumber,
                    WorkstationName = candidate.WorkstationName,
                    StartedUtc = candidate.StartedUtc,
                }).ToList() ?? new List<WorkstationOperatorAwareness>(),
            };
        }

        private static WorkstationDrawingReference MapDrawingReference(RunBookWorkstationApiClient.DrawingReference drawing)
        {
            return new WorkstationDrawingReference
            {
                DrawingId = drawing.DrawingId,
                Label = drawing.Label,
                Revision = drawing.Revision,
                PageCount = drawing.PageCount,
                IsPrimary = drawing.IsPrimary,
                ContentType = drawing.ContentType,
                DownloadRoute = drawing.DownloadRoute,
            };
        }

        private static WorkstationInspectionTaskPackage? MapInspectionTaskPackage(RunBookWorkstationApiClient.InspectionTaskPackage? package)
        {
            if (package == null)
                return null;

            return new WorkstationInspectionTaskPackage
            {
                WorkOrderId = package.WorkOrderId,
                OperationId = package.OperationId,
                FeatureSetId = package.FeatureSetId,
                FeatureSetName = package.FeatureSetName,
                TemplateKey = package.TemplateKey,
                SessionId = package.SessionId,
                Tasks = package.Tasks.Select(task => new WorkstationInspectionTask
                {
                    FeatureId = task.FeatureId,
                    BalloonNumber = task.BalloonNumber,
                    ItemNumber = task.ItemNumber,
                    Zone = task.Zone,
                    FeatureText = task.FeatureText,
                    Nominal = task.Nominal,
                    TolPlus = task.TolPlus,
                    TolMinus = task.TolMinus,
                    Units = task.Units,
                    Classification = task.Classification,
                    InspectionMethod = task.InspectionMethod,
                    Frequency = task.Frequency,
                    InputType = task.InputType,
                    InputOptionsJson = task.InputOptionsJson,
                    Notes = task.Notes,
                    SampleIndex = task.SampleIndex < 1 ? 1 : task.SampleIndex,
                    ActualValue = task.ActualValue,
                    PassFail = task.PassFail,
                    Inspector = task.Inspector,
                    MeasuredUtc = task.MeasuredUtc,
                    ResultNotes = task.ResultNotes,
                }).ToList()
            };
        }

        private static string BuildInitials(string displayName)
        {
            var parts = (displayName ?? "")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(part => char.ToUpperInvariant(part[0]));
            var initials = new string(parts.ToArray());
            return string.IsNullOrWhiteSpace(initials) ? "RB" : initials;
        }

        private static string BuildAccessSummary(WorkstationAuthPackageEmployee employee)
        {
            var modules = new List<string>();
            if (employee.CanTimeClock) modules.Add("Time Clock");
            if (employee.CanJobsModule) modules.Add("Work Orders");
            if (employee.CanJobsModule) modules.Add("Drawings");
            if (employee.CanInspectionEntry) modules.Add("Inspection");
            var summary = modules.Count == 0 ? "No workstation modules assigned" : string.Join(" + ", modules);
            return employee.HasWorkstationPasscode
                ? summary
                : summary + "  |  Passcode not configured";
        }

        private static string ToRosterRole(string role, WorkstationAuthPackageEmployee employee)
        {
            var cleanRole = (role ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(cleanRole) && !string.Equals(cleanRole, "employee", StringComparison.OrdinalIgnoreCase))
                return cleanRole;

            if (employee.CanJobsModule) return "Operator";
            return "Employee";
        }

        private static string PickAvatarBrush(string displayName)
        {
            string[] palette = { "#5F8FD9", "#5DAE8D", "#8B78D8", "#D98B5F", "#C86484", "#5A9CC1", "#C7A34E", "#6AAC96" };
            var index = Math.Abs((displayName ?? "").GetHashCode()) % palette.Length;
            return palette[index];
        }

        private static string PickAccentBrush(string displayName)
        {
            string[] palette = { "#A9C7FF", "#A8E6CF", "#CAB8FF", "#F4C29A", "#FFBFD9", "#9ED9F6", "#F0DA98", "#AFE8D3" };
            var index = Math.Abs((displayName ?? "").GetHashCode()) % palette.Length;
            return palette[index];
        }

        private async Task RunBusyAsync(Func<Task> action)
        {
            if (IsBusy)
                return;

            IsBusy = true;
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                DebugLogService.WriteException("Workstation operation failed", ex);
                var rawMessage = ex.Message ?? "";
                if (IsTrustFailure(rawMessage))
                {
                    HandleTrustFailure(rawMessage);
                    return;
                }
                if (IsEmployeeSessionFailure(rawMessage))
                {
                    HandleEmployeeSessionFailure(rawMessage);
                    return;
                }

                StatusText = GetUserFacingErrorMessage(ex);
                if (IsConnectivityFailure(ex))
                {
                    MarkDesktopRequestFailure(ex.Message);
                    OfflineStatus = "Unable to reach RunBook service.";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static bool IsConnectivityFailure(Exception ex)
        {
            if (ex is TimeoutException)
                return true;

            if (ex is TaskCanceledException)
                return true;

            if (ex is System.Net.Http.HttpRequestException)
                return true;

            if (ex is IOException ioEx && ioEx.InnerException is System.Net.Sockets.SocketException)
                return true;

            return ex.InnerException is System.Net.Sockets.SocketException;
        }

        private static string GetUserFacingErrorMessage(Exception ex)
        {
            var message = (ex?.Message ?? "").Trim();
            if (message.Length == 0)
                return "RunBook could not complete that action.";

                return message switch
                {
                    "Workstation request does not match the active Desktop shop." => "This workstation is pointed at a different Desktop shop. Refresh registration and employee auth, then try again.",
                    "Work order is not available to Workstation." => "This job is no longer available on this workstation.",
                    "Work order is visible on Workstation but is not currently executable by this employee." => "This job is visible here, but you cannot work it right now.",
                    "This work order is not currently executable by this employee." => "This job is not currently executable for you.",
                  "Work order is not open for workstation execution." => "This job is no longer open for workstation execution.",
                  "Work order is not open for workstation reporting." => "This job is no longer open for reporting.",
                  "WORKSTATION_SESSION_REQUIRED: Workstation sign-in is required." => "Your workstation sign-in is required. Please sign in again.",
                  "WORKSTATION_SESSION_INVALID: Workstation sign-in is no longer valid." => "Your workstation sign-in is no longer valid. Please sign in again.",
                  "WORKSTATION_SESSION_EXPIRED: Workstation sign-in expired. Please sign in again." => "Your workstation sign-in expired. Please sign in again.",
                  "WORKSTATION_SESSION_MISMATCH: Workstation sign-in no longer matches this trusted device." => "This workstation sign-in no longer matches the trusted device. Please sign in again.",
                  _ => message,
              };
        }

        private void HandleEmployeeSessionFailure(string message)
        {
            var userMessage = GetUserFacingErrorMessage(new InvalidOperationException(message));
            MarkDesktopRequestSuccess();
            Logout();
            OfflineStatus = "Desktop connected, but the workstation sign-in is no longer valid.";
            StatusText = userMessage;
        }

        private void MarkDesktopRequestSuccess()
        {
            _lastDesktopSuccessUtc = DateTime.UtcNow;
            _lastDesktopFailureReason = "";
            RefreshConnectionStatuses();
        }

        private void MarkDesktopRequestFailure(string? reason)
        {
            _lastDesktopFailureUtc = DateTime.UtcNow;
            _lastDesktopFailureReason = reason ?? "";
            RefreshConnectionStatuses();
        }

        private void RefreshConnectionStatuses()
        {
            var statuses = _connectionStatusService.BuildStatuses(
                Settings,
                Registration,
                CurrentSession,
                _lastDesktopSuccessUtc,
                _lastDesktopFailureUtc,
                _lastDesktopFailureReason);

            ConnectionStatuses.Clear();
            foreach (var status in statuses)
                ConnectionStatuses.Add(status);
        }

        private static WorkstationTimeclockSnapshot MapSnapshot(RunBookWorkstationApiClient.DesktopTimeclockSnapshot? snapshot)
        {
            var model = new WorkstationTimeclockSnapshot();
            if (snapshot == null)
                return model;

            model.EmployeeId = snapshot.EmployeeId;
            model.RemoteEmployeeId = snapshot.RemoteEmployeeId;
            model.EmployeeCode = snapshot.EmployeeCode;
            model.EmployeeName = snapshot.EmployeeName;
            model.CurrentStatus = snapshot.CurrentStatus;
            model.StatusSinceUtc = snapshot.StatusSinceUtc;
            model.LastPunchUtc = snapshot.LastPunchUtc;
            model.SupportsLunch = snapshot.SupportsLunch;
            model.LastSyncUtc = snapshot.LastSyncUtc;
            model.LastSyncMessage = snapshot.LastSyncMessage;
            model.RecentPunches = snapshot.RecentPunches.Select(punch => new WorkstationPunchRecord
            {
                Id = punch.Id,
                EventType = punch.EventType,
                ClientTs = punch.ClientTs,
                ServerTs = punch.ServerTs,
                Source = punch.Source,
                Note = punch.Note,
                IsPendingSync = punch.IsPendingSync,
                SyncState = string.IsNullOrWhiteSpace(punch.SyncState) ? "synced" : punch.SyncState,
            }).ToList();
            model.RecentTimeOffRequests = snapshot.RecentTimeOffRequests.Select(request => new WorkstationTimeOffRequestRecord
            {
                Id = request.Id,
                RequestType = request.RequestType,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                HoursRequested = request.HoursRequested,
                EmployeeNote = request.EmployeeNote,
                ManagerNote = request.ManagerNote,
                Status = request.Status,
                SubmittedUtc = request.SubmittedUtc,
                IsPendingSync = request.IsPendingSync,
                SyncState = string.IsNullOrWhiteSpace(request.SyncState) ? "synced" : request.SyncState,
            }).ToList();
            return model;
        }

        private static WorkstationPunchRecord ClonePunch(WorkstationPunchRecord punch)
        {
            return new WorkstationPunchRecord
            {
                Id = punch.Id,
                EventType = punch.EventType,
                ClientTs = punch.ClientTs,
                ServerTs = punch.ServerTs,
                Source = punch.Source,
                Note = punch.Note,
                IsPendingSync = punch.IsPendingSync,
                SyncState = punch.SyncState
            };
        }

        private static WorkstationTimeOffRequestRecord CloneRequest(WorkstationTimeOffRequestRecord request)
        {
            return new WorkstationTimeOffRequestRecord
            {
                Id = request.Id,
                RequestType = request.RequestType,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                HoursRequested = request.HoursRequested,
                EmployeeNote = request.EmployeeNote,
                ManagerNote = request.ManagerNote,
                Status = request.Status,
                SubmittedUtc = request.SubmittedUtc,
                IsPendingSync = request.IsPendingSync,
                SyncState = request.SyncState
            };
        }

        private static (string Status, string SinceUtc, string LastPunchUtc) DeriveShiftState(IEnumerable<WorkstationPunchRecord> punches)
        {
            string status = "OUT";
            string sinceUtc = "";
            string lastPunchUtc = "";
            string workSince = "";

            foreach (var punch in punches.OrderBy(item => ParseUtc(item.ClientTs)).ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                var normalized = (punch.EventType ?? "").Trim().ToUpperInvariant();
                switch (normalized)
                {
                    case "CLOCK_IN":
                        if (workSince.Length == 0)
                            workSince = punch.ClientTs;
                        status = "IN_WORKING";
                        sinceUtc = workSince;
                        lastPunchUtc = punch.ClientTs;
                        break;
                    case "CLOCK_OUT":
                        status = "OUT";
                        sinceUtc = "";
                        lastPunchUtc = punch.ClientTs;
                        workSince = "";
                        break;
                    case "BREAK_START":
                        if (workSince.Length > 0)
                        {
                            status = "IN_BREAK";
                            sinceUtc = punch.ClientTs;
                            lastPunchUtc = punch.ClientTs;
                        }
                        break;
                    case "BREAK_END":
                        if (workSince.Length > 0)
                        {
                            status = "IN_WORKING";
                            sinceUtc = workSince;
                            lastPunchUtc = punch.ClientTs;
                        }
                        break;
                    case "LUNCH_START":
                        if (workSince.Length > 0)
                        {
                            status = "IN_LUNCH";
                            sinceUtc = punch.ClientTs;
                            lastPunchUtc = punch.ClientTs;
                        }
                        break;
                    case "LUNCH_END":
                        if (workSince.Length > 0)
                        {
                            status = "IN_WORKING";
                            sinceUtc = workSince;
                            lastPunchUtc = punch.ClientTs;
                        }
                        break;
                }
            }

            return (status, sinceUtc, lastPunchUtc);
        }

        private bool CanSubmitEvent(string eventType)
        {
            var current = CurrentShiftStatus switch
            {
                "CLOCKED IN" => "IN_WORKING",
                "ON BREAK" => "IN_BREAK",
                "ON LUNCH" => "IN_LUNCH",
                _ => "OUT"
            };
            return (current, eventType.Trim().ToUpperInvariant()) switch
            {
                ("OUT", "CLOCK_IN") => true,
                ("IN_WORKING", "CLOCK_OUT") => true,
                ("IN_WORKING", "BREAK_START") => true,
                ("IN_WORKING", "LUNCH_START") => true,
                ("IN_BREAK", "BREAK_END") => true,
                ("IN_LUNCH", "LUNCH_END") => true,
                _ => false
            };
        }

        private static string ToShiftTitle(string status)
        {
            return (status ?? "").Trim().ToUpperInvariant() switch
            {
                "IN_WORKING" => "CLOCKED IN",
                "IN_BREAK" => "ON BREAK",
                "IN_LUNCH" => "ON LUNCH",
                _ => "CLOCKED OUT"
            };
        }

        private static string BuildShiftDetail(WorkstationTimeclockSnapshot snapshot)
            => BuildShiftDetail(snapshot.CurrentStatus, snapshot.StatusSinceUtc, snapshot.LastPunchUtc);

        private static string BuildShiftDetail(string status, string sinceUtc, string lastPunchUtc)
        {
            var normalized = (status ?? "").Trim().ToUpperInvariant();
            return normalized == "OUT"
                ? (string.IsNullOrWhiteSpace(lastPunchUtc) ? "No punches loaded yet." : $"Last punch at {FormatLocalTime(lastPunchUtc)}.")
                : $"Since {FormatLocalTime(sinceUtc)}.";
        }

        private static string FormatLocalTime(string utc)
        {
            return DateTime.TryParse(utc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToLocalTime().ToString("g", CultureInfo.InvariantCulture)
                : utc;
        }

        private static DateTime ParseUtc(string utc)
        {
            return DateTime.TryParse(utc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : DateTime.MinValue;
        }

        private static string NormalizeDate(string value)
        {
            var clean = (value ?? "").Trim();
            if (DateTime.TryParseExact(clean, new[] { "MM-dd-yyyy", "yyyy-MM-dd", "M-d-yyyy", "M/d/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return "";
        }

        private static DateTime? TryParsePickerDate(string value)
        {
            var clean = NormalizeDate(value);
            return DateTime.TryParseExact(clean, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }

        private static string ToModuleLabel(string key) => key switch
        {
            "home" => "Home",
            "timeclock" => "Time Clock",
            "workorders" => "Work Orders",
            "drawings" => "Drawings",
            _ => key
        };

        private static string FormatFriendlyDate(string value)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
                : (string.IsNullOrWhiteSpace(value) ? "No due date" : value);
        }

        private static string ToEventLabel(string key) => key switch
        {
            "clock_in" => "Clock In",
            "clock_out" => "Clock Out",
            "break_start" => "Break Start",
            "break_end" => "Break End",
            "lunch_start" => "Lunch Start",
            "lunch_end" => "Lunch End",
            _ => key
        };

        private void RaiseCommandStates()
        {
            if (LoginCommand is RelayCommand login) login.RaiseCanExecuteChanged();
            if (LogoutCommand is RelayCommand logout) logout.RaiseCanExecuteChanged();
            if (SaveSettingsCommand is RelayCommand save) save.RaiseCanExecuteChanged();
            if (ConnectControlCommand is RelayCommand connect) connect.RaiseCanExecuteChanged();
            if (DisconnectControlCommand is RelayCommand disconnect) disconnect.RaiseCanExecuteChanged();
            if (RefreshRegistrationCommand is RelayCommand refresh) refresh.RaiseCanExecuteChanged();
            if (RefreshTimeClockCommand is RelayCommand refreshClock) refreshClock.RaiseCanExecuteChanged();
            if (ClockInCommand is RelayCommand clockIn) clockIn.RaiseCanExecuteChanged();
            if (ClockOutCommand is RelayCommand clockOut) clockOut.RaiseCanExecuteChanged();
            if (BreakStartCommand is RelayCommand breakStart) breakStart.RaiseCanExecuteChanged();
            if (BreakEndCommand is RelayCommand breakEnd) breakEnd.RaiseCanExecuteChanged();
            if (LunchStartCommand is RelayCommand lunchStart) lunchStart.RaiseCanExecuteChanged();
            if (LunchEndCommand is RelayCommand lunchEnd) lunchEnd.RaiseCanExecuteChanged();
            if (SyncPendingCommand is RelayCommand syncPending) syncPending.RaiseCanExecuteChanged();
            if (SubmitTimeOffCommand is RelayCommand submitTimeOff) submitTimeOff.RaiseCanExecuteChanged();
            if (RefreshWorkOrdersCommand is RelayCommand refreshWorkOrders) refreshWorkOrders.RaiseCanExecuteChanged();
            if (ResumeCurrentJobCommand is RelayCommand resumeCurrentJob) resumeCurrentJob.RaiseCanExecuteChanged();
            if (ResumeRecentJobCommand is RelayCommand resumeRecentJob) resumeRecentJob.RaiseCanExecuteChanged();
            if (SelectModuleKeyCommand is RelayCommand<string> selectModuleKey) selectModuleKey.RaiseCanExecuteChanged();
            if (SelectWorkOrderCommand is RelayCommand<WorkstationWorkOrderSummary> selectWorkOrder) selectWorkOrder.RaiseCanExecuteChanged();
            if (OpenDrawingCommand is RelayCommand<WorkstationDrawingReference> openDrawing) openDrawing.RaiseCanExecuteChanged();
            if (SelectOperationCommand is RelayCommand<WorkstationWorkOrderOperationSummary> selectOperation) selectOperation.RaiseCanExecuteChanged();
            if (StartOperationCommand is RelayCommand startOperation) startOperation.RaiseCanExecuteChanged();
            if (StopOperationCommand is RelayCommand stopOperation) stopOperation.RaiseCanExecuteChanged();
            if (CompleteOperationCommand is RelayCommand completeOperation) completeOperation.RaiseCanExecuteChanged();
            if (SubmitQuantityCommand is RelayCommand submitQuantity) submitQuantity.RaiseCanExecuteChanged();
            if (SubmitScrapCommand is RelayCommand submitScrap) submitScrap.RaiseCanExecuteChanged();
            if (SubmitOperationNoteCommand is RelayCommand submitNote) submitNote.RaiseCanExecuteChanged();
            if (SubmitOperationHelpCommand is RelayCommand submitHelp) submitHelp.RaiseCanExecuteChanged();
            if (RefreshInspectionTasksCommand is RelayCommand refreshInspection) refreshInspection.RaiseCanExecuteChanged();
            if (SelectInspectionTaskCommand is RelayCommand<WorkstationInspectionTask> selectInspection) selectInspection.RaiseCanExecuteChanged();
            if (SubmitInspectionResultCommand is RelayCommand submitInspection) submitInspection.RaiseCanExecuteChanged();
            if (OpenRosterEmployeeCommand is RelayCommand<WorkstationRosterEmployee> openRoster) openRoster.RaiseCanExecuteChanged();
            if (AppendPasscodeDigitCommand is RelayCommand<string> append) append.RaiseCanExecuteChanged();
            if (BackspacePasscodeCommand is RelayCommand backspace) backspace.RaiseCanExecuteChanged();
            if (ClearPasscodeCommand is RelayCommand clear) clear.RaiseCanExecuteChanged();
            if (CancelPasscodeCommand is RelayCommand cancel) cancel.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
