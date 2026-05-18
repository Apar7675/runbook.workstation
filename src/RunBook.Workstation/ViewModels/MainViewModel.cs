using RunBook.Workstation.Models;
using RunBook.Workstation.Services;
using QRCoder;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace RunBook.Workstation.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly RunBookWorkstationApiClient _api = new RunBookWorkstationApiClient();
        private readonly WorkstationConnectionStatusService _connectionStatusService = new WorkstationConnectionStatusService();
        private readonly DispatcherTimer _sessionTimer;
        private readonly DispatcherTimer _syncTimer;
        private static readonly TimeSpan AuthRefreshInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan IdleLogoutTimeout = TimeSpan.FromMinutes(5);

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
        private BitmapImage? _operationAttachmentIntakeQrImage;
        private string _operationAttachmentIntakeUrl = "";
        private string _operationAttachmentIntakeStatusText = "Select an operation to create an attachment intake QR.";
        private WorkstationDrawingReference? _selectedDrawing;
        private WorkstationInspectionTaskPackage? _inspectionPackage;
        private WorkstationInspectionTask? _selectedInspectionTask;
        private bool _isInspectionOverlayOpen;
        private RunBookWorkstationApiClient.OperationPacket? _operationPacket;
        private string _operationPacketLoadStatus = "Operation packet has not loaded yet.";
        private BitmapImage? _currentWorkOrderThumbnailImage;
        private WorkstationActiveContext? _activeContext;
        private WorkstationCurrentJobContext? _currentJob;
        private WorkstationCurrentJobContext? _recentJob;
        private WorkstationShopAwareness _shopAwareness = new WorkstationShopAwareness();
        private string _statusText = "Workstation ready.";
        private string _sessionCountdown = "Signed out";
        private string _passcode = "";
        private string _timeClockStatus = "No punches yet.";
        private string _offlineStatus = "Offline status unknown";
        private string _authCacheStatus = "No local employee auth cache loaded.";
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
        private string _headerDateText = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        private string _headerTimeText = DateTime.Now.ToString("h:mm tt");
        private string _currentShiftStatus = "CLOCKED OUT";
        private string _currentShiftDetail = "No shift loaded yet.";
        private string _pendingSyncSummary = "No pending sync items.";
        private string _timeOffType = "VACATION";
        private string _timeOffStartDate = "";
        private string _timeOffEndDate = "";
        private string _timeOffHoursText = "";
        private string _timeOffNote = "";
        private string _employeeQuickSearchText = "";
        private string _pendingWelcomeModuleKey = "";
        private bool _isPasscodeDialogOpen;
        private bool _isSupervisorDialogOpen;
        private bool _isEmployeeBrowserOpen;
        private bool _isRunBookAlertOpen;
        private string _runBookAlertTitle = "";
        private string _runBookAlertBody = "";
        private string _runBookAlertPrimaryText = "OK";
        private string _runBookAlertSecondaryText = "";
        private bool _showRunBookAlertSecondaryAction;
        private TaskCompletionSource<bool>? _runBookAlertCompletion;
        private string _lastRunBookIssueAlertKey = "";
        private string _workOrdersStatus = "Work orders will load when this module opens.";
        private WorkstationCurrentJobContext? _pendingResumeOperation;
        private bool _pendingResumeOperationCapturedFromPunchOut;
        private DateTime _lastInteractionUtc = DateTime.UtcNow;
        private const int MaxProductionQuantityValue = 1000000;
        private const int MaxWorkstationNoteLength = 1000;
        private const int MaxInspectionActualValueLength = 256;
        private static readonly TimeSpan DesktopProbeForceInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan DesktopProbePassiveInterval = TimeSpan.FromSeconds(60);
        private const string ClockedOutOperationStartMessage = "You must be clocked in before starting this operation.\nPlease punch in to begin recording production time.";
        private const string ActiveOperationPausedForClockOutMessage = "Your active operation has been paused because you are clocked out.\nProduction time will resume when you clock in and restart the operation.";
        private const string WorkstationRegistrationRequiredRawMessage = "Workstation registration with RunBook Service is required.";
        private const string WorkstationRegistrationRequiredUserMessage = "Workstation registration/trust is required before employee auth can sync. The Service health check can be green while this device is still not trusted.";

        private string _quantityReportText = "";
        private string _scrapReportText = "";
        private string _operationNoteText = "";
        private string _operationActionNoteText = "";
        private string _receivedMaterialSpec = "";
        private string _receivedMaterialShape = "";
        private string _receivedMaterialSize = "";
        private string _receivedMaterialGrade = "";
        private string _receivedMaterialQuantity = "";
        private string _receivedMaterialUnit = "Bars";
        private string _receivedMaterialCondition = "OK";
        private string _receivedMaterialNotes = "";
        private string _receivedMaterialStorageLocation = "";
        private bool _receiveMaterialMultipleHeatLots;
        private int _savedMaterialReceiptId;
        private string _inspectionActualValue = "";
        private string _inspectionResultNoteText = "";
        private bool _inspectionResultSubmissionAvailable;
        private bool _isMobileCaptureDialogOpen;
        private string _selectedMobileCaptureType = "Damage Photo";
        private string _mobileCaptureUrl = "";
        private string _mobileCaptureExpiresText = "";
        private BitmapImage? _mobileCaptureQrImage;
        private int _assignedWorkOrderCount;
        private int _backupWorkOrderCount;
        private DateTime? _lastLocalHostSuccessUtc;
        private DateTime? _lastLocalHostFailureUtc;
        private string _lastLocalHostFailureReason = "";
        private DateTime? _lastLocalHostProbeCheckedUtc;
        private DateTime? _lastDesktopSuccessUtc;
        private DateTime? _lastDesktopFailureUtc;
        private string _lastDesktopFailureReason = "";
        private DateTime? _lastDesktopProbeCheckedUtc;
        private DateTime? _lastDesktopProbeSuccessUtc;
        private DateTime? _lastDesktopProbeFailureUtc;
        private string _lastDesktopProbeFailureReason = "";
        private DateTime _lastConnectionStatusRefreshUtc = DateTime.MinValue;
        private bool _isLocalHostProbeInFlight;
        private bool _pendingLocalHostProbeRefresh;
        private int _localHostProbeGeneration;
        private bool _isControlConfirmedAvailable;
        private bool _isBackgroundSyncRunning;
        private bool _isPostLoginHydrationRunning;
        private bool _serviceWriteBlocked = true;
        private string _lastTimeClockPresentationSignature = "";
        private DateTime _lastPassiveConnectionRefreshUtc = DateTime.MinValue;
        private UnlockTimingTrace? _unlockTimingTrace;

        public MainViewModel()
        {
            _settings = WorkstationStorageService.LoadSettings();
            _settingsBaseUrl = _settings.ControlBaseUrl;
            _settingsDesktopBaseUrl = _settings.DesktopBaseUrl;
            _settingsShopId = _settings.ShopId;
            _settingsShopName = _settings.ShopName;
            _settingsWorkstationName = _settings.WorkstationName;
            _settingsPairingCode = _settings.PairingCode;
            _controlSessionStatus = ControlSessionService.GetStatusLabel();
            _isControlConfirmedAvailable = false;
            NormalizeDesktopBaseUrl();
            BindToServiceAuthorityOnBoot();
            _registration = WorkstationStorageService.LoadRegistration();
            _session = WorkstationStorageService.LoadSession();
            _timeclockSnapshot = WorkstationStorageService.LoadTimeclockState();
            _authCache = WorkstationStorageService.LoadAuthCache();

            if (_serviceWriteBlocked)
            {
                _session = null;
                _timeclockSnapshot = null;
                MarkRegistrationValidationDegraded("service_authority_blocked", "Device trust could not be validated because RunBook.Service is unavailable or blocked. Continuing with the last saved workstation pairing.");
                WorkstationStorageService.SaveSession(null);
                WorkstationStorageService.SaveTimeclockState(null);
            }

            if (_registration != null &&
                !string.IsNullOrWhiteSpace(Settings.ShopId) &&
                !string.Equals(_registration.ShopId, Settings.ShopId, StringComparison.OrdinalIgnoreCase))
            {
                _registration.PairingRequiredReason = "shop_mismatch";
                _registration = null;
                WorkstationStorageService.SaveRegistration(null);
                DebugLogService.Write("Workstation registration cleared | reason=shop_mismatch | paired=false");
            }

            if (_authCache?.Package != null &&
                !string.IsNullOrWhiteSpace(Settings.ShopId) &&
                !string.Equals(_authCache.Package.ShopId, Settings.ShopId, StringComparison.OrdinalIgnoreCase))
            {
                _authCache = null;
                WorkstationStorageService.SaveAuthCache(null);
            }

            LoginCommand = new RelayCommand(async () => await SubmitPasscodeUnlockAsync(), () => CanConfirmPasscode);
            LogoutCommand = new RelayCommand(Logout, () => CurrentSession != null && !IsBusy);
            OpenSettingsCommand = new RelayCommand(() => StatusText = "Settings stay available from the left rail.", () => !IsBusy);
            SaveSettingsCommand = new RelayCommand(SaveSettings, () => !IsBusy);
            ConnectControlCommand = new RelayCommand(async () => await ConnectControlAsync(), () => !IsBusy);
            DisconnectControlCommand = new RelayCommand(DisconnectControl, () => !IsBusy);
            RefreshRegistrationCommand = new RelayCommand(async () => await RefreshRegistrationAsync(), () => !IsBusy);
            RefreshEmployeeAuthCommand = new RelayCommand(async () => await RefreshEmployeeAuthAsync(), () => !IsBusy);
            OpenSupervisorDialogCommand = new RelayCommand(OpenSupervisorDialog, () => !IsBusy);
            CloseSupervisorDialogCommand = new RelayCommand(CloseSupervisorDialog, () => !IsBusy);
            RunBookAlertPrimaryCommand = new RelayCommand(() => CompleteRunBookAlert(true), () => IsRunBookAlertOpen);
            RunBookAlertSecondaryCommand = new RelayCommand(() => CompleteRunBookAlert(false), () => IsRunBookAlertOpen && ShowRunBookAlertSecondaryAction);
            CloseRunBookAlertCommand = RunBookAlertPrimaryCommand;
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
            StartOperationCommand = new RelayCommand(async () => await ExecuteOperationAsync("start"), () => CurrentSession != null && HasOperationExecutionAccess && SelectedOperation != null && !IsBusy);
            StopOperationCommand = new RelayCommand(async () => await ExecuteOperationAsync("stop"), () => CurrentSession != null && HasOperationExecutionAccess && SelectedOperation?.CanStop == true && !IsBusy);
            CompleteOperationCommand = new RelayCommand(async () => await ExecuteOperationAsync("complete"), () => CurrentSession != null && HasOperationExecutionAccess && SelectedOperation?.CanComplete == true && !IsBusy);
            SaveDataEntryCommand = new RelayCommand(async () => await SaveDataEntryAsync(), () => CurrentSession != null && SelectedOperation != null && !IsBusy);
            SubmitQuantityCommand = new RelayCommand(async () => await SubmitQuantityAsync(), () => CurrentSession != null && HasProductionQuantityAccess && SelectedOperation != null && !IsBusy);
            SubmitScrapCommand = new RelayCommand(async () => await SubmitScrapAsync(), () => CurrentSession != null && HasProductionScrapAccess && SelectedOperation != null && !IsBusy);
            SubmitOperationNoteCommand = new RelayCommand(async () => await SubmitOperationNoteAsync(), () => CurrentSession != null && HasProductionNoteAccess && SelectedOperation != null && !IsBusy);
            SubmitOperationHelpCommand = new RelayCommand(async () => await SubmitOperationHelpAsync(), () => CurrentSession != null && HasWorkOrdersAccess && SelectedOperation != null && !IsBusy);
            RefreshInspectionTasksCommand = new RelayCommand(async () => await RefreshInspectionTasksAsync(false), () => CurrentSession != null && HasInspectionViewAccess && SelectedWorkOrder != null && !IsBusy);
            SelectInspectionTaskCommand = new RelayCommand<WorkstationInspectionTask>(SelectInspectionTask, task => task != null && !IsBusy);
            SubmitInspectionResultCommand = new RelayCommand(async () => await SubmitInspectionResultAsync(), () => CurrentSession != null && HasInspectionEntryAccess && _inspectionResultSubmissionAvailable && SelectedInspectionTask != null && SelectedWorkOrder != null && !IsBusy);
            OpenOperationInspectionCommand = new RelayCommand(OpenOperationInspection, () => HasOperationInspection && !IsBusy);
            CloseOperationInspectionCommand = new RelayCommand(CloseOperationInspection, () => IsInspectionOverlayOpen && !IsBusy);
            OpenPacketDocumentCommand = new RelayCommand<RunBookWorkstationApiClient.OperationPacketDocument>(async document => await OpenPacketDocumentAsync(document), document => document != null && CurrentSession != null && !IsBusy);
            OpenMobileCaptureCommand = new RelayCommand(async () => await OpenMobileCaptureAsync(), () => CurrentSession != null && HasOperationExecutionAccess && SelectedWorkOrder != null && SelectedOperation != null && !IsBusy);
            CloseMobileCaptureCommand = new RelayCommand(CloseMobileCapture, () => IsMobileCaptureDialogOpen && !IsBusy);
            RefreshOperationAttachmentsCommand = new RelayCommand(async () => await RefreshOperationAttachmentsAsync(), () => CurrentSession != null && SelectedWorkOrder != null && SelectedOperation != null && !IsBusy);
            AddMaterialHeatLotCommand = new RelayCommand(AddMaterialHeatLot, () => IsReceiveMaterialOperation && !IsBusy);
            RemoveMaterialHeatLotCommand = new RelayCommand<WorkstationMaterialHeatLotEntry>(RemoveMaterialHeatLot, row => row != null && MaterialHeatLots.Count > 1 && !IsBusy);
            PrimaryOperatorActionCommand = new RelayCommand(ExecutePrimaryOperatorAction, () => !IsBusy && (IsLoggedIn || RosterEmployees.Count > 0));
            OpenRosterEmployeeCommand = new RelayCommand<WorkstationRosterEmployee>(OpenRosterEmployee, employee => employee != null && !IsBusy);
            LaunchWelcomeFlowCommand = new RelayCommand<string>(LaunchWelcomeFlow, key => !IsBusy && !string.IsNullOrWhiteSpace(key));
            OpenEmployeeBrowserCommand = new RelayCommand(OpenEmployeeBrowser, () => !IsBusy && RosterEmployees.Count > 0);
            CloseEmployeeBrowserCommand = new RelayCommand(CloseEmployeeBrowser, () => !IsBusy && IsEmployeeBrowserOpen);
            SubmitEmployeeQuickSearchCommand = new RelayCommand(async () => await SubmitEmployeeQuickSearchAsync(), () => !IsBusy);
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

            if (!_serviceWriteBlocked)
            {
                NormalizeLocalShopScope();
                LoadCachedPunches();
                LoadCachedSnapshot();
                LoadCachedAuthCache();
                RestoreSessionState();
            }
            BuildVisibleModules();
            if (VisibleModules.Count > 0)
                SelectedModule = VisibleModules[0];

            if (_serviceWriteBlocked)
            {
                OfflineStatus = Registration == null
                    ? "RunBook.Service cannot validate workstation trust because local authority is unavailable."
                    : "Device trust could not be validated because RunBook.Service is unavailable. Continuing with the last saved workstation pairing.";
                if (string.IsNullOrWhiteSpace(StatusText) || string.Equals(StatusText, "Workstation ready.", StringComparison.Ordinal))
                    StatusText = OfflineStatus;
            }
            else
            {
                _ = InitializeFromControlSessionSafeAsync();
            }
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
        public ObservableCollection<RunBookWorkstationApiClient.OperationAttachment> OperationAttachments { get; } = new();
        public ObservableCollection<RunBookWorkstationApiClient.OperationPacketDocument> PacketDrawingDocuments { get; } = new();
        public ObservableCollection<RunBookWorkstationApiClient.OperationPacketDocument> PacketBalloonedDrawingDocuments { get; } = new();
        public ObservableCollection<RunBookWorkstationApiClient.OperationPacketDocument> PacketInspectionDocuments { get; } = new();
        public ObservableCollection<RunBookWorkstationApiClient.OperationPacketDocument> PacketOperationReferences { get; } = new();
        public ObservableCollection<RunBookWorkstationApiClient.BalloonMarker> PacketBalloonMarkers { get; } = new();
        public ObservableCollection<WorkstationDataEntrySection> DataEntrySections { get; } = new();
        public ObservableCollection<string> MaterialUnitOptions { get; } = new()
        {
            "Bars", "Pieces", "Feet", "Inches", "Pounds", "Sheets", "Plates", "Tubes", "Coils", "Rolls", "Bundles", "Each", "Other"
        };
        public ObservableCollection<string> MaterialConditionOptions { get; } = new() { "OK", "Issue" };
        public ObservableCollection<WorkstationMaterialHeatLotEntry> MaterialHeatLots { get; } = new();
        public ObservableCollection<string> MobileCaptureTypes { get; } = new()
        {
            "Packing List",
            "Material Cert",
            "Damage Photo",
            "Setup Photo",
            "Inspection Evidence",
            "Other Evidence"
        };

        public ICommand LoginCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ConnectControlCommand { get; }
        public ICommand DisconnectControlCommand { get; }
        public ICommand RefreshRegistrationCommand { get; }
        public ICommand RefreshEmployeeAuthCommand { get; }
        public ICommand OpenSupervisorDialogCommand { get; }
        public ICommand CloseSupervisorDialogCommand { get; }
        public ICommand RunBookAlertPrimaryCommand { get; }
        public ICommand RunBookAlertSecondaryCommand { get; }
        public ICommand CloseRunBookAlertCommand { get; }
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
        public ICommand SaveDataEntryCommand { get; }
        public ICommand SubmitQuantityCommand { get; }
        public ICommand SubmitScrapCommand { get; }
        public ICommand SubmitOperationNoteCommand { get; }
        public ICommand SubmitOperationHelpCommand { get; }
        public ICommand RefreshInspectionTasksCommand { get; }
        public ICommand SelectInspectionTaskCommand { get; }
        public ICommand SubmitInspectionResultCommand { get; }
        public ICommand OpenOperationInspectionCommand { get; }
        public ICommand CloseOperationInspectionCommand { get; }
        public ICommand OpenPacketDocumentCommand { get; }
        public ICommand OpenMobileCaptureCommand { get; }
        public ICommand CloseMobileCaptureCommand { get; }
        public ICommand RefreshOperationAttachmentsCommand { get; }
        public ICommand AddMaterialHeatLotCommand { get; }
        public ICommand RemoveMaterialHeatLotCommand { get; }
        public ICommand PrimaryOperatorActionCommand { get; }
        public ICommand OpenRosterEmployeeCommand { get; }
        public ICommand LaunchWelcomeFlowCommand { get; }
        public ICommand OpenEmployeeBrowserCommand { get; }
        public ICommand CloseEmployeeBrowserCommand { get; }
        public ICommand SubmitEmployeeQuickSearchCommand { get; }
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
                OnPropertyChanged(nameof(StationAreaLabel));
                OnPropertyChanged(nameof(StationSubtitle));
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
                OnPropertyChanged(nameof(LoginHeadline));
                OnPropertyChanged(nameof(LoginInstructionLine));
                OnPropertyChanged(nameof(StationAreaLabel));
                OnPropertyChanged(nameof(StationSubtitle));
                OnPropertyChanged(nameof(CurrentOperatorLine));
                OnPropertyChanged(nameof(CurrentOperatorStatusLine));
                OnPropertyChanged(nameof(PrimaryOperatorActionText));
                OnPropertyChanged(nameof(SessionModulesLine));
                OnPropertyChanged(nameof(ShowRecentEmployeeEmptyState));
                OnPropertyChanged(nameof(RecentEmployeeEmptyText));
                OnPropertyChanged(nameof(HasMyActiveOperationShortcut));
                OnPropertyChanged(nameof(ShowIdleLogoutBadge));
                OnPropertyChanged(nameof(IdleLogoutBadgeText));
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
                OnPropertyChanged(nameof(ShowGlobalStatusFooter));
                OnPropertyChanged(nameof(IsHomeSelected));
                OnPropertyChanged(nameof(IsWorkOrdersSelected));
                OnPropertyChanged(nameof(IsDrawingsSelected));
                OnPropertyChanged(nameof(ServiceStatusHeadline));
                OnPropertyChanged(nameof(ServiceStatusDetail));
                OnPropertyChanged(nameof(ServiceStatusAccentBrush));
                OnPropertyChanged(nameof(ServiceStatusBackgroundBrush));
                OnPropertyChanged(nameof(ServiceStatusNote));
                OnPropertyChanged(nameof(StationStatusDetailText));
                OnPropertyChanged(nameof(RuntimeAuthorityStatusText));
                OnPropertyChanged(nameof(RuntimeAuthorityStatusBrush));
                OnPropertyChanged(nameof(WorkstationOnlineStatusBrush));
                OnPropertyChanged(nameof(LastSyncDisplayText));
                RefreshRosterEmployeeStatuses();
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
                OnPropertyChanged(nameof(WorkstationOnlineStatusBrush));
                OnPropertyChanged(nameof(WorkstationOnlineStatusText));
                OnPropertyChanged(nameof(LastSyncDisplayText));
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
                OnPropertyChanged(nameof(ShowGlobalStatusFooter));
                OnPropertyChanged(nameof(IsHomeSelected));
                OnPropertyChanged(nameof(IsWorkOrdersSelected));
                OnPropertyChanged(nameof(IsDrawingsSelected));
                OnPropertyChanged(nameof(ShowIdleLogoutBadge));
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
                    CurrentWorkOrderThumbnailImage = null;
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
                OnPropertyChanged(nameof(SelectedOperationDisplayStatus));
                OnPropertyChanged(nameof(OperationStartActionText));
                OnPropertyChanged(nameof(ActiveWorkContextLine));
                RebuildDataEntrySections();
                UpdateOperationAttachmentIntakeQr();
                if (_selectedOperation == null)
                {
                    ClearOperationPacket();
                }
                RaiseCommandStates();
                if (CurrentSession != null && SelectedWorkOrder != null && _selectedOperation != null && !IsBusy)
                    _ = RefreshOperationPacketAsync(false);
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
                OnPropertyChanged(nameof(InspectionOverlayTypeLabel));
                OnPropertyChanged(nameof(UseInProcessInspectionLayout));
                OnPropertyChanged(nameof(UseSideBySideInspectionLayout));
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
                _inspectionActualValue = value?.ActualValue ?? "";
                _inspectionResultNoteText = value?.ResultNotes ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedInspectionTaskTitle));
                OnPropertyChanged(nameof(SelectedInspectionTaskSummary));
                OnPropertyChanged(nameof(SelectedInspectionBalloonLabel));
                OnPropertyChanged(nameof(SelectedInspectionFeatureLabel));
                OnPropertyChanged(nameof(InspectionActualValue));
                OnPropertyChanged(nameof(InspectionResultNoteText));
                RaiseCommandStates();
            }
        }

        public bool IsInspectionOverlayOpen
        {
            get => _isInspectionOverlayOpen;
            private set
            {
                if (_isInspectionOverlayOpen == value)
                    return;

                _isInspectionOverlayOpen = value;
                OnPropertyChanged();
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
                OnPropertyChanged(nameof(PasscodeDialogActionTitle));
                OnPropertyChanged(nameof(PasscodeDialogEmployeeStatusText));
                OnPropertyChanged(nameof(ShowPasscodeDialogEmployeeStatus));
                OnPropertyChanged(nameof(PasscodeDialogEmployeeStatusForeground));
                OnPropertyChanged(nameof(PasscodeDialogEmployeeStatusBackground));
                OnPropertyChanged(nameof(PasscodeDialogEmployeeStatusBorder));
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
                OnPropertyChanged(nameof(PasscodeDialogMessageText));
                OnPropertyChanged(nameof(ShowPasscodeDialogMessage));
                RaiseCommandStates();
            }
        }

        public bool IsSupervisorDialogOpen
        {
            get => _isSupervisorDialogOpen;
            private set
            {
                if (_isSupervisorDialogOpen == value)
                    return;

                _isSupervisorDialogOpen = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }

        public bool IsRunBookAlertOpen
        {
            get => _isRunBookAlertOpen;
            private set
            {
                if (_isRunBookAlertOpen == value)
                    return;

                _isRunBookAlertOpen = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }

        public string RunBookAlertTitle
        {
            get => _runBookAlertTitle;
            private set
            {
                if (string.Equals(_runBookAlertTitle, value, StringComparison.Ordinal))
                    return;

                _runBookAlertTitle = value ?? "";
                OnPropertyChanged();
            }
        }

        public string RunBookAlertBody
        {
            get => _runBookAlertBody;
            private set
            {
                if (string.Equals(_runBookAlertBody, value, StringComparison.Ordinal))
                    return;

                _runBookAlertBody = value ?? "";
                OnPropertyChanged();
            }
        }
        public string RunBookAlertPrimaryText
        {
            get => _runBookAlertPrimaryText;
            private set
            {
                if (string.Equals(_runBookAlertPrimaryText, value, StringComparison.Ordinal))
                    return;

                _runBookAlertPrimaryText = value ?? "";
                OnPropertyChanged();
            }
        }

        public string RunBookAlertSecondaryText
        {
            get => _runBookAlertSecondaryText;
            private set
            {
                if (string.Equals(_runBookAlertSecondaryText, value, StringComparison.Ordinal))
                    return;

                _runBookAlertSecondaryText = value ?? "";
                OnPropertyChanged();
            }
        }

        public bool ShowRunBookAlertSecondaryAction
        {
            get => _showRunBookAlertSecondaryAction;
            private set
            {
                if (_showRunBookAlertSecondaryAction == value)
                    return;

                _showRunBookAlertSecondaryAction = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                _statusText = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusTextBrush));
                OnPropertyChanged(nameof(StatusTextBorderBrush));
                OnPropertyChanged(nameof(StatusTextBackgroundBrush));
                OnPropertyChanged(nameof(PasscodeDialogMessageText));
                OnPropertyChanged(nameof(ShowPasscodeDialogMessage));
            }
        }
        public string StatusTextBrush => IsErrorStatusText ? "#F0525E" : IsWarningStatusText ? "#E8BC52" : IsSuccessStatusText ? "#3CC875" : "#B0BDD0";
        public string StatusTextBorderBrush => IsErrorStatusText ? "#66F0525E" : IsWarningStatusText ? "#5CE8BC52" : IsSuccessStatusText ? "#553CC875" : "#3B526F";
        public string StatusTextBackgroundBrush => IsErrorStatusText ? "#18F0525E" : IsWarningStatusText ? "#14E8BC52" : IsSuccessStatusText ? "#123CC875" : "#15162636";
        private bool IsErrorStatusText => ContainsStatusText("error") || ContainsStatusText("failed") || ContainsStatusText("unable") || ContainsStatusText("unavailable") || ContainsStatusText("invalid") || ContainsStatusText("expired");
        private bool IsWarningStatusText => ContainsStatusText("waiting") || ContainsStatusText("not configured") || ContainsStatusText("required");
        private bool IsSuccessStatusText => ContainsStatusText("saved") || ContainsStatusText("complete") || ContainsStatusText("ready") || ContainsStatusText("operational") || ContainsStatusText("connected");
        private bool ContainsStatusText(string value)
            => (_statusText ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        public string SessionCountdown { get => _sessionCountdown; private set { _sessionCountdown = value ?? ""; OnPropertyChanged(); } }
        public string TimeClockStatus { get => _timeClockStatus; private set { _timeClockStatus = value ?? ""; OnPropertyChanged(); } }
        public string OfflineStatus
        {
            get => _offlineStatus;
            private set
            {
                _offlineStatus = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(StationStatusDetailText));
            }
        }
        public string AuthCacheStatus { get => _authCacheStatus; private set { _authCacheStatus = value ?? ""; OnPropertyChanged(); } }
        public string SettingsBaseUrl { get => _settingsBaseUrl; set { _settingsBaseUrl = value ?? ""; OnPropertyChanged(); } }
        public string SettingsDesktopBaseUrl { get => _settingsDesktopBaseUrl; set { _settingsDesktopBaseUrl = value ?? ""; OnPropertyChanged(); } }
        public string SettingsShopId { get => _settingsShopId; set { _settingsShopId = value ?? ""; OnPropertyChanged(); } }
        public string SettingsShopName { get => _settingsShopName; set { _settingsShopName = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(StationAreaLabel)); } }
        public string SettingsWorkstationName { get => _settingsWorkstationName; set { _settingsWorkstationName = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(StationSubtitle)); OnPropertyChanged(nameof(WorkstationIdentityLine)); } }
        public string SettingsPairingCode { get => _settingsPairingCode; set { _settingsPairingCode = value ?? ""; OnPropertyChanged(); } }
        public string SettingsControlEmail { get => _settingsControlEmail; set { _settingsControlEmail = value ?? ""; OnPropertyChanged(); } }
        public string SettingsControlPassword { get => _settingsControlPassword; set { _settingsControlPassword = value ?? ""; OnPropertyChanged(); } }
        public string ControlSessionStatus { get => _controlSessionStatus; private set { _controlSessionStatus = value ?? ""; OnPropertyChanged(); } }
        public string CurrentShiftStatus { get => _currentShiftStatus; private set { _currentShiftStatus = value ?? ""; OnPropertyChanged(); } }
        public string CurrentShiftDetail { get => _currentShiftDetail; private set { _currentShiftDetail = value ?? ""; OnPropertyChanged(); } }
        public string PendingSyncSummary { get => _pendingSyncSummary; private set { _pendingSyncSummary = value ?? ""; OnPropertyChanged(); } }
        public string WorkOrdersStatus { get => _workOrdersStatus; private set { _workOrdersStatus = value ?? ""; OnPropertyChanged(); } }
        public string HeaderDateText => _headerDateText;
        public string HeaderTimeText => _headerTimeText;
        public string StationAreaLabel => string.IsNullOrWhiteSpace(Settings.ShopName) ? "WORKSTATION AREA" : Settings.ShopName.ToUpperInvariant();
        public string StationSubtitle => string.IsNullOrWhiteSpace(Settings.WorkstationName) ? "Workstation" : Settings.WorkstationName;
        public string RefreshConnectionsButtonText => "Refresh Employee Avatars";
        public bool HasSupervisorSession => ControlSessionService.HasSession();
        public bool ShowSupervisorSignInFields => !HasSupervisorSession;
        public bool ShowSupervisorEnrollmentActions => HasSupervisorSession;
        public string SupervisorDialogTitle => HasSupervisorSession ? "Supervisor Access Ready" : "Supervisor Sign In";
        public string SupervisorDialogSubtitle => HasSupervisorSession
            ? "Supervisor tools are unlocked for registration, reconnects, and codes."
            : "RunBook supervisor email and password are required before registration codes can be used.";
        public string IdleLogoutButtonText => "Log Out / Switch User";
        public string IdleLogoutBadgeText => CurrentSession == null ? "" : FormatIdleLogoutRemaining(GetIdleSecondsRemaining());
        public bool ShowIdleLogoutBadge => CurrentSession != null && !IsWorkOrdersSelected;
        public string TimeClockStateKey => GetShiftStateKey(CurrentShiftStatus);
        public string TimeClockHeroTitle => TimeClockStateKey switch
        {
            "working" => "You Are Clocked In",
            "break" => "You Are On Break",
            "lunch" => "You Are At Lunch",
            _ => "You Are Clocked Out"
        };
        public string TimeClockHeroStatusLabel => CurrentShiftStatus;
        public string TimeClockHeroHelperText => TimeClockStateKey switch
        {
            "working" => "You are currently working. Keep up the great work!",
            "break" => "Your break is active. End break when you return.",
            "lunch" => "Your lunch is active. End lunch when you return.",
            _ => "Clock in when you are ready to start recording time."
        };
        public string TimeClockHeroPrimaryLine => BuildHeroPrimaryLine();
        public string TimeClockHeroSecondaryLine => BuildHeroSecondaryLine();
        public string TimeClockHeroSinceLine => BuildHeroSinceLine();
        public string TimeClockHeroClockInTime => BuildHeroClockInTime();
        public string TimeClockHeroClockInDate => BuildHeroClockInDate();
        public string TimeClockHeroElapsedValue => BuildHeroElapsedValue();
        public string TimeClockHeroElapsedCaption => TimeClockStateKey == "out" ? "not currently running" : $"as of {DateTime.Now.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
        public string TimeClockHeroShiftLine => BuildHeroShiftLine();
        public string TimeClockWeeklyDateRange => BuildWeeklyDateRange();
        public string TimeClockPendingApprovalCount => RecentTimeOffRequests.Count.ToString(CultureInfo.InvariantCulture);
        public string TimeClockPendingApprovalText => HasRecentTimeOffRequests ? "Pending requests loaded" : "No pending requests";
        public string TimeClockFooterStatusLine => string.IsNullOrWhiteSpace(TimeClockStatus) ? $"Loaded service timeclock state for {SessionEmployeeName}." : TimeClockStatus;
        public string TimeClockSyncStatusLabel => HasPendingSyncItems ? "Pending Sync" : "Synced";
        public string TimeClockSyncDetailText => BuildSyncSummaryValue();
        public string WorkstationVersionLabel => $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
        public string TimeClockTimelineClockInText => TimeClockStateKey == "out" ? "Ready" : BuildStatusTimeValue();
        public string TimeClockTimelineWorkingText => TimeClockStateKey switch
        {
            "working" => "In Progress",
            "break" => "Paused for break",
            "lunch" => "Paused for lunch",
            _ => "Upcoming"
        };
        public string TimeClockTimelineLunchText => TimeClockStateKey == "lunch" ? "In Progress" : "Upcoming";
        public string TimeClockTimelineClockOutText => TimeClockStateKey == "out" ? "Completed" : "Upcoming";
        public string TimeClockHeroAccentBrush => TimeClockStateKey switch
        {
            "working" => "#58E58B",
            "break" => "#F5A623",
            "lunch" => "#49A7FF",
            _ => "#FF5B6E"
        };
        public string TimeClockHeroBorderBrush => TimeClockStateKey switch
        {
            "working" => "#1F6A4C",
            "break" => "#8A5A10",
            "lunch" => "#2B6FB2",
            _ => "#8E3140"
        };
        public string TimeClockHeroBackgroundBrush => TimeClockStateKey switch
        {
            "working" => "#0E2F25",
            "break" => "#31230F",
            "lunch" => "#112946",
            _ => "#34151C"
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
                OnPropertyChanged(nameof(CurrentOperatorStatusLine));
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
        public string QuantityReportText { get => _quantityReportText; set { _quantityReportText = value ?? ""; OnPropertyChanged(); RaiseDataEntryPropertiesChanged(); RaiseCommandStates(); } }
        public string ScrapReportText { get => _scrapReportText; set { _scrapReportText = value ?? ""; OnPropertyChanged(); RaiseDataEntryPropertiesChanged(); RaiseCommandStates(); } }
        public string OperationNoteText { get => _operationNoteText; set { _operationNoteText = value ?? ""; OnPropertyChanged(); RaiseDataEntryPropertiesChanged(); RaiseCommandStates(); } }
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
        public bool ShowGlobalStatusFooter => !IsTimeClockSelected;
        public bool IsWorkOrdersSelected => string.Equals(SelectedModule?.Key, "workorders", StringComparison.OrdinalIgnoreCase);
        public bool IsDrawingsSelected => string.Equals(SelectedModule?.Key, "drawings", StringComparison.OrdinalIgnoreCase);
        public bool HasCurrentJob => CurrentJob != null && CurrentJob.WorkOrderId > 0;
        public bool HasRecentJob => !HasCurrentJob && RecentJob != null && RecentJob.WorkOrderId > 0;
        public bool HasShopAwareness => ShopAwareness.Summary.VisibleJobs > 0 || ShopOperators.Count > 0;
        public bool CanConfirmPasscode => !IsBusy && IsPasscodeDialogOpen && SelectedRosterEmployee != null && _passcode.Length >= 4 && _passcode.Length <= 6;
        public string LoginHeadline => IsLoggedIn ? "Go to Work" : "Select Operator";
        public string LoginInstructionLine => IsLoggedIn
            ? "Your workstation session is ready. Continue into work when you are ready."
            : "Choose your operator card below, then enter your 4 to 6 digit passcode to sign in.";
        public string CurrentOperatorLine => IsLoggedIn ? $"Operator: {SessionEmployeeName}" : "Operator: None selected";
        public string CurrentOperatorStatusLine => IsLoggedIn
            ? $"Status: {(HasCurrentJob ? "Working" : "Ready")}"
            : "Status: Waiting for operator sign-in";
        public string PrimaryOperatorActionText => IsLoggedIn ? "Go to Work" : "Select Operator";
        public string WorkstationIdentityLine => $"{Settings.WorkstationName}  |  {Settings.WorkstationId}";
        public IReadOnlyList<WorkstationRosterEmployee> RecentRosterEmployees => RosterEmployees.Take(8).ToList();
        public bool HasMoreRosterEmployees => RosterEmployees.Count > RecentRosterEmployees.Count;
        public bool HasRosterEmployees => RosterEmployees.Count > 0;
        public bool ShowRecentEmployeeEmptyState => RosterEmployees.Count == 0;
        public string RecentEmployeeEmptyText => RosterEmployees.Count == 0
            ? "Choose an action above, then select your employee profile."
            : "Select a recent employee to continue into passcode sign-in.";
        public string RecentEmployeeEmptyDetailText => RosterEmployees.Count == 0
            ? "Recent employees will appear here after sign-in."
            : "";
        public string EmployeeQuickSearchText
        {
            get => _employeeQuickSearchText;
            set
            {
                var normalized = value ?? "";
                if (string.Equals(_employeeQuickSearchText, normalized, StringComparison.Ordinal))
                    return;
                _employeeQuickSearchText = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EmployeeQuickSearchHint));
                OnPropertyChanged(nameof(EmployeeBrowserEmployees));
                RaiseCommandStates();
            }
        }
        public string EmployeeQuickSearchHint => string.IsNullOrWhiteSpace(EmployeeQuickSearchText)
            ? "Search employee by name or badge..."
            : EmployeeQuickSearchText.Trim();
        public string QuickSearchHelperText => RosterEmployees.Count == 0
            ? "Employee search becomes available after the workstation roster is loaded."
            : "Search employee by name or badge, or open the full employee list.";
        public bool IsEmployeeBrowserOpen
        {
            get => _isEmployeeBrowserOpen;
            private set
            {
                if (_isEmployeeBrowserOpen == value)
                    return;
                _isEmployeeBrowserOpen = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }
        public IReadOnlyList<WorkstationRosterEmployee> EmployeeBrowserEmployees
        {
            get
            {
                var query = (EmployeeQuickSearchText ?? "").Trim();
                if (query.Length == 0)
                    return RosterEmployees.ToList();

                return RosterEmployees
                    .Where(employee =>
                        employee.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        employee.EmployeeCode.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }
        }
        public string ServiceStatusHeadline => _serviceWriteBlocked ? "SERVICE ATTENTION" : "SERVICE READY";
        public string ServiceStatusDetail => _serviceWriteBlocked
            ? "Workstation writes are paused until RunBook.Service is healthy."
            : "Connected to RunBook Service";
        public string ServiceStatusAccentBrush => _serviceWriteBlocked ? "#FF5A6A" : "#2BD576";
        public string ServiceStatusBackgroundBrush => _serviceWriteBlocked ? "#22FF5A6A" : "#182BD576";
        public string WorkstationOnlineStatusBrush => Registration?.IsActive == true ? "#FF2BD576" : "#FFAAB6C8";
        public string WorkstationOnlineStatusText => Registration?.IsActive == true ? "Online" : "Not paired";
        public string RuntimeAuthorityStatusText => _serviceWriteBlocked ? "Blocked" : "Healthy";
        public string RuntimeAuthorityStatusBrush => _serviceWriteBlocked ? "#FFFF5A6A" : "#FF2BD576";
        public string ServiceStatusNote => _serviceWriteBlocked
            ? "RunBook.Service local authority is unavailable right now."
            : "RunBook.Service local authority connected.";
        public string StationStatusDetailText => string.Equals(OfflineStatus, ServiceStatusNote, StringComparison.OrdinalIgnoreCase)
            ? ""
            : OfflineStatus;
        public string LastSyncDisplayText
        {
            get
            {
                var raw = FirstNonBlank(_timeclockSnapshot?.LastSyncUtc, Registration?.LastSyncUtc, _authCache?.LastRefreshSuccessUtc);
                return string.IsNullOrWhiteSpace(raw) ? "Not synced" : FormatDisplayDateTime(raw);
            }
        }
        public bool HasMyActiveOperationShortcut => CurrentJob != null && CurrentJob.WorkOrderId > 0 && CurrentJob.OperationId > 0;
        public string ConnectivityLine
        {
            get
            {
                var shop = string.IsNullOrWhiteSpace(Settings.ShopName) ? "Unassigned shop" : Settings.ShopName;
                var registration = Registration == null ? "Not registered" : $"Registered: {Registration.Status}";
                var localHost = BuildLocalHostEndpointLabel();
                return $"{shop}  |  {registration}  |  {localHost}";
            }
        }
        public string RosterDiagnosticLine
        {
            get
            {
                if (_authCache?.Package == null)
                    return "No employee auth package is loaded from the local workstation authority yet.";

                var total = _authCache.Package.Employees?.Count ?? 0;
                var active = _authCache.Package.Employees?.Count(employee => employee.IsActive) ?? 0;
                var workstationReady = _authCache.Package.Employees?.Count(employee => employee.IsActive && employee.WorkstationAccessEnabled) ?? 0;
                var visible = RosterEmployees.Count;
                return $"Local package: {total} employees | active {active} | workstation-ready {workstationReady} | showing {visible}.";
            }
        }
        public string RosterAvatarStatusLine
        {
            get
            {
                if (RosterEmployees.Count == 0)
                    return "";

                var withPhotos = RosterEmployees.Count(employee => employee.HasAvatarDisplayUrl);
                return withPhotos == 0
                    ? "Desktop did not send face-photo avatars for this roster, so Workstation is showing generated avatar circles."
                    : $"Showing face-photo avatars for {withPhotos} employee{(withPhotos == 1 ? "" : "s")}.";
            }
        }

        public string SessionEmployeeName => CurrentSession?.Employee?.DisplayName ?? "No active user";
        public string SessionRole => CurrentSession?.Employee?.Role ?? "Signed out";
        public string SessionModulesLine => CurrentSession == null ? "No modules available" : (CurrentSession.Modules.Count == 0 ? "No access assigned" : string.Join("  |  ", CurrentSession.Modules.Select(ToModuleLabel)));
        public string CurrentJobTitle => CurrentJob == null ? "No active job" : $"{CurrentJob.WorkOrderNumber} • Op {CurrentJob.OperationNumber:000}";
        public string CurrentJobSummary => CurrentJob == null ? "Service has not assigned an active job to this employee yet." : $"{CurrentJob.PartNumber} • {CurrentJob.OperationTitle}";
        public string CurrentJobHint => CurrentJob == null ? "Select an assigned job to begin work." : $"{CurrentJob.AssignmentLabel} • {CurrentJob.OperationStatus}";
        public string RecentJobTitle => RecentJob == null ? "No recent job" : $"{RecentJob.WorkOrderNumber} • Op {RecentJob.OperationNumber:000}";
        public string RecentJobSummary => RecentJob == null ? "No recent workstation job is waiting to resume." : $"{RecentJob.PartNumber} • {RecentJob.OperationTitle}";
        public string ShopSummaryLine => !HasShopAwareness
            ? "Service will surface shop awareness after workstation jobs load."
            : $"Active jobs {ShopAwareness.Summary.ActiveJobs} | Waiting jobs {ShopAwareness.Summary.WaitingJobs} | Completed {ShopAwareness.Summary.CompletedJobs}";
        public string ShopSummaryCountsLine => !HasShopAwareness
            ? "No shop-level visibility is loaded yet."
            : $"Active operators {ShopAwareness.Summary.ActiveOperators} | Idle {ShopAwareness.Summary.IdleOperators} | Visible jobs {ShopAwareness.Summary.VisibleJobs}";
        public string ShopOperatorAwarenessLine => ShopOperators.Count == 0
            ? "No operators are currently active in this workstation view."
            : $"Showing {ShopOperators.Count} active operator{(ShopOperators.Count == 1 ? "" : "s")} from Service.";
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
            : $"{WorkOrderDetail.PartNumber} - Rev {WorkOrderDetail.Revision} - Qty {WorkOrderDetail.Quantity}";
        public string SelectedWorkOrderContext => WorkOrderDetail == null
            ? "Service now serves workstation work-order reads."
            : $"{WorkOrderDetail.ReleaseState} snapshot - Due {FormatFriendlyDate(WorkOrderDetail.DueDate)} - {WorkOrderDetail.SnapshotLoadSource}";
        public string SelectedWorkOrderOperationsSummary => WorkOrderDetail == null
            ? "No operation summary loaded yet."
            : WorkOrderDetail.TotalOperations <= 0
                ? "No released operations were found for this work order."
                : $"In Progress: {WorkOrderDetail.OperationSummary.InProgress} - Completed: {WorkOrderDetail.OperationSummary.Completed} - Remaining: {WorkOrderDetail.OperationSummary.NotStarted}";
        public string SelectedWorkOrderProgressSummary => WorkOrderDetail == null
            ? "Service computes production visibility from company manufacturing state."
            : WorkOrderDetail.TotalOperations <= 0
                ? "Progress will appear once released operations exist."
                : $"{WorkOrderDetail.ProgressPercent}% complete - {WorkOrderDetail.CompletedOperations}/{WorkOrderDetail.TotalOperations} operations finished";
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
                ? $"{_activeContext.WorkOrderNumber} - {_activeContext.PartNumber}"
                : $"{_activeContext.WorkOrderNumber} - OP{_activeContext.OperationNumber:000} {_activeContext.OperationTitle}";
        public BitmapImage? CurrentWorkOrderThumbnailImage
        {
            get => _currentWorkOrderThumbnailImage;
            private set
            {
                if (ReferenceEquals(_currentWorkOrderThumbnailImage, value))
                    return;

                _currentWorkOrderThumbnailImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentWorkOrderThumbnailVisibility));
                OnPropertyChanged(nameof(CurrentWorkOrderThumbnailPlaceholderVisibility));
            }
        }
        public Visibility CurrentWorkOrderThumbnailVisibility => CurrentWorkOrderThumbnailImage == null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CurrentWorkOrderThumbnailPlaceholderVisibility => CurrentWorkOrderThumbnailImage == null ? Visibility.Visible : Visibility.Collapsed;
        public string SelectedOperationTitle => SelectedOperation == null
            ? "Select an operation"
            : $"OP{SelectedOperation.OperationNumber:000} - {SelectedOperation.Title}";
        public string SelectedOperationDisplayStatus => SelectedOperation?.DisplayStatus ?? "";
        public string OperationStartActionText => SelectedOperation?.IsPaused == true ? "Resume Operation" : "Start Operation";
        public string SelectedOperationContext => SelectedOperation == null
            ? "Pick a released operation to execute from Workstation."
            : $"{SelectedOperation.DisplayStatus} - {SelectedOperation.Department} / {SelectedOperation.WorkCenter}";
        public string DataEntryOperationTypeLabel => SelectedOperation == null
            ? "No operation selected"
            : FirstNonBlank(_operationPacket?.OperationTypeDisplay, _operationPacket?.OperationType, SelectedOperation.Title, "Custom");
        public string DataEntryHeaderSummary => SelectedOperation == null
            ? "Select an operation to see required entry steps."
            : BuildDataEntryHeaderSummary();
        public string DataEntryEntryTitle => GetOperationFamily() switch
        {
            "order-material" => "Material Order Entry",
            "receive-material" => "Material Receipt Entry",
            "saw-cut" => "Saw Cut Entry",
            "setup" => "Setup Entry",
            "cnc" => "Production Entry",
            "swiss" => "Production Entry",
            "deburr" => "Deburr Entry",
            "shipping" => "Shipping Entry",
            "complete-close" => "Closeout Entry",
            _ => "Operation Entry"
        };
        public string DataEntryEntryInstruction => GetOperationFamily() switch
        {
            "order-material" => "Save ordered quantity through the existing Service operation data path.",
            "receive-material" => "Enter received quantity for this material operation.",
            "saw-cut" => "Enter cut quantity and scrap/remnant quantity when applicable.",
            "setup" => "Use notes for setup/handoff details; save quantity only if your route requires it.",
            "cnc" => "Enter good quantity and scrap quantity for this production run.",
            "swiss" => "Enter good quantity and scrap quantity for this production run.",
            "deburr" => "Enter completed quantity and rework/scrap when applicable.",
            "shipping" => "Enter packed or shipped quantity for this operation.",
            "complete-close" => "Enter final completed quantity and total scrap when applicable.",
            _ => "Save supported operation data through the existing Service write path."
        };
        public string DataEntryQuantityLabel => GetOperationFamily() switch
        {
            "order-material" => "Ordered Qty",
            "receive-material" => "Received Qty",
            "saw-cut" => "Cut Qty",
            "setup" => "Setup Qty",
            "deburr" => "Qty Completed",
            "inspection" => "Accepted Qty",
            "shipping" => "Qty Shipped",
            "complete-close" => "Final Qty Complete",
            _ => "Good Qty"
        };
        public string DataEntrySaveProgressText => GetOperationFamily() switch
        {
            "receive-material" => "Save Receiving Entry",
            "order-material" => "Save Ordered Qty",
            "saw-cut" => "Save Cut Qty",
            "setup" => "Save Setup Note",
            "inspection" => "Save Review Note",
            "shipping" => "Save Shipped Qty",
            _ => "Save Progress"
        };
        public string DataEntrySubmitText => SelectedOperation?.IsCompleted == true ? "Completed" : IsReceiveMaterialOperation ? "Submit Receive Material" : "Complete Operation";
        public bool DataEntryCanSubmitOperation => string.IsNullOrWhiteSpace(DataEntryValidationMessage);
        public bool IsReceiveMaterialOperation => SelectedOperation != null && GetOperationFamily() == "receive-material";
        public Visibility ReceiveMaterialEntryVisibility => IsReceiveMaterialOperation ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StandardDataEntryVisibility => IsReceiveMaterialOperation ? Visibility.Collapsed : Visibility.Visible;
        public bool ShowDataEntryQuantityField => SelectedOperation != null && !IsReceiveMaterialOperation && GetOperationFamily() is not "inspection" and not "setup";
        public bool ShowDataEntryScrapField => SelectedOperation != null && GetOperationFamily() is "saw-cut" or "cnc" or "swiss" or "deburr" or "complete-close" or "custom";
        public bool ShowDataEntryInspectionCard => SelectedOperation != null && (HasOperationInspection || GetOperationFamily() == "inspection");
        public bool ShowDataEntryEvidenceCard => SelectedOperation != null;
        public bool ShowDataEntryMissingReason => !string.IsNullOrWhiteSpace(DataEntryValidationMessage);
        public string DataEntryValidationMessage => BuildDataEntryValidationMessage();
        public RunBookWorkstationApiClient.MaterialRequirementDto? PrimaryMaterialRequirement => _operationPacket?.MaterialRequirements?.FirstOrDefault();
        public string ExpectedMaterialSummary
        {
            get
            {
                var req = PrimaryMaterialRequirement;
                if (req == null) return "No expected material has been released for this operation.";
                var material = FirstNonBlank(req.MaterialSummaryDisplay, string.Join(" ", new[] { req.ExpectedSize, req.ExpectedShape, req.ExpectedGrade, req.ExpectedSpec }.Where(x => !string.IsNullOrWhiteSpace(x))));
                return string.IsNullOrWhiteSpace(material) ? "Expected material details not recorded." : $"Expected: {material}";
            }
        }
        public string ExpectedMaterialQuantityText => PrimaryMaterialRequirement == null
            ? "--"
            : PrimaryMaterialRequirement.ExpectedQuantity > 0
                ? $"{PrimaryMaterialRequirement.ExpectedQuantity:0.####} {PrimaryMaterialRequirement.ExpectedUnit}".Trim()
                : "--";
        public string ReceivedMaterialSpec { get => _receivedMaterialSpec; set { _receivedMaterialSpec = value ?? ""; OnPropertyChanged(); RaiseMaterialReceivingChanged(); } }
        public string ReceivedMaterialShape { get => _receivedMaterialShape; set { _receivedMaterialShape = value ?? ""; OnPropertyChanged(); RaiseMaterialReceivingChanged(); } }
        public string ReceivedMaterialSize { get => _receivedMaterialSize; set { _receivedMaterialSize = value ?? ""; OnPropertyChanged(); RaiseMaterialReceivingChanged(); } }
        public string ReceivedMaterialGrade { get => _receivedMaterialGrade; set { _receivedMaterialGrade = value ?? ""; OnPropertyChanged(); RaiseMaterialReceivingChanged(); } }
        public string ReceivedMaterialQuantity { get => _receivedMaterialQuantity; set { _receivedMaterialQuantity = value ?? ""; OnPropertyChanged(); RaiseMaterialReceivingChanged(); } }
        public string ReceivedMaterialUnit { get => _receivedMaterialUnit; set { _receivedMaterialUnit = value ?? ""; OnPropertyChanged(); RaiseMaterialReceivingChanged(); } }
        public string ReceivedMaterialCondition { get => _receivedMaterialCondition; set { _receivedMaterialCondition = string.IsNullOrWhiteSpace(value) ? "OK" : value; OnPropertyChanged(); } }
        public string ReceivedMaterialNotes { get => _receivedMaterialNotes; set { _receivedMaterialNotes = value ?? ""; OnPropertyChanged(); } }
        public string ReceivedMaterialStorageLocation { get => _receivedMaterialStorageLocation; set { _receivedMaterialStorageLocation = value ?? ""; OnPropertyChanged(); } }
        public bool ReceiveMaterialMultipleHeatLots { get => _receiveMaterialMultipleHeatLots; set { if (_receiveMaterialMultipleHeatLots != value) { _receiveMaterialMultipleHeatLots = value; OnPropertyChanged(); EnsureHeatLotMode(); RaiseMaterialReceivingChanged(); } } }
        public string MaterialHeatLotModeText => ReceiveMaterialMultipleHeatLots ? "Yes, multiple heat lots" : "No, one heat lot";
        public string MaterialHeatLotTotalText => $"{MaterialHeatLotTotal():0.####} {ReceivedMaterialUnit}".Trim();
        public string MaterialDifferenceText => double.TryParse(ReceivedMaterialQuantity, NumberStyles.Float, CultureInfo.InvariantCulture, out var qty)
            ? $"{MaterialHeatLotTotal() - qty:0.####} {ReceivedMaterialUnit}".Trim()
            : "--";
        public string MaterialReceivingWarning => BuildMaterialReceivingWarning();
        public string InspectionAvailabilityMessage => HasOperationInspection
            ? InspectionSubtitle
            : "No inspection tasks are linked to this operation.";
        public string InspectionTitle => InspectionPackage == null
            ? "Inspection Tasks"
            : string.IsNullOrWhiteSpace(InspectionPackage.FeatureSetName) ? "Inspection Tasks" : InspectionPackage.FeatureSetName;
        public string InspectionSubtitle => InspectionPackage == null
            ? "Inspection entry will appear when an operation is selected."
            : (InspectionTasks.Count == 0
                ? "No inspection tasks available for the current context."
                : _inspectionResultSubmissionAvailable
                    ? $"Loaded {InspectionTasks.Count} inspection tasks from RunBook Service."
                    : $"Loaded {InspectionTasks.Count} inspection tasks from RunBook Service. Inspection result submission is not available on Workstation yet.");
        public bool HasOperationInspection => SelectedOperation != null && (InspectionTasks.Count > 0 || PacketInspectionDocuments.Count > 0);
        public bool HasPacketDocuments => PacketDrawingDocuments.Count > 0 || PacketBalloonedDrawingDocuments.Count > 0 || PacketInspectionDocuments.Count > 0 || PacketOperationReferences.Count > 0;
        public bool HasBalloonGeometry => _operationPacket?.HasBalloonGeometry == true;
        public string PacketDocumentSummary => _operationPacket == null
            ? _operationPacketLoadStatus
            : $"{PacketDrawingDocuments.Count} drawing(s), {PacketBalloonedDrawingDocuments.Count} ballooned drawing(s), {PacketInspectionDocuments.Count} inspection reference(s), {PacketOperationReferences.Count} operation reference(s).";
        public string BalloonGeometryStatus => HasBalloonGeometry
            ? "Balloon geometry is available from Service."
            : "Exact balloon geometry is not exposed by Service yet. Balloon number mapping is available.";
        public string InspectionOverlayTypeLabel => InspectionPackage == null
            ? "Inspection Review"
            : string.IsNullOrWhiteSpace(InspectionPackage.FeatureSetName)
                ? InspectionPackage.TemplateKey
                : InspectionPackage.FeatureSetName;
        public bool UseInProcessInspectionLayout => IsInProcessInspection(InspectionPackage);
        public bool UseSideBySideInspectionLayout => !UseInProcessInspectionLayout;
        public string SelectedInspectionBalloonLabel => SelectedInspectionTask == null
            ? "Select a report row to highlight its balloon."
            : SelectedInspectionTask.BalloonNumber > 0
                ? $"Balloon #{SelectedInspectionTask.BalloonNumber} selected"
                : "Selected row has no balloon number from Service.";
        public string SelectedInspectionFeatureLabel => SelectedInspectionTask == null
            ? "No inspection row selected."
            : $"Feature {SelectedInspectionTask.FeatureId} - {SelectedInspectionTask.FeatureText}";
        public bool IsMobileCaptureDialogOpen
        {
            get => _isMobileCaptureDialogOpen;
            private set
            {
                if (_isMobileCaptureDialogOpen == value)
                    return;
                _isMobileCaptureDialogOpen = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }

        public string SelectedMobileCaptureType
        {
            get => _selectedMobileCaptureType;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "Other Evidence" : value.Trim();
                if (string.Equals(_selectedMobileCaptureType, normalized, StringComparison.Ordinal))
                    return;
                _selectedMobileCaptureType = normalized;
                OnPropertyChanged();
            }
        }

        public string MobileCaptureUrl
        {
            get => _mobileCaptureUrl;
            private set
            {
                if (string.Equals(_mobileCaptureUrl, value, StringComparison.Ordinal))
                    return;
                _mobileCaptureUrl = value ?? "";
                OnPropertyChanged();
            }
        }

        public string MobileCaptureExpiresText
        {
            get => _mobileCaptureExpiresText;
            private set
            {
                if (string.Equals(_mobileCaptureExpiresText, value, StringComparison.Ordinal))
                    return;
                _mobileCaptureExpiresText = value ?? "";
                OnPropertyChanged();
            }
        }

        public BitmapImage? MobileCaptureQrImage
        {
            get => _mobileCaptureQrImage;
            private set
            {
                _mobileCaptureQrImage = value;
                OnPropertyChanged();
            }
        }

        public BitmapImage? OperationAttachmentIntakeQrImage
        {
            get => _operationAttachmentIntakeQrImage;
            private set
            {
                _operationAttachmentIntakeQrImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasOperationAttachmentIntakeQr));
                OnPropertyChanged(nameof(OperationAttachmentIntakePlaceholderVisibility));
            }
        }

        public bool HasOperationAttachmentIntakeQr => OperationAttachmentIntakeQrImage != null;
        public Visibility OperationAttachmentIntakePlaceholderVisibility => HasOperationAttachmentIntakeQr ? Visibility.Collapsed : Visibility.Visible;

        public string OperationAttachmentIntakeUrl
        {
            get => _operationAttachmentIntakeUrl;
            private set
            {
                if (string.Equals(_operationAttachmentIntakeUrl, value, StringComparison.Ordinal))
                    return;
                _operationAttachmentIntakeUrl = value ?? "";
                OnPropertyChanged();
            }
        }

        public string OperationAttachmentIntakeStatusText
        {
            get => _operationAttachmentIntakeStatusText;
            private set
            {
                if (string.Equals(_operationAttachmentIntakeStatusText, value, StringComparison.Ordinal))
                    return;
                _operationAttachmentIntakeStatusText = value ?? "";
                OnPropertyChanged();
            }
        }

        public string OperationAttachmentSummary => OperationAttachments.Count == 0
            ? "No mobile evidence has been captured for this operation yet."
            : $"{OperationAttachments.Count} operation attachment{(OperationAttachments.Count == 1 ? "" : "s")} captured.";
        public string SelectedInspectionTaskTitle => SelectedInspectionTask == null
            ? "Select an inspection item"
            : (SelectedInspectionTask.BalloonNumber > 0 ? $"Balloon {SelectedInspectionTask.BalloonNumber}" : $"Feature {SelectedInspectionTask.FeatureId}");
        public string SelectedInspectionTaskSummary => SelectedInspectionTask == null
            ? "Measured value entry will appear here."
            : $"{SelectedInspectionTask.FeatureText} - {SelectedInspectionTask.InputType}";
        public string PasscodeDialogTitle => SelectedRosterEmployee?.DisplayName ?? "Employee Sign In";
        public string PasscodeDialogSubtitle => SelectedRosterEmployee == null ? "Select an employee to continue." : SelectedRosterEmployee.Role;
        public string PasscodeDialogModuleSummary => SelectedRosterEmployee?.AccessSummary ?? "";
        public string PasscodeDialogActionTitle => _pendingWelcomeModuleKey switch
        {
            "timeclock" => "Continue to Punch In / Out",
            "workorders" => "Continue to Work Orders",
            "drawings" => "Continue to Drawings",
            "inspection" => "Continue to Inspection Reports",
            _ => "Continue to Workstation"
        };
        public string PasscodeDialogEmployeeStatusText => SelectedRosterEmployee?.StatusChipText ?? "";
        public bool ShowPasscodeDialogEmployeeStatus => !string.IsNullOrWhiteSpace(PasscodeDialogEmployeeStatusText);
        public string PasscodeDialogEmployeeStatusForeground => string.IsNullOrWhiteSpace(SelectedRosterEmployee?.StatusChipForeground) ? "#FF2BD576" : SelectedRosterEmployee!.StatusChipForeground;
        public string PasscodeDialogEmployeeStatusBackground => string.IsNullOrWhiteSpace(SelectedRosterEmployee?.StatusChipBackground) ? "#142BD576" : SelectedRosterEmployee!.StatusChipBackground;
        public string PasscodeDialogEmployeeStatusBorder => string.IsNullOrWhiteSpace(SelectedRosterEmployee?.StatusChipBorder) ? "#332BD576" : SelectedRosterEmployee!.StatusChipBorder;
        public string PasscodeMaskDisplay => _passcode.Length == 0 ? "Enter code" : string.Join(" ", _passcode.Select(_ => "\u2022"));
        public string PasscodeDialogMessageText => IsPasscodeDialogOpen ? StatusText : "";
        public bool ShowPasscodeDialogMessage => IsPasscodeDialogOpen && !string.IsNullOrWhiteSpace(PasscodeDialogMessageText);
        public bool PasscodeDigitOneFilled => _passcode.Length >= 1;
        public bool PasscodeDigitTwoFilled => _passcode.Length >= 2;
        public bool PasscodeDigitThreeFilled => _passcode.Length >= 3;
        public bool PasscodeDigitFourFilled => _passcode.Length >= 4;
        public bool PasscodeDigitFiveFilled => _passcode.Length >= 5;
        public bool PasscodeDigitSixFilled => _passcode.Length >= 6;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetControlPassword(string password) => SettingsControlPassword = password ?? "";

        public void ClearSupervisorCredentials()
        {
            SettingsControlEmail = "";
            SettingsControlPassword = "";
        }

        public void NotifyUserActivity()
        {
            if (CurrentSession == null)
                return;

            _lastInteractionUtc = DateTime.UtcNow;
            OnPropertyChanged(nameof(IdleLogoutBadgeText));
        }

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
                    _ = SubmitPasscodeUnlockAsync();
                return true;
            }

            if (key == Key.Escape)
            {
                CancelPasscodeDialog();
                return true;
            }

            return false;
        }

        private Task SubmitPasscodeUnlockAsync()
        {
            var unlockTrace = GetOrCreateUnlockTrace(SelectedRosterEmployee);
            LogUnlockTrace(unlockTrace, "passcode_submit_click", $"passcode_digits={_passcode.Length}");
            return LoginAsync();
        }

        private async Task LoginAsync()
        {
            var unlockTrace = GetOrCreateUnlockTrace(SelectedRosterEmployee);
            LogUnlockTrace(unlockTrace, "login_async_entry", $"passcode_digits={_passcode.Length}");
            if (!CanConfirmPasscode || SelectedRosterEmployee == null)
            {
                StatusText = "Enter a 4 to 6 digit passcode.";
                return;
            }

            var passcodeValidationStopwatch = Stopwatch.StartNew();
            NormalizeLocalShopScope();
            var authSettingsValid = ValidateLocalAuthSettings();
            LogUnlockTrace(unlockTrace, "passcode_validation_complete", $"duration_ms={passcodeValidationStopwatch.ElapsedMilliseconds};auth_settings_valid={authSettingsValid}");
            if (!authSettingsValid)
                return;

            var submittedPasscode = _passcode;
            await RunBusyAsync(async () =>
            {
                var authHealth = GetAuthCacheHealth();
                UpdateAuthCacheStatus(authHealth);
                var employee = SelectedRosterEmployee;

                if (employee == null)
                {
                    StatusText = "Select an employee and enter a 4 to 6 digit passcode.";
                    ClearPasscode();
                    return;
                }

                var loginStopwatch = Stopwatch.StartNew();
                var login = await _api.LoginLocalEmployeeAsync(
                    Settings,
                    employee.RemoteEmployeeId,
                    employee.EmployeeId,
                    employee.EmployeeCode,
                    submittedPasscode,
                    CancellationToken.None,
                    unlockTrace?.TraceId);
                LogUnlockTrace(unlockTrace, "login_local_employee_async_complete", $"duration_ms={loginStopwatch.ElapsedMilliseconds}");

                var payload = MapCapabilityPayload(login.Payload);
                var modules = payload.Modules
                    .Where(entry => entry.Value)
                    .Select(entry => entry.Key)
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var sessionAssignmentStopwatch = Stopwatch.StartNew();
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
                LogUnlockTrace(unlockTrace, "session_assignment_complete", $"duration_ms={sessionAssignmentStopwatch.ElapsedMilliseconds}");
                NotifyUserActivity();
                WorkstationStorageService.SaveSession(snapshot);
                MarkLocalHostRequestSuccess();
                Settings.WorkstationName = Registration?.WorkstationName ?? Settings.WorkstationName;
                WorkstationStorageService.SaveSettings(Settings);
                var buildModulesStopwatch = Stopwatch.StartNew();
                BuildVisibleModules();
                LogUnlockTrace(unlockTrace, "build_visible_modules_complete", $"duration_ms={buildModulesStopwatch.ElapsedMilliseconds};module_count={VisibleModules.Count}");
                var desiredModuleKey = string.Equals(_pendingWelcomeModuleKey, "inspection", StringComparison.OrdinalIgnoreCase) ? "workorders" : _pendingWelcomeModuleKey;
                SelectedModule = VisibleModules.FirstOrDefault(module => string.Equals(module.Key, desiredModuleKey, StringComparison.OrdinalIgnoreCase))
                    ?? VisibleModules.FirstOrDefault();
                LogUnlockTrace(unlockTrace, "shell_transition_ready", $"selected_module={SelectedModule?.Key ?? ""}");
                StatusText = $"Signed in as {SessionEmployeeName}.";
                OfflineStatus = "Local workstation session is active.";
                _passcode = "";
                IsPasscodeDialogOpen = false;
                CloseEmployeeBrowser();
                SelectedRosterEmployee = null;
                OnPropertyChanged(nameof(PasscodeMaskDisplay));
                NotifyPasscodeEntryVisuals();
                UpdateSessionCountdown();
            });

            if (CurrentSession != null)
            {
                var openInspectionAfterHydrate = string.Equals(_pendingWelcomeModuleKey, "inspection", StringComparison.OrdinalIgnoreCase);
                _ = HydrateSignedInShellAsync(CurrentSession.Token);
                if (openInspectionAfterHydrate)
                    OpenInspectionReportsExperience();
                _pendingWelcomeModuleKey = "";
            }
        }

        private async Task RefreshRegistrationAsync()
        {
            await RunBusyAsync(async () =>
            {
                await TrySyncCurrentShopIfConfirmedAsync();
                await EnsureRegistrationAsync();
                await RefreshAuthPackageAsyncCore(true);
                await LoadRosterCoreAsync();
                StatusText = "Workstation registration and employee auth refreshed.";
            });
        }

        public void TraceSignedInShellVisible()
        {
            if (_unlockTimingTrace == null || _unlockTimingTrace.ShellVisibleLogged || CurrentSession == null || !ShowModulesShell)
                return;

            _unlockTimingTrace.ShellVisibleLogged = true;
            LogUnlockTrace(_unlockTimingTrace, "signed_in_shell_visible", $"selected_module={SelectedModule?.Key ?? ""}");
        }

        private async Task RefreshEmployeeAuthAsync()
        {
            await RunBusyAsync(async () =>
            {
                await TrySyncCurrentShopIfConfirmedAsync();

                var refreshed = await RefreshAuthPackageAsyncCore(true);
                await LoadRosterCoreAsync();
                if (refreshed)
                    StatusText = "Employee auth package refreshed from the local workstation authority.";
            });
        }

        private async Task RefreshTimeClockAsync()
        {
            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            await RunBusyAsync(async () =>
            {
                await RefreshLocalTimeclockStateAsync();
                await SyncPendingQueueAsync(false);
            });
        }

        private async Task SubmitPunchAsync(string eventType, string note)
        {
            if (!EnsureServiceWritable())
                return;

            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            var pausesProduction = IsProductionPausePunch(eventType);
            var activeOperation = pausesProduction ? GetActiveOperationForCurrentSession() : null;
            var resumeAfterPunch = IsProductionResumePunch(eventType);
            if (activeOperation != null && IsClockOutPunch(eventType))
            {
                var result = MessageBox.Show(
                    $"You are currently working on OP{activeOperation.OperationNumber:000} - {activeOperation.Title}.\nChoose how to handle this operation before clocking out.\n\nOK pauses the operation and clocks you out. Cancel leaves your shift and operation unchanged.",
                    "Active operation in progress",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.OK)
                {
                    StatusText = "Clock out canceled. Active operation remains in progress.";
                    return;
                }
            }

            if (activeOperation != null)
            {
                _pendingResumeOperation = BuildResumeOperationContext(activeOperation);
                _pendingResumeOperationCapturedFromPunchOut = _pendingResumeOperation != null;
            }

            var punchSubmitted = false;
            await RunBusyAsync(async () =>
            {
                if (!CanSubmitEvent(eventType))
                {
                    StatusText = $"Cannot record {ToEventLabel(eventType)} while state is {CurrentShiftStatus}.";
                    return;
                }

                EnqueuePunch(eventType, note);
                await SyncPendingQueueAsync(false);
                punchSubmitted = true;
                if (activeOperation != null)
                    StatusText = ActiveOperationPausedForClockOutMessage;
            });

            if (punchSubmitted && resumeAfterPunch && string.Equals(TimeClockStateKey, "working", StringComparison.OrdinalIgnoreCase))
                await PromptToResumePausedOperationAsync();
        }

        private void Logout()
        {
            CurrentSession = null;
            _pendingWelcomeModuleKey = "";
            EmployeeQuickSearchText = "";
            CloseEmployeeBrowser();
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
            _lastInteractionUtc = DateTime.UtcNow;
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
            OnPropertyChanged(nameof(IdleLogoutBadgeText));
            OnPropertyChanged(nameof(ShowIdleLogoutBadge));
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

        private void ExecutePrimaryOperatorAction()
        {
            if (!IsLoggedIn)
            {
                StatusText = "Select an operator card below to continue.";
                return;
            }

            var targetModule = VisibleModules.FirstOrDefault(module => string.Equals(module.Key, "workorders", StringComparison.OrdinalIgnoreCase))
                ?? VisibleModules.FirstOrDefault(module => string.Equals(module.Key, "home", StringComparison.OrdinalIgnoreCase))
                ?? VisibleModules.FirstOrDefault();

            if (targetModule != null)
                SelectModule(targetModule);
        }

        private void LaunchWelcomeFlow(string? key)
        {
            var normalized = (key ?? "").Trim().ToLowerInvariant();
            if (normalized.Length == 0)
                return;

            _pendingWelcomeModuleKey = normalized;

            if (!IsLoggedIn)
            {
                if (RosterEmployees.Count > 0)
                {
                    OpenEmployeeBrowser();
                    StatusText = normalized switch
                    {
                        "timeclock" => "Select an employee to continue into Time Clock.",
                        "workorders" => "Select an employee to continue into Work Orders.",
                        "drawings" => "Select an employee to continue into Drawings.",
                        "inspection" => "Select an employee to continue into Inspection Reports.",
                        _ => "Select an employee to continue."
                    };
                }
                else
                {
                    StatusText = "Choose an action above, then select your employee profile when the workstation roster is ready.";
                }
                return;
            }

            SelectModuleByKey(string.Equals(_pendingWelcomeModuleKey, "inspection", StringComparison.OrdinalIgnoreCase) ? "workorders" : _pendingWelcomeModuleKey);
            if (normalized == "inspection")
                OpenInspectionReportsExperience();
        }

        private async Task SubmitEmployeeQuickSearchAsync()
        {
            var query = (EmployeeQuickSearchText ?? "").Trim();
            if (RosterEmployees.Count == 0)
            {
                StatusText = "Employee profiles will appear here after the workstation roster finishes loading.";
                await Task.CompletedTask;
                return;
            }

            if (query.Length == 0)
            {
                OpenEmployeeBrowser();
                StatusText = "Search by employee name or badge, or choose from the employee list.";
                return;
            }

            var match = EmployeeBrowserEmployees.FirstOrDefault();
            if (match == null)
            {
                OpenEmployeeBrowser();
                StatusText = $"No roster match yet for \"{query}\". Try another name or open the full employee list.";
                return;
            }

            OpenRosterEmployee(match);
            await Task.CompletedTask;
        }

        private void OpenEmployeeBrowser()
        {
            IsEmployeeBrowserOpen = true;
            OnPropertyChanged(nameof(EmployeeBrowserEmployees));
        }

        private void CloseEmployeeBrowser()
        {
            IsEmployeeBrowserOpen = false;
        }

        private void SaveSettings()
        {
            Settings.ControlBaseUrl = (SettingsBaseUrl ?? "").Trim();
            Settings.DesktopBaseUrl = (SettingsDesktopBaseUrl ?? "").Trim();
            Settings.ShopId = (SettingsShopId ?? "").Trim();
            Settings.ShopName = (SettingsShopName ?? "").Trim();
            Settings.WorkstationName = (SettingsWorkstationName ?? "").Trim();
            Settings.PairingCode = NormalizePairingCode(SettingsPairingCode);
            NormalizeDesktopBaseUrl();
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
                _isControlConfirmedAvailable = true;
                ClearSupervisorCredentials();
                ControlSessionStatus = ControlSessionService.GetStatusLabel();
                OnPropertyChanged(nameof(HasSupervisorSession));
                OnPropertyChanged(nameof(ShowSupervisorSignInFields));
                OnPropertyChanged(nameof(ShowSupervisorEnrollmentActions));
                OnPropertyChanged(nameof(SupervisorDialogTitle));
                OnPropertyChanged(nameof(SupervisorDialogSubtitle));
                return Task.CompletedTask;
            });

            await RunBusyAsync(async () =>
            {
                await TrySyncCurrentShopIfConfirmedAsync();
                await LoadRosterCoreAsync();
                StatusText = $"Supervisor session ready for {Settings.ShopName}.";
                RefreshConnectionStatuses();
            });
        }

        private void DisconnectControl()
        {
            ControlSessionService.SignOut();
            _isControlConfirmedAvailable = false;
            ClearSupervisorCredentials();
            ControlSessionStatus = ControlSessionService.GetStatusLabel();
            OnPropertyChanged(nameof(HasSupervisorSession));
            OnPropertyChanged(nameof(ShowSupervisorSignInFields));
            OnPropertyChanged(nameof(ShowSupervisorEnrollmentActions));
            OnPropertyChanged(nameof(SupervisorDialogTitle));
            OnPropertyChanged(nameof(SupervisorDialogSubtitle));
            RefreshConnectionStatuses();
            LoadCachedAuthCache();
            StatusText = "Supervisor session cleared. Cached local employee auth remains available.";
        }

        private async Task EnsureRegistrationAsync()
        {
            NormalizeLocalShopScope();
            SaveSettings();
            if (string.IsNullOrWhiteSpace(Settings.ShopId))
                throw new InvalidOperationException("Shop ID is required before workstation registration with RunBook Service.");

            if (HasUsableSavedRegistration())
            {
                MarkRegistrationTrusted("saved_registration_reused");
                StatusText = "Using the saved trusted workstation pairing.";
                return;
            }

            if (Registration != null &&
                !string.IsNullOrWhiteSpace(Registration.ShopId) &&
                !string.Equals(Registration.ShopId, Settings.ShopId, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(Settings.PairingCode))
            {
                Registration.PairingRequiredReason = "shop_mismatch";
                WorkstationStorageService.SaveRegistration(Registration);
                throw new InvalidOperationException("PAIRING_REQUIRED: This workstation is paired to a different shop. Switch shops or pair this workstation again.");
            }

            if (string.IsNullOrWhiteSpace(Settings.PairingCode))
            {
                if (Registration == null)
                    throw new InvalidOperationException("PAIRING_REQUIRED: This workstation has not been paired yet. Enter a pairing code to connect it to your shop.");

                Registration.PairingRequiredReason = "missing_or_incomplete_trust";
                WorkstationStorageService.SaveRegistration(Registration);
                throw new InvalidOperationException("PAIRING_REQUIRED: This workstation's saved pairing could not be verified. Please pair again.");
            }

            var registration = await _api.RegisterAsync(Settings, CancellationToken.None);
            Registration = registration;
            MarkRegistrationTrusted("paired");
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
            NotifyUserActivity();
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

            _unlockTimingTrace = UnlockTimingTrace.Start(employee);
            LogUnlockTrace(_unlockTimingTrace, "roster_tile_click");
            CloseEmployeeBrowser();
            SelectedRosterEmployee = employee;
            _passcode = "";
            IsPasscodeDialogOpen = true;
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            NotifyPasscodeEntryVisuals();
            StatusText = employee.HasWorkstationPasscode
                ? $"Enter the passcode for {employee.DisplayName}."
                : $"{employee.DisplayName} may need an employee auth refresh if the passcode was just configured. Enter the passcode to try RunBook Service validation.";
        }

        private void AppendPasscodeDigit(string? digit)
        {
            if (string.IsNullOrWhiteSpace(digit) || !char.IsDigit(digit[0]) || _passcode.Length >= 6)
                return;

            _passcode += digit[0];
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            NotifyPasscodeEntryVisuals();
            RaiseCommandStates();
        }

        private void RemovePasscodeDigit()
        {
            if (_passcode.Length == 0)
                return;

            _passcode = _passcode[..^1];
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            NotifyPasscodeEntryVisuals();
            RaiseCommandStates();
        }

        private void ClearPasscode()
        {
            if (_passcode.Length == 0)
                return;

            _passcode = "";
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            NotifyPasscodeEntryVisuals();
            RaiseCommandStates();
        }

        private void CancelPasscodeDialog()
        {
            _passcode = "";
            IsPasscodeDialogOpen = false;
            SelectedRosterEmployee = null;
            _pendingWelcomeModuleKey = "";
            OnPropertyChanged(nameof(PasscodeMaskDisplay));
            NotifyPasscodeEntryVisuals();
            StatusText = "Workstation ready.";
        }

        private void NotifyPasscodeEntryVisuals()
        {
            OnPropertyChanged(nameof(PasscodeDigitOneFilled));
            OnPropertyChanged(nameof(PasscodeDigitTwoFilled));
            OnPropertyChanged(nameof(PasscodeDigitThreeFilled));
            OnPropertyChanged(nameof(PasscodeDigitFourFilled));
            OnPropertyChanged(nameof(PasscodeDigitFiveFilled));
            OnPropertyChanged(nameof(PasscodeDigitSixFilled));
        }

        private void OpenInspectionReportsExperience()
        {
            if (!HasWorkOrdersAccess)
            {
                StatusText = "Inspection reports are not available for this employee.";
                return;
            }

            SelectModuleByKey("workorders");
            if (HasOperationInspection)
            {
                OpenOperationInspection();
                return;
            }

            StatusText = "Inspection reports open from Work Orders when an operation has linked inspection documents.";
        }

        private void OpenSupervisorDialog()
        {
            ClearSupervisorCredentials();
            IsSupervisorDialogOpen = true;
            StatusText = HasSupervisorSession
                ? "Supervisor tools are ready."
                : "Supervisor sign-in is required before registration codes can be used.";
        }

        private void CloseSupervisorDialog()
        {
            ClearSupervisorCredentials();
            IsSupervisorDialogOpen = false;
        }

        private void ShowRunBookAlert(string title, string body)
        {
            _runBookAlertCompletion = null;
            RunBookAlertTitle = title;
            RunBookAlertBody = body;
            RunBookAlertPrimaryText = "OK";
            RunBookAlertSecondaryText = "";
            ShowRunBookAlertSecondaryAction = false;
            IsRunBookAlertOpen = true;
        }

        private void ShowRunBookIssueAlert(string key, string title, string whatHappened, string howToFix)
        {
            var normalizedKey = string.IsNullOrWhiteSpace(key) ? title : key.Trim();
            if (string.Equals(_lastRunBookIssueAlertKey, normalizedKey, StringComparison.OrdinalIgnoreCase) && IsRunBookAlertOpen)
                return;

            _lastRunBookIssueAlertKey = normalizedKey;
            var body = $"What happened:\n{whatHappened.Trim()}\n\nHow to fix:\n{howToFix.Trim()}";
            ShowRunBookAlert(title, body);
        }

        private Task<bool> ShowRunBookDecisionAsync(string title, string body, string primaryText, string secondaryText)
        {
            _runBookAlertCompletion = new TaskCompletionSource<bool>();
            RunBookAlertTitle = title;
            RunBookAlertBody = body;
            RunBookAlertPrimaryText = string.IsNullOrWhiteSpace(primaryText) ? "OK" : primaryText.Trim();
            RunBookAlertSecondaryText = string.IsNullOrWhiteSpace(secondaryText) ? "Cancel" : secondaryText.Trim();
            ShowRunBookAlertSecondaryAction = true;
            IsRunBookAlertOpen = true;
            return _runBookAlertCompletion.Task;
        }

        private void CompleteRunBookAlert(bool result)
        {
            var completion = _runBookAlertCompletion;
            _runBookAlertCompletion = null;
            IsRunBookAlertOpen = false;
            ShowRunBookAlertSecondaryAction = false;
            RunBookAlertTitle = "";
            RunBookAlertBody = "";
            RunBookAlertPrimaryText = "OK";
            RunBookAlertSecondaryText = "";
            completion?.TrySetResult(result);
        }

        private async Task BackgroundSyncAsync()
        {
            if (IsBusy || _isBackgroundSyncRunning)
                return;

            _isBackgroundSyncRunning = true;
            try
            {
                if (_lastAuthRefreshAttemptUtc == DateTime.MinValue || DateTime.UtcNow - _lastAuthRefreshAttemptUtc >= AuthRefreshInterval)
                    await RefreshAuthPackageAsyncCore(false);

                await SyncPendingQueueAsync(false);
            }
            catch (Exception ex)
            {
                DebugLogService.WriteException("BackgroundSyncAsync", ex);
                if (IsConnectivityFailure(ex))
                {
                    MarkLocalHostRequestFailure(ex.Message);
                    OfflineStatus = "RunBook.Service is unavailable right now.";
                    return;
                }

                StatusText = GetUserFacingErrorMessage(ex);
            }
            finally
            {
                _isBackgroundSyncRunning = false;
            }
        }

        private async Task HydrateSignedInShellAsync(string sessionToken)
        {
            if (_isPostLoginHydrationRunning)
                return;

            _isPostLoginHydrationRunning = true;
            try
            {
                if (CurrentSession == null || !string.Equals(CurrentSession.Token, sessionToken, StringComparison.Ordinal))
                    return;

                LoadQueue();

                if (HasTimeClockAccess)
                {
                    await RefreshLocalTimeclockStateAsync();
                    if (CurrentSession == null || !string.Equals(CurrentSession.Token, sessionToken, StringComparison.Ordinal))
                        return;

                    await SyncPendingQueueAsync(false);
                }

                if (CurrentSession != null &&
                    string.Equals(CurrentSession.Token, sessionToken, StringComparison.Ordinal) &&
                    HasWorkOrdersAccess)
                {
                    await RefreshWorkOrdersAsync(false);
                }
            }
            catch (Exception ex)
            {
                DebugLogService.WriteException("HydrateSignedInShellAsync", ex);
            }
            finally
            {
                _isPostLoginHydrationRunning = false;
            }
        }

        private async Task RefreshCapabilityPayloadAsync(bool allowFallback)
        {
            if (CurrentSession == null)
                return;

            try
            {
                var response = await _api.GetSessionMeAsync(Settings, CurrentSession, CancellationToken.None);
                MarkLocalHostRequestSuccess();
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
            OnPropertyChanged(nameof(LoginHeadline));
            OnPropertyChanged(nameof(LoginInstructionLine));
            OnPropertyChanged(nameof(CurrentOperatorLine));
            OnPropertyChanged(nameof(CurrentOperatorStatusLine));
            OnPropertyChanged(nameof(PrimaryOperatorActionText));
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
                    ? "Service has no assigned or backup work orders available for this employee."
                    : $"Loaded {WorkOrders.Count} workstation jobs from Service. Assigned {_assignedWorkOrderCount}, backup {_backupWorkOrderCount}.";
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
                StatusText = $"Loaded {workOrder.WorkOrderNumber} from Service.";
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
                StatusText = $"Resumed {CurrentJob.WorkOrderNumber} from Service.";
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
                StatusText = $"Loaded recent job {RecentJob.WorkOrderNumber} from Service.";
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

            if (SelectedOperation != null)
                await RefreshOperationPacketAsync(false);
        }

        private async Task SelectOperationAsync(WorkstationWorkOrderOperationSummary? operation)
        {
            if (operation == null)
                return;

            SelectedOperation = operation;
            await RefreshOperationPacketAsync(false);
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
                    throw new InvalidOperationException("RunBook Service returned an empty drawing payload.");

                var tempFolder = Path.Combine(WorkstationStorageService.TempFolder, "drawings");
                Directory.CreateDirectory(tempFolder);
                var fileName = string.IsNullOrWhiteSpace(response.FileName) ? $"{drawing.DrawingId}.bin" : response.FileName;
                var localPath = Path.Combine(tempFolder, fileName);
                File.WriteAllBytes(localPath, bytes);
                Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
                StatusText = $"Opened {drawing.Label} in the default viewer.";
            });
        }

        private async Task OpenPacketDocumentAsync(RunBookWorkstationApiClient.OperationPacketDocument? document)
        {
            if (document == null || CurrentSession == null)
                return;

            var route = string.IsNullOrWhiteSpace(document.DownloadRoute) ? document.PreviewRoute : document.DownloadRoute;
            if (string.IsNullOrWhiteSpace(route))
            {
                StatusText = "This packet document does not have a Service download route yet.";
                return;
            }

            await RunBusyAsync(async () =>
            {
                var response = await _api.DownloadPacketDocumentAsync(Settings, CurrentSession, route, CancellationToken.None);
                var bytes = response.GetBytes();
                if (bytes.Length == 0)
                    throw new InvalidOperationException("RunBook Service returned an empty document payload.");

                var tempFolder = Path.Combine(WorkstationStorageService.TempFolder, "packet-documents");
                Directory.CreateDirectory(tempFolder);
                var fileName = string.IsNullOrWhiteSpace(response.FileName) ? $"{document.Id}.bin" : response.FileName;
                var localPath = Path.Combine(tempFolder, fileName);
                File.WriteAllBytes(localPath, bytes);
                Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
                StatusText = $"Opened {document.Title} in the default viewer.";
            });
        }

        private async Task ExecuteOperationAsync(string action)
        {
            if (!EnsureServiceWritable())
                return;

            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null || !HasOperationExecutionAccess)
                return;

            if (string.Equals((action ?? "").Trim(), "complete", StringComparison.OrdinalIgnoreCase) && IsReceiveMaterialOperation)
            {
                await SubmitMaterialReceivingAsync(completeOperation: true);
                return;
            }

            if (string.Equals((action ?? "").Trim(), "start", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(TimeClockStateKey, "working", StringComparison.OrdinalIgnoreCase))
            {
                ShowRunBookAlert("Clock In Required", ClockedOutOperationStartMessage);
                return;
            }

            if (string.Equals((action ?? "").Trim(), "start", StringComparison.OrdinalIgnoreCase) &&
                SelectedOperation.CanStart != true &&
                SelectedOperation.IsPaused != true)
            {
                ShowRunBookAlert("Operation Not Available", BuildStartOperationUnavailableMessage());
                return;
            }

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
            if (!EnsureServiceWritable())
                return;

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

        private async Task SaveDataEntryAsync()
        {
            if (IsReceiveMaterialOperation)
            {
                await SubmitMaterialReceivingAsync(completeOperation: false);
                return;
            }

            if (ShowDataEntryQuantityField)
            {
                await SubmitQuantityAsync();
                return;
            }

            if (!string.IsNullOrWhiteSpace(OperationNoteText))
            {
                await SubmitOperationNoteAsync();
                return;
            }

            StatusText = "Enter a value or note before saving progress.";
        }

        private async Task SubmitMaterialReceivingAsync(bool completeOperation)
        {
            if (!EnsureServiceWritable())
                return;
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null)
                return;
            if (PrimaryMaterialRequirement == null)
            {
                StatusText = "No expected material requirement is available for this Receive Material operation.";
                return;
            }
            var warning = BuildMaterialReceivingWarning();
            if (!string.IsNullOrWhiteSpace(warning))
            {
                StatusText = warning;
                return;
            }
            if (!double.TryParse((ReceivedMaterialQuantity ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var receivedQty))
            {
                StatusText = "Received quantity is required.";
                return;
            }

            var request = new RunBookWorkstationApiClient.MaterialReceiptSubmitRequest
            {
                MaterialRequirementId = PrimaryMaterialRequirement.MaterialRequirementId,
                ReceivedShape = ReceivedMaterialShape,
                ReceivedSize = ReceivedMaterialSize,
                ReceivedGrade = ReceivedMaterialGrade,
                ReceivedSpec = ReceivedMaterialSpec,
                ReceivedQuantity = receivedQty,
                ReceivedUnit = ReceivedMaterialUnit,
                ConditionStatus = ReceivedMaterialCondition,
                Notes = ReceivedMaterialNotes,
                StorageLocation = ReceivedMaterialStorageLocation,
                HeatLots = MaterialHeatLots.Select(row => new RunBookWorkstationApiClient.MaterialReceiptHeatLotSubmitRequest
                {
                    HeatLotNumber = row.HeatLotNumber,
                    Quantity = double.TryParse((row.QuantityText ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var qty) ? qty : 0,
                    Unit = FirstNonBlank(row.Unit, ReceivedMaterialUnit),
                    CertReceived = row.CertReceived,
                    Notes = row.Notes
                }).ToList()
            };

            await RunBusyAsync(async () =>
            {
                if (_savedMaterialReceiptId <= 0)
                {
                    var receiptResponse = await _api.SaveMaterialReceiptAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, request, CancellationToken.None);
                    _savedMaterialReceiptId = receiptResponse.MaterialReceiptId;
                    WorkOrderDetail = MapWorkOrderDetail(receiptResponse.WorkOrder);
                    StatusText = string.IsNullOrWhiteSpace(receiptResponse.Message) ? "Material receipt saved." : receiptResponse.Message;
                    await RefreshOperationPacketAsync(false);
                }

                if (completeOperation)
                {
                    var completeResponse = await _api.CompleteOperationAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, OperationActionNoteText, CancellationToken.None);
                    ApplyWorkOrderMutationResponse(completeResponse, SelectedOperation.OperationId);
                    OperationActionNoteText = "";
                    StatusText = "Material receipt submitted and operation completed.";
                }
            });
        }

        private async Task SubmitScrapAsync()
        {
            if (!EnsureServiceWritable())
                return;

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
            if (!EnsureServiceWritable())
                return;

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
            if (!EnsureServiceWritable())
                return;

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
                _inspectionResultSubmissionAvailable = false;
                InspectionPackage = MapInspectionTaskPackage(response.Inspection);
                InspectionTasks.Clear();
                foreach (var task in InspectionPackage?.Tasks ?? Enumerable.Empty<WorkstationInspectionTask>())
                    InspectionTasks.Add(task);
                OnPropertyChanged(nameof(InspectionSubtitle));
                OnPropertyChanged(nameof(HasOperationInspection));
                OnPropertyChanged(nameof(SelectedInspectionBalloonLabel));
                OnPropertyChanged(nameof(SelectedInspectionFeatureLabel));
                RaiseCommandStates();
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
                _inspectionResultSubmissionAvailable = false;
                InspectionPackage = null;
                InspectionTasks.Clear();
                SelectedInspectionTask = null;
                OnPropertyChanged(nameof(InspectionSubtitle));
                OnPropertyChanged(nameof(HasOperationInspection));
                OnPropertyChanged(nameof(SelectedInspectionBalloonLabel));
                OnPropertyChanged(nameof(SelectedInspectionFeatureLabel));
                RaiseCommandStates();
                InspectionActualValue = "";
                InspectionResultNoteText = "";
                if (manual)
                    StatusText = ex.Message;
            }
        }

        private async Task RefreshOperationPacketAsync(bool manual)
        {
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null)
                return;

            try
            {
                _operationPacketLoadStatus = "Loading operation packet from RunBook Service...";
                OnPropertyChanged(nameof(PacketDocumentSummary));
                var response = await _api.GetOperationPacketAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, CancellationToken.None);
                ApplyOperationPacket(response.Packet);
                await LoadCurrentWorkOrderThumbnailAsync(response.Packet);
                if (manual)
                    StatusText = PacketDocumentSummary;
            }
            catch (Exception ex)
            {
                ClearOperationPacket($"Operation packet failed to load: {GetUserFacingErrorMessage(ex)}");
                if (manual)
                    StatusText = PacketDocumentSummary;
            }
        }

        private void ApplyOperationPacket(RunBookWorkstationApiClient.OperationPacket? packet)
        {
            _operationPacket = packet;
            _operationPacketLoadStatus = packet == null ? "Operation packet has not loaded yet." : "Operation packet loaded from RunBook Service.";
            PacketDrawingDocuments.Clear();
            PacketBalloonedDrawingDocuments.Clear();
            PacketInspectionDocuments.Clear();
            PacketOperationReferences.Clear();
            PacketBalloonMarkers.Clear();
            OperationAttachments.Clear();
            InspectionTasks.Clear();

            if (packet != null)
            {
                foreach (var document in packet.DrawingDocuments ?? Enumerable.Empty<RunBookWorkstationApiClient.OperationPacketDocument>())
                    PacketDrawingDocuments.Add(document);
                foreach (var document in packet.BalloonedDrawingDocuments ?? Enumerable.Empty<RunBookWorkstationApiClient.OperationPacketDocument>())
                    PacketBalloonedDrawingDocuments.Add(document);
                foreach (var document in packet.InspectionDocuments ?? Enumerable.Empty<RunBookWorkstationApiClient.OperationPacketDocument>())
                    PacketInspectionDocuments.Add(document);
                foreach (var document in packet.OperationReferences ?? Enumerable.Empty<RunBookWorkstationApiClient.OperationPacketDocument>())
                    PacketOperationReferences.Add(document);
                foreach (var marker in packet.BalloonMarkers ?? Enumerable.Empty<RunBookWorkstationApiClient.BalloonMarker>())
                    PacketBalloonMarkers.Add(marker);
                foreach (var attachment in packet.OperationAttachments ?? Enumerable.Empty<RunBookWorkstationApiClient.OperationAttachment>())
                    OperationAttachments.Add(attachment);
                foreach (var task in packet.InspectionTasks?.Select(MapInspectionTask) ?? Enumerable.Empty<WorkstationInspectionTask>())
                    InspectionTasks.Add(task);
                foreach (var unit in packet.MaterialUnitOptions ?? Enumerable.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(unit) && !MaterialUnitOptions.Any(existing => string.Equals(existing, unit, StringComparison.OrdinalIgnoreCase)))
                        MaterialUnitOptions.Add(unit);
                }

                InspectionPackage = new WorkstationInspectionTaskPackage
                {
                    WorkOrderId = packet.WorkOrderId,
                    OperationId = packet.OperationId,
                    FeatureSetName = PacketInspectionDocuments.FirstOrDefault()?.Title ?? "",
                    TemplateKey = PacketInspectionDocuments.FirstOrDefault()?.Type ?? "TemplateA",
                    Tasks = InspectionTasks.ToList()
                };
            }
            else
            {
                InspectionPackage = null;
            }

            SeedMaterialReceivingFields();
            SelectedInspectionTask = InspectionTasks.FirstOrDefault(task => task.FeatureId == SelectedInspectionTask?.FeatureId) ?? InspectionTasks.FirstOrDefault();
            OnPropertyChanged(nameof(HasOperationInspection));
            OnPropertyChanged(nameof(HasPacketDocuments));
            OnPropertyChanged(nameof(HasBalloonGeometry));
            OnPropertyChanged(nameof(PacketDocumentSummary));
            OnPropertyChanged(nameof(BalloonGeometryStatus));
            OnPropertyChanged(nameof(OperationAttachmentSummary));
            OnPropertyChanged(nameof(InspectionSubtitle));
            OnPropertyChanged(nameof(SelectedInspectionBalloonLabel));
            OnPropertyChanged(nameof(SelectedInspectionFeatureLabel));
            RebuildDataEntrySections();
            RaiseCommandStates();
        }

        private void SeedMaterialReceivingFields()
        {
            var req = PrimaryMaterialRequirement;
            if (req == null)
            {
                _savedMaterialReceiptId = 0;
                return;
            }

            var existingReceipt = _operationPacket?.MaterialReceipts?
                .Where(receipt => receipt.WorkOrderOpId == SelectedOperation?.OperationId)
                .OrderByDescending(receipt => receipt.MaterialReceiptId)
                .FirstOrDefault();
            _savedMaterialReceiptId = existingReceipt?.MaterialReceiptId ?? 0;

            ReceivedMaterialSpec = FirstNonBlank(existingReceipt?.ReceivedSpec, req.ExpectedSpec);
            ReceivedMaterialShape = FirstNonBlank(existingReceipt?.ReceivedShape, req.ExpectedShape);
            ReceivedMaterialSize = FirstNonBlank(existingReceipt?.ReceivedSize, req.ExpectedSize);
            ReceivedMaterialGrade = FirstNonBlank(existingReceipt?.ReceivedGrade, req.ExpectedGrade);
            ReceivedMaterialQuantity = existingReceipt != null && existingReceipt.ReceivedQuantity > 0
                ? existingReceipt.ReceivedQuantity.ToString("0.####", CultureInfo.InvariantCulture)
                : (req.ExpectedQuantity > 0 ? req.ExpectedQuantity.ToString("0.####", CultureInfo.InvariantCulture) : "");
            ReceivedMaterialUnit = FirstNonBlank(existingReceipt?.ReceivedUnit, req.ExpectedUnit, MaterialUnitOptions.FirstOrDefault(), "Bars");
            ReceivedMaterialCondition = FirstNonBlank(existingReceipt?.ConditionStatus, "OK");
            ReceivedMaterialNotes = existingReceipt?.Notes ?? "";

            ClearMaterialHeatLots();
            var traceRows = existingReceipt == null
                ? Enumerable.Empty<RunBookWorkstationApiClient.MaterialTraceDto>()
                : _operationPacket?.MaterialTraces?.Where(trace => trace.MaterialReceiptId == existingReceipt.MaterialReceiptId) ?? Enumerable.Empty<RunBookWorkstationApiClient.MaterialTraceDto>();
            foreach (var trace in traceRows)
            {
                AddMaterialHeatLot(new WorkstationMaterialHeatLotEntry
                {
                    HeatLotNumber = trace.HeatLotNumber,
                    QuantityText = trace.ReceivedQuantity > 0 ? trace.ReceivedQuantity.ToString("0.####", CultureInfo.InvariantCulture) : "",
                    Unit = FirstNonBlank(trace.Unit, ReceivedMaterialUnit),
                    CertReceived = string.Equals(trace.CertStatus, "Received", StringComparison.OrdinalIgnoreCase) || string.Equals(trace.CertStatus, "Attached", StringComparison.OrdinalIgnoreCase),
                    Notes = trace.Notes
                });
            }
            if (MaterialHeatLots.Count == 0)
                AddMaterialHeatLot();
            ReceiveMaterialMultipleHeatLots = MaterialHeatLots.Count > 1;
        }

        private void ClearOperationPacket()
            => ClearOperationPacket("Operation packet has not loaded yet.");

        private void ClearOperationPacket(string status)
        {
            _operationPacket = null;
            _operationPacketLoadStatus = string.IsNullOrWhiteSpace(status) ? "Operation packet has not loaded yet." : status;
            CurrentWorkOrderThumbnailImage = null;
            PacketDrawingDocuments.Clear();
            PacketBalloonedDrawingDocuments.Clear();
            PacketInspectionDocuments.Clear();
            PacketOperationReferences.Clear();
            PacketBalloonMarkers.Clear();
            OperationAttachments.Clear();
            InspectionTasks.Clear();
            InspectionPackage = null;
            SelectedInspectionTask = null;
            OnPropertyChanged(nameof(HasOperationInspection));
            OnPropertyChanged(nameof(HasPacketDocuments));
            OnPropertyChanged(nameof(HasBalloonGeometry));
            OnPropertyChanged(nameof(PacketDocumentSummary));
            OnPropertyChanged(nameof(BalloonGeometryStatus));
            OnPropertyChanged(nameof(OperationAttachmentSummary));
            OnPropertyChanged(nameof(InspectionSubtitle));
            OnPropertyChanged(nameof(SelectedInspectionBalloonLabel));
            OnPropertyChanged(nameof(SelectedInspectionFeatureLabel));
            RebuildDataEntrySections();
        }

        private void RebuildDataEntrySections()
        {
            DataEntrySections.Clear();

            if (SelectedOperation == null)
            {
                RaiseDataEntryPropertiesChanged();
                return;
            }

            var family = GetOperationFamily();
            var sectionNumber = 1;

            var required = new WorkstationDataEntrySection
            {
                Number = sectionNumber++.ToString(CultureInfo.InvariantCulture),
                Title = "Required Inputs",
                Subtitle = BuildRequiredInputsSubtitle(family),
                AccentBrush = "#31C7FF",
                BorderBrush = "#5531C7FF",
                BackgroundBrush = "#D40F1B2F"
            };

            AddIfPresent(required, "Operation type", DataEntryOperationTypeLabel, "Ready");
            AddIfPresent(required, "Work center", FirstNonBlank(_operationPacket?.WorkCenterDisplay, SelectedOperation.WorkCenter), "Ready");
            var materialContext = FirstNonBlank(_operationPacket?.MaterialSummaryDisplay);
            if (string.IsNullOrWhiteSpace(materialContext) && HasMaterialContext(family))
                materialContext = WorkOrderDetail?.PartDescription ?? "";
            AddIfPresent(required, HasMaterialContext(family) ? "Material / trace" : "Reference context", materialContext, HasMaterialContext(family) ? "Ready" : "Optional");
            AddIfPresent(required, "Run notes", FirstNonBlank(_operationPacket?.NotesDisplay, _operationPacket?.DetailNotesDisplay), "Optional");
            if (_operationPacket?.RequireNotes == true)
                required.Items.Add(BuildRequirementRow("Operation note", string.IsNullOrWhiteSpace(OperationNoteText) ? "A note is required by released routing." : "Note entered and ready to save.", string.IsNullOrWhiteSpace(OperationNoteText) ? "Missing" : "Ready"));
            if (required.Items.Count == 0)
                required.Items.Add(BuildRequirementRow("No required inputs for this operation.", "", "Ready"));
            DataEntrySections.Add(required);

            var confirmations = new WorkstationDataEntrySection
            {
                Number = sectionNumber++.ToString(CultureInfo.InvariantCulture),
                Title = "Confirmations",
                Subtitle = BuildConfirmationSubtitle(family),
                AccentBrush = family == "inspection" ? "#9A6BFF" : "#31C7FF",
                BorderBrush = family == "inspection" ? "#6A9A6BFF" : "#5531C7FF",
                BackgroundBrush = family == "inspection" ? "#241A1330" : "#D40F1B2F"
            };

            foreach (var item in _operationPacket?.ChecklistItems ?? Enumerable.Empty<RunBookWorkstationApiClient.OperationPacketChecklistItem>())
                AddIfPresent(confirmations, item.IsRequired ? "Required check" : "Optional check", item.Label, item.IsRequired ? "Missing" : "Optional");
            if ((_operationPacket?.RequireChecklist == true) && confirmations.Items.Count == 0)
                confirmations.Items.Add(BuildRequirementRow("Checklist", "Required by routing, but no released checklist rows were found.", "Missing"));
            if (confirmations.Items.Count > 0)
                DataEntrySections.Add(confirmations);

            if (ShowDataEntryInspectionCard)
            {
                var inspection = new WorkstationDataEntrySection
                {
                    Number = sectionNumber++.ToString(CultureInfo.InvariantCulture),
                    Title = GetOperationFamily() == "inspection" ? "Inspection / Review" : "Linked Inspection",
                    Subtitle = InspectionAvailabilityMessage,
                    AccentBrush = "#9A6BFF",
                    BorderBrush = "#6A9A6BFF",
                    BackgroundBrush = "#241A1330"
                };

                if (InspectionTasks.Count == 0)
                    inspection.Items.Add(BuildRequirementRow("Inspection result entry", "Detailed inspection result submission is not available on Workstation yet.", "Optional"));
                else
                {
                    foreach (var task in InspectionTasks.Take(5))
                        inspection.Items.Add(BuildRequirementRow(task.BalloonNumber > 0 ? $"Balloon {task.BalloonNumber}" : $"Feature {task.FeatureId}", FirstNonBlank(task.FeatureText, task.InputType), string.IsNullOrWhiteSpace(task.ActualValue) ? "Missing" : "Completed"));
                    if (InspectionTasks.Count > 5)
                        inspection.Items.Add(BuildRequirementRow("Additional inspection rows", $"{InspectionTasks.Count - 5} more linked task(s).", "Ready"));
                }

                DataEntrySections.Add(inspection);
            }

            var evidence = new WorkstationDataEntrySection
            {
                Number = sectionNumber++.ToString(CultureInfo.InvariantCulture),
                Title = "Evidence / Attachments",
                Subtitle = BuildEvidenceSubtitle(),
                AccentBrush = "#31C7FF",
                BorderBrush = "#5531C7FF",
                BackgroundBrush = "#D40F1B2F"
            };
            evidence.Items.Add(BuildRequirementRow("Captured evidence", OperationAttachmentSummary, IsEvidenceRequired() && OperationAttachments.Count == 0 ? "Missing" : OperationAttachments.Count > 0 ? "Completed" : "Optional"));
            var evidenceState = OperationAttachments.Count > 0 ? "Completed" : "Missing";
            if (_operationPacket?.RequirePhoto == true) evidence.Items.Add(BuildRequirementRow("Photo", "Required by released routing.", evidenceState));
            if (_operationPacket?.RequireVideo == true) evidence.Items.Add(BuildRequirementRow("Video", "Required by released routing.", evidenceState));
            if (_operationPacket?.RequireAttachment == true) evidence.Items.Add(BuildRequirementRow("Attachment", "Required by released routing.", evidenceState));
            DataEntrySections.Add(evidence);

            RaiseDataEntryPropertiesChanged();
        }

        private void RaiseDataEntryPropertiesChanged()
        {
            OnPropertyChanged(nameof(DataEntryOperationTypeLabel));
            OnPropertyChanged(nameof(DataEntryHeaderSummary));
            OnPropertyChanged(nameof(DataEntryEntryTitle));
            OnPropertyChanged(nameof(DataEntryEntryInstruction));
            OnPropertyChanged(nameof(DataEntryQuantityLabel));
            OnPropertyChanged(nameof(DataEntrySaveProgressText));
            OnPropertyChanged(nameof(DataEntrySubmitText));
            OnPropertyChanged(nameof(DataEntryCanSubmitOperation));
            OnPropertyChanged(nameof(ShowDataEntryQuantityField));
            OnPropertyChanged(nameof(ShowDataEntryScrapField));
            OnPropertyChanged(nameof(ShowDataEntryInspectionCard));
            OnPropertyChanged(nameof(ShowDataEntryEvidenceCard));
            OnPropertyChanged(nameof(ShowDataEntryMissingReason));
            OnPropertyChanged(nameof(DataEntryValidationMessage));
            OnPropertyChanged(nameof(InspectionAvailabilityMessage));
            OnPropertyChanged(nameof(IsReceiveMaterialOperation));
            OnPropertyChanged(nameof(ReceiveMaterialEntryVisibility));
            OnPropertyChanged(nameof(StandardDataEntryVisibility));
            OnPropertyChanged(nameof(ExpectedMaterialSummary));
            OnPropertyChanged(nameof(ExpectedMaterialQuantityText));
            RaiseMaterialReceivingChanged();
        }

        private void RaiseMaterialReceivingChanged()
        {
            OnPropertyChanged(nameof(MaterialHeatLotTotalText));
            OnPropertyChanged(nameof(MaterialDifferenceText));
            OnPropertyChanged(nameof(MaterialReceivingWarning));
            OnPropertyChanged(nameof(MaterialHeatLotModeText));
            if (SaveDataEntryCommand is RelayCommand save) save.RaiseCanExecuteChanged();
            if (CompleteOperationCommand is RelayCommand complete) complete.RaiseCanExecuteChanged();
        }

        private double MaterialHeatLotTotal()
            => MaterialHeatLots.Sum(row => double.TryParse((row.QuantityText ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var qty) ? qty : 0);

        private string BuildMaterialReceivingWarning()
        {
            if (!IsReceiveMaterialOperation)
                return "";
            if (PrimaryMaterialRequirement == null)
                return "No released material context found for this operation.";
            if (!double.TryParse((ReceivedMaterialQuantity ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var receivedQty) || receivedQty <= 0)
                return "Received quantity is required.";
            if (string.IsNullOrWhiteSpace(ReceivedMaterialUnit))
                return "Received unit is required.";
            if (string.IsNullOrWhiteSpace(ReceivedMaterialShape) || string.IsNullOrWhiteSpace(ReceivedMaterialSize) || string.IsNullOrWhiteSpace(ReceivedMaterialGrade))
                return "Confirm received shape, size, and grade.";
            if (MaterialHeatLots.Count == 0)
                return "At least one heat lot is required.";
            foreach (var row in MaterialHeatLots)
            {
                if (string.IsNullOrWhiteSpace(row.HeatLotNumber))
                    return "Heat lot number is required for each row.";
                if (!double.TryParse((row.QuantityText ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var heatQty) || heatQty <= 0)
                    return "Heat lot quantity is required for each row.";
            }
            if (Math.Abs(MaterialHeatLotTotal() - receivedQty) > 0.0001)
                return "Heat lot total does not match received quantity.";
            var expectedUnit = PrimaryMaterialRequirement?.ExpectedUnit ?? "";
            if (!string.IsNullOrWhiteSpace(expectedUnit) && !string.Equals(expectedUnit.Trim(), ReceivedMaterialUnit.Trim(), StringComparison.OrdinalIgnoreCase))
                return "Received unit does not match expected unit.";
            return "";
        }

        private async Task LoadCurrentWorkOrderThumbnailAsync(RunBookWorkstationApiClient.OperationPacket? packet)
        {
            CurrentWorkOrderThumbnailImage = null;
            if (packet == null || CurrentSession == null)
                return;

            var thumbnailDocument = (packet.DrawingDocuments ?? new List<RunBookWorkstationApiClient.OperationPacketDocument>())
                .FirstOrDefault(document =>
                    string.Equals(document.Id, "drawing-thumbnail", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(document.Type, "Thumbnail", StringComparison.OrdinalIgnoreCase));
            if (thumbnailDocument == null || string.IsNullOrWhiteSpace(thumbnailDocument.ThumbnailRoute))
                return;

            try
            {
                var thumbnail = await _api.DownloadPacketDocumentThumbnailAsync(Settings, CurrentSession, thumbnailDocument.ThumbnailRoute, CancellationToken.None);
                CurrentWorkOrderThumbnailImage = BuildBitmapImage(thumbnail.Bytes);
            }
            catch (Exception ex)
            {
                DebugLogService.WriteException("LoadCurrentWorkOrderThumbnailAsync", ex);
            }
        }

        private static BitmapImage? BuildBitmapImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private string GetOperationFamily()
        {
            var label = $"{_operationPacket?.OperationTypeDisplay} {_operationPacket?.OperationType} {SelectedOperation?.Title} {SelectedOperation?.Department}".ToLowerInvariant();
            if (label.Contains("order") && label.Contains("material")) return "order-material";
            if (label.Contains("receive") && label.Contains("material")) return "receive-material";
            if (label.Contains("saw") || label.Contains("cut")) return "saw-cut";
            if (label.Contains("setup")) return "setup";
            if (label.Contains("swiss")) return "swiss";
            if (label.Contains("mill") || label.Contains("lathe") || label.Contains("cnc") || label.Contains("machin")) return "cnc";
            if (label.Contains("deburr")) return "deburr";
            if (label.Contains("final") && label.Contains("inspection")) return "inspection";
            if (label.Contains("inspection") || label.Contains("quality") || label.Contains("qa")) return "inspection";
            if (label.Contains("ship")) return "shipping";
            if (label.Contains("close") || label.Contains("complete")) return "complete-close";
            return "custom";
        }

        private string BuildDataEntryHeaderSummary()
        {
            var family = GetOperationFamily();
            return family switch
            {
                "order-material" => "Record material ordering progress from released material requirements.",
                "receive-material" => "Verify received material, certs, quantity, and trace evidence.",
                "saw-cut" => "Record cut quantity, scrap/remnant notes, and source material context.",
                "setup" => "Confirm setup readiness and capture handoff notes.",
                "cnc" => "Record good quantity, scrap, run notes, and in-process inspection references.",
                "swiss" => "Record good quantity, scrap, run notes, and in-process inspection references.",
                "deburr" => "Record completed quantity, rework/scrap, condition notes, and evidence.",
                "inspection" => "Review linked inspection tasks and capture inspection notes/evidence.",
                "shipping" => "Record packed or shipped quantity, shipment notes, and scan evidence.",
                "complete-close" => "Confirm final quantity, prior operation readiness, and closeout notes.",
                _ => "Capture quantity, notes, checklist items, and evidence for this released operation."
            };
        }

        private static string BuildRequiredInputsSubtitle(string family) => family switch
        {
            "receive-material" => "Expected material, received quantity, heat/cert context where available.",
            "order-material" => "Material spec, required quantity, vendor/PO context where available.",
            "setup" => "Setup confirmations, program/fixture context, and handoff notes.",
            "cnc" or "swiss" => "Good quantity, scrap quantity, machine/work center, and run notes.",
            "inspection" => "Linked inspection references and review notes.",
            "shipping" => "Packed/shipped quantity and shipping evidence.",
            _ => "Only released context and supported Workstation entry fields are shown."
        };

        private static string BuildConfirmationSubtitle(string family) => family switch
        {
            "setup" => "Setup checks released with the route appear here.",
            "inspection" => "Review/signoff checks released with the route appear here.",
            _ => "Checklist items released with the route appear here."
        };

        private string BuildEvidenceSubtitle()
        {
            if (IsEvidenceRequired())
                return "Evidence is required by the released route; add photo/scan attachments before completion.";
            return "Attach photos, certs, packing slips, or other operation evidence when useful.";
        }

        private string BuildDataEntryValidationMessage()
        {
            if (SelectedOperation == null)
                return "";
            if (IsReceiveMaterialOperation)
                return MaterialReceivingWarning;
            if (_operationPacket?.RequireNotes == true && string.IsNullOrWhiteSpace(OperationNoteText))
                return "A note is required by this released operation before submitting.";
            if (IsEvidenceRequired() && OperationAttachments.Count == 0)
                return "Evidence is marked required by this released operation.";
            return "";
        }

        private bool IsEvidenceRequired()
            => _operationPacket?.RequireAttachment == true || _operationPacket?.RequirePhoto == true || _operationPacket?.RequireVideo == true || (_operationPacket?.EvidenceRequired ?? 0) != 0;

        private static bool HasMaterialContext(string family)
            => family is "order-material" or "receive-material" or "saw-cut";

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static void AddIfPresent(WorkstationDataEntrySection section, string label, string? value, string state)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            section.Items.Add(BuildRequirementRow(label, value.Trim(), state));
        }

        private static WorkstationDataEntryRequirementRow BuildRequirementRow(string label, string value, string state)
        {
            var normalized = string.IsNullOrWhiteSpace(state) ? "Ready" : state.Trim();
            return new WorkstationDataEntryRequirementRow
            {
                Label = label,
                Value = value,
                State = normalized,
                StateBrush = normalized.Equals("Missing", StringComparison.OrdinalIgnoreCase) ? "#E8BC52" :
                    normalized.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? "#3CC875" :
                    normalized.Equals("Ready", StringComparison.OrdinalIgnoreCase) ? "#31C7FF" : "#B0BDD0",
                StateBorderBrush = normalized.Equals("Missing", StringComparison.OrdinalIgnoreCase) ? "#66E8BC52" :
                    normalized.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? "#553CC875" :
                    normalized.Equals("Ready", StringComparison.OrdinalIgnoreCase) ? "#5531C7FF" : "#3B526F",
                StateBackgroundBrush = normalized.Equals("Missing", StringComparison.OrdinalIgnoreCase) ? "#14E8BC52" :
                    normalized.Equals("Completed", StringComparison.OrdinalIgnoreCase) ? "#123CC875" :
                    normalized.Equals("Ready", StringComparison.OrdinalIgnoreCase) ? "#1031C7FF" : "#15162636"
            };
        }

        private void AddMaterialHeatLot()
            => AddMaterialHeatLot(new WorkstationMaterialHeatLotEntry { Unit = FirstNonBlank(ReceivedMaterialUnit, "Bars") });

        private void AddMaterialHeatLot(WorkstationMaterialHeatLotEntry row)
        {
            row.PropertyChanged += (_, _) => RaiseMaterialReceivingChanged();
            MaterialHeatLots.Add(row);
            RaiseMaterialReceivingChanged();
            if (RemoveMaterialHeatLotCommand is RelayCommand<WorkstationMaterialHeatLotEntry> remove) remove.RaiseCanExecuteChanged();
        }

        private void RemoveMaterialHeatLot(WorkstationMaterialHeatLotEntry? row)
        {
            if (row == null || MaterialHeatLots.Count <= 1)
                return;
            MaterialHeatLots.Remove(row);
            RaiseMaterialReceivingChanged();
            if (RemoveMaterialHeatLotCommand is RelayCommand<WorkstationMaterialHeatLotEntry> remove) remove.RaiseCanExecuteChanged();
        }

        private void ClearMaterialHeatLots()
        {
            MaterialHeatLots.Clear();
            RaiseMaterialReceivingChanged();
        }

        private void EnsureHeatLotMode()
        {
            if (!ReceiveMaterialMultipleHeatLots && MaterialHeatLots.Count > 1)
            {
                var first = MaterialHeatLots.FirstOrDefault();
                ClearMaterialHeatLots();
                AddMaterialHeatLot(first ?? new WorkstationMaterialHeatLotEntry { Unit = FirstNonBlank(ReceivedMaterialUnit, "Bars") });
            }
            if (MaterialHeatLots.Count == 0)
                AddMaterialHeatLot();
        }

        private void SelectInspectionTask(WorkstationInspectionTask? task)
        {
            if (task == null)
                return;

            SelectedInspectionTask = task;
            InspectionActualValue = task.ActualValue;
            InspectionResultNoteText = task.ResultNotes;
        }

        private void OpenOperationInspection()
        {
            if (!HasOperationInspection)
            {
                StatusText = "No inspection tasks are available for the selected operation.";
                return;
            }

            SelectedInspectionTask ??= InspectionTasks.FirstOrDefault();
            IsInspectionOverlayOpen = true;
        }

        private void CloseOperationInspection()
        {
            IsInspectionOverlayOpen = false;
        }

        private async Task OpenMobileCaptureAsync()
        {
            if (!EnsureServiceWritable())
                return;

            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null)
                return;

            await RunBusyAsync(async () =>
            {
                var response = await _api.CreateMobileCaptureSessionAsync(
                    Settings,
                    CurrentSession,
                    SelectedWorkOrder.WorkOrderId,
                    SelectedOperation.OperationId,
                    SelectedMobileCaptureType,
                    CancellationToken.None);

                var capture = response.Capture ?? throw new InvalidOperationException("Service did not return a mobile capture session.");
                MobileCaptureUrl = capture.CaptureUrl;
                MobileCaptureExpiresText = FormatCaptureExpiry(capture.ExpiresUtc);
                MobileCaptureQrImage = BuildQrImage(capture.CaptureUrl);
                IsMobileCaptureDialogOpen = true;
                StatusText = "Mobile capture link is ready. Scan the QR code with the phone.";
            });
        }

        private void CloseMobileCapture()
        {
            IsMobileCaptureDialogOpen = false;
            MobileCaptureUrl = "";
            MobileCaptureExpiresText = "";
            MobileCaptureQrImage = null;
        }

        private async Task RefreshOperationAttachmentsAsync()
        {
            if (CurrentSession == null || SelectedWorkOrder == null || SelectedOperation == null)
                return;

            try
            {
                var response = await _api.GetOperationAttachmentsAsync(Settings, CurrentSession, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId, CancellationToken.None);
                OperationAttachments.Clear();
                foreach (var attachment in response.Attachments?.Items ?? Enumerable.Empty<RunBookWorkstationApiClient.OperationAttachment>())
                    OperationAttachments.Add(attachment);
                OnPropertyChanged(nameof(OperationAttachmentSummary));
                RebuildDataEntrySections();
            }
            catch (Exception ex)
            {
                OperationAttachments.Clear();
                OnPropertyChanged(nameof(OperationAttachmentSummary));
                RebuildDataEntrySections();
                StatusText = ex.Message;
            }
        }

        private async Task SubmitInspectionResultAsync()
        {
            if (!EnsureServiceWritable())
                return;

            if (!_inspectionResultSubmissionAvailable)
            {
                StatusText = "Inspection result submission is not available on Workstation yet.";
                return;
            }

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
                OnPropertyChanged(nameof(HasOperationInspection));
                OnPropertyChanged(nameof(SelectedInspectionBalloonLabel));
                OnPropertyChanged(nameof(SelectedInspectionFeatureLabel));
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
                OfflineStatus = GetSavedPairingDegradedMessage() ?? GetAuthCacheHealth().Message;
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

        private async Task RefreshLocalTimeclockStateAsync()
        {
            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            var response = await _api.GetLocalTimeclockStateAsync(Settings, CurrentSession, CancellationToken.None);
            MarkLocalHostRequestSuccess();
            var snapshot = MapSnapshot(response.Snapshot);
            ApplySnapshot(snapshot, true);
            OfflineStatus = "RunBook.Service timeclock authority connected.";
            StatusText = $"Loaded Service timeclock state for {snapshot.EmployeeName}.";
        }

        private async Task<bool> RefreshAuthPackageAsyncCore(bool manual)
        {
            NormalizeLocalShopScope();
            SaveSettings();
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
                if (manual)
                {
                    await EnsureRegistrationAsync();
                }
                else
                {
                    var message = WorkstationRegistrationRequiredUserMessage;
                    _authCache ??= new WorkstationEmployeeAuthCache();
                    _authCache.LastRefreshError = message;
                    WorkstationStorageService.SaveAuthCache(_authCache);
                    UpdateAuthCacheStatus();
                    OfflineStatus = message;
                    return false;
                }
            }

            _lastAuthRefreshAttemptUtc = DateTime.UtcNow;
            _authCache ??= new WorkstationEmployeeAuthCache();
            _authCache.LastRefreshAttemptUtc = _lastAuthRefreshAttemptUtc.ToString("O", CultureInfo.InvariantCulture);

            try
            {
                var response = await _api.GetLocalAuthPackageAsync(Settings, CancellationToken.None);
                MarkLocalHostRequestSuccess();
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
                MarkRegistrationTrusted("auth_package_validated");
                OfflineStatus = "RunBook.Service local authority connected.";
                if (manual)
                    StatusText = $"Employee auth refreshed from the local workstation authority for {package.Employees.Count} employees.";
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
                {
                    MarkLocalHostRequestFailure(ex.Message);
                    MarkRegistrationValidationDegraded("service_unavailable", "Device trust could not be validated because RunBook.Service is unavailable. Continuing with the last saved workstation pairing.");
                }
                var authHealth = GetAuthCacheHealth();
                UpdateAuthCacheStatus(authHealth);
                OfflineStatus = IsConnectivityFailure(ex)
                    ? "Device trust could not be validated because RunBook.Service is unavailable. Continuing with the last saved workstation pairing."
                    : GetServiceFailureStatus("/api/workstation-local/auth-package", ex.Message, "Employee auth cache is not available yet.");
                if (manual)
                    StatusText = IsConnectivityFailure(ex) ? OfflineStatus : (authHealth.AllowsOfflineLogin ? authHealth.Message : GetUserFacingErrorMessage(ex));
                return false;
            }
        }

        private async Task SyncPendingQueueAsync(bool manual)
        {
            if (CurrentSession == null || !HasTimeClockAccess)
                return;

            var pendingItems = PendingSyncItems
                .Where(item =>
                    string.Equals(item.SyncStatus, "pending", StringComparison.OrdinalIgnoreCase) ||
                    (manual && string.Equals(item.SyncStatus, "failed", StringComparison.OrdinalIgnoreCase)))
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

            foreach (var item in pendingItems.Where(item => string.Equals(item.SyncStatus, "failed", StringComparison.OrdinalIgnoreCase)))
                item.SyncStatus = "pending";

            RunBookWorkstationApiClient.DesktopSyncResponse? localSnapshotResponse = null;
            var localError = "";

            try
            {
                var response = await _api.SyncLocalTimeclockAsync(Settings, CurrentSession, pendingItems, CancellationToken.None);
                ApplySyncResults(response.Results);
                localSnapshotResponse = response;
                MarkLocalHostRequestSuccess();
            }
            catch (Exception ex)
            {
                localError = ex.Message;
                foreach (var item in pendingItems)
                {
                    item.RetryCount++;
                    item.LastError = ex.Message;
                }

                if (IsConnectivityFailure(ex))
                    MarkLocalHostRequestFailure(ex.Message);
            }

            SaveQueue();
            if (localSnapshotResponse?.Snapshot != null)
            {
                var snapshot = MapSnapshot(localSnapshotResponse.Snapshot);
                ApplySnapshot(snapshot, true);
            }
            else
            {
                RebuildLocalProjection();
            }

            RemoveSyncedQueueItems();

            if (localError.Length == 0)
            {
                OfflineStatus = "RunBook.Service connected and workstation sync complete.";
                TimeClockStatus = HasPendingSyncItems ? "Some items still need attention." : "RunBook.Service workstation sync is fully caught up.";
                StatusText = HasPendingSyncItems
                    ? "Some workstation items were rejected and need review."
                    : "Workstation sync items sent to RunBook.Service.";
                return;
            }

            OfflineStatus = IsConnectivityFailureMessage(localError)
                ? "RunBook.Service unavailable. Workstation sync items will sync later."
                : GetServiceFailureStatus("/api/workstation-local/timeclock/sync", localError, "Service endpoint failed: /api/workstation-local/timeclock/sync");
            if (manual)
                StatusText = localError;
        }

        private void ApplySyncResults(IEnumerable<RunBookWorkstationApiClient.DesktopSyncItemResult> results)
        {
            foreach (var result in results)
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
            TimeClockStatus = $"{ToEventLabel(eventType)} pending local sync.";
        }

        private async Task SubmitTimeOffAsync()
        {
            if (!EnsureServiceWritable())
                return;

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
            OnPropertyChanged(nameof(LastSyncDisplayText));
            RefreshRosterEmployeeStatuses();
            RebuildLocalProjection();
        }

        private void RebuildLocalProjection()
        {
            var basePunches = (_timeclockSnapshot?.RecentPunches ?? new List<WorkstationPunchRecord>())
                .Where(punch => !string.Equals(punch.Source, "workstation-pending", StringComparison.OrdinalIgnoreCase))
                .Select(ClonePunch)
                .ToList();

            var pendingPunches = PendingSyncItems.Where(entry =>
                string.Equals(entry.ItemType, "punch", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.SyncStatus, "pending", StringComparison.OrdinalIgnoreCase))
                .Select(item => new WorkstationPunchRecord
                {
                    Id = item.ClientEventId,
                    EventType = item.ActionType.ToUpperInvariant(),
                    ClientTs = item.EffectiveUtc,
                    ServerTs = "",
                    Source = "workstation-pending",
                    Note = item.Note,
                    IsPendingSync = true,
                    SyncState = item.SyncStatus
                })
                .ToList();

            var displayPunches = basePunches
                .Concat(pendingPunches)
                .ToList();

            var orderedPunches = displayPunches
                .OrderByDescending(entry => ParseUtc(entry.ClientTs))
                .ThenByDescending(entry => entry.Id, StringComparer.Ordinal)
                .Take(20)
                .ToArray();
            WorkstationStorageService.SaveTimeClockCache(orderedPunches);
            RecentPunches.Clear();
            foreach (var punch in orderedPunches)
                RecentPunches.Add(punch);

            var requests = (_timeclockSnapshot?.RecentTimeOffRequests ?? new List<WorkstationTimeOffRequestRecord>())
                .Where(request => !request.IsPendingSync)
                .Select(CloneRequest)
                .ToList();
            foreach (var item in PendingSyncItems.Where(entry =>
                string.Equals(entry.ItemType, "time_off_request", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.SyncStatus, "pending", StringComparison.OrdinalIgnoreCase)))
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
            UpdateHeaderClock();
            OnPropertyChanged(nameof(IdleLogoutBadgeText));
            MaybeRefreshPassiveConnectionStatuses();

            if (CurrentSession == null)
            {
                SessionCountdown = "Signed out";
                return;
            }

            if (!DateTime.TryParse(CurrentSession.ExpiresAtUtc, null, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var expiresUtc))
            {
                SessionCountdown = "Session timing unavailable";
                return;
            }

            if (IsWorkOrdersSelected)
            {
                SessionCountdown = "";
                return;
            }

            var idleRemaining = IdleLogoutTimeout - (DateTime.UtcNow - _lastInteractionUtc);
            if (idleRemaining <= TimeSpan.Zero)
            {
                Logout();
                StatusText = "Signed out after 5 minutes with no mouse or typing activity.";
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
        }

        private void UpdateHeaderClock()
        {
            var now = DateTime.Now;
            var dateText = now.ToString("dddd, MMMM d, yyyy");
            var timeText = now.ToString("h:mm tt");

            if (!string.Equals(_headerDateText, dateText, StringComparison.Ordinal))
            {
                _headerDateText = dateText;
                OnPropertyChanged(nameof(HeaderDateText));
            }

            if (!string.Equals(_headerTimeText, timeText, StringComparison.Ordinal))
            {
                _headerTimeText = timeText;
                OnPropertyChanged(nameof(HeaderTimeText));
            }
        }

        private int GetIdleSecondsRemaining()
        {
            if (CurrentSession == null)
                return (int)IdleLogoutTimeout.TotalSeconds;

            var remaining = IdleLogoutTimeout - (DateTime.UtcNow - _lastInteractionUtc);
            return Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        }

        private static string FormatIdleLogoutRemaining(int seconds)
        {
            if (seconds >= 60)
            {
                var minutes = seconds / 60;
                var remainder = seconds % 60;
                return $"{minutes}:{remainder:00}";
            }

            return $"{seconds}s";
        }

        private void RefreshTimeClockPresentation()
        {
            var signature = BuildTimeClockPresentationSignature();
            if (string.Equals(_lastTimeClockPresentationSignature, signature, StringComparison.Ordinal))
                return;

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
            OnPropertyChanged(nameof(TimeClockHeroSinceLine));
            OnPropertyChanged(nameof(TimeClockHeroClockInTime));
            OnPropertyChanged(nameof(TimeClockHeroClockInDate));
            OnPropertyChanged(nameof(TimeClockHeroElapsedValue));
            OnPropertyChanged(nameof(TimeClockHeroElapsedCaption));
            OnPropertyChanged(nameof(TimeClockHeroShiftLine));
            OnPropertyChanged(nameof(TimeClockWeeklyDateRange));
            OnPropertyChanged(nameof(TimeClockPendingApprovalCount));
            OnPropertyChanged(nameof(TimeClockPendingApprovalText));
            OnPropertyChanged(nameof(TimeClockFooterStatusLine));
            OnPropertyChanged(nameof(TimeClockSyncStatusLabel));
            OnPropertyChanged(nameof(TimeClockSyncDetailText));
            OnPropertyChanged(nameof(TimeClockTimelineClockInText));
            OnPropertyChanged(nameof(TimeClockTimelineWorkingText));
            OnPropertyChanged(nameof(TimeClockTimelineLunchText));
            OnPropertyChanged(nameof(TimeClockTimelineClockOutText));
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
            _lastTimeClockPresentationSignature = signature;
        }

        private string BuildTimeClockPresentationSignature()
        {
            return string.Join("|",
                _timeclockSnapshot?.CurrentStatus ?? "",
                _timeclockSnapshot?.StatusSinceUtc ?? "",
                _timeclockSnapshot?.LastPunchUtc ?? "",
                _timeclockSnapshot?.LastSyncUtc ?? "",
                _timeclockSnapshot?.LastSyncMessage ?? "",
                _timeclockSnapshot?.SupportsLunch == true ? "1" : "0",
                RecentPunches.Count.ToString(CultureInfo.InvariantCulture),
                RecentPunches.LastOrDefault()?.Id ?? "",
                RecentPunches.LastOrDefault()?.SyncState ?? "",
                RecentTimeOffRequests.Count.ToString(CultureInfo.InvariantCulture),
                RecentTimeOffRequests.LastOrDefault()?.Id ?? "",
                RecentTimeOffRequests.LastOrDefault()?.SyncState ?? "",
                PendingSyncItems.Count.ToString(CultureInfo.InvariantCulture),
                PendingSyncItems.Count(item => string.Equals(item.SyncStatus, "pending", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture),
                PendingSyncItems.Count(item => string.Equals(item.SyncStatus, "failed", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture),
                CurrentShiftStatus ?? "",
                CurrentShiftDetail ?? "");
        }

        private void MaybeRefreshPassiveConnectionStatuses()
        {
            var now = DateTime.UtcNow;
            if (now - _lastPassiveConnectionRefreshUtc < TimeSpan.FromSeconds(5))
                return;

            _lastPassiveConnectionRefreshUtc = now;
            RefreshConnectionStatuses(false);
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
                Label = "Worked",
                Value = FormatDuration(summary.WorkTotal),
                DetailText = "of 8h 00m",
                IconText = "W",
                AccentBrush = "#58E58B",
                ProgressValue = GetProgressPercent(summary.WorkTotal, TimeSpan.FromHours(8))
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Break Total",
                Value = FormatDuration(summary.BreakTotal),
                DetailText = "of 0h 30m",
                IconText = "B",
                AccentBrush = "#F5A623",
                ProgressValue = GetProgressPercent(summary.BreakTotal, TimeSpan.FromMinutes(30))
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Lunch Total",
                Value = SupportsLunch ? FormatDuration(summary.LunchTotal) : "--",
                DetailText = SupportsLunch ? "of 1h 00m" : "not enabled",
                IconText = "L",
                AccentBrush = "#49A7FF",
                ProgressValue = SupportsLunch ? GetProgressPercent(summary.LunchTotal, TimeSpan.FromHours(1)) : 0
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Overtime",
                Value = FormatDuration(GetOvertime(summary.WorkTotal, TimeSpan.FromHours(8))),
                DetailText = "after 8h 00m",
                IconText = "OT",
                AccentBrush = "#A46BFF",
                ProgressValue = GetProgressPercent(GetOvertime(summary.WorkTotal, TimeSpan.FromHours(8)), TimeSpan.FromHours(2))
            };
        }

        private IEnumerable<WorkstationTimeClockSummaryRow> BuildWeeklySummaryRows()
        {
            var now = DateTime.Now;
            var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
            var summary = SummarizePunchRange(startOfWeek, startOfWeek.AddDays(7));

            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Total Worked",
                Value = FormatDuration(summary.WorkTotal),
                DetailText = "Target: 40h 00m",
                IconText = "W",
                AccentBrush = "#49A7FF",
                ProgressValue = GetProgressPercent(summary.WorkTotal, TimeSpan.FromHours(40))
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Days Worked",
                Value = summary.ClockInCount.ToString(CultureInfo.InvariantCulture),
                DetailText = "of 5",
                IconText = "D",
                AccentBrush = "#49A7FF",
                ProgressValue = Math.Min(100, summary.ClockInCount / 5.0 * 100)
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Total Breaks",
                Value = FormatDuration(summary.BreakTotal),
                DetailText = "of 2h 30m",
                IconText = "B",
                AccentBrush = "#F5A623",
                ProgressValue = GetProgressPercent(summary.BreakTotal, TimeSpan.FromMinutes(150))
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Total Lunches",
                Value = SupportsLunch ? FormatDuration(summary.LunchTotal) : "--",
                DetailText = SupportsLunch ? "of 5h 00m" : "not enabled",
                IconText = "L",
                AccentBrush = "#49A7FF",
                ProgressValue = SupportsLunch ? GetProgressPercent(summary.LunchTotal, TimeSpan.FromHours(5)) : 0
            };
        }

        private IEnumerable<WorkstationTimeClockSummaryRow> BuildCurrentStatusSummaryRows()
        {
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Status",
                Value = CurrentShiftStatus,
                DetailText = TimeClockStateKey == "out" ? "Not recording time" : "Recording service time",
                IconText = "S",
                AccentBrush = TimeClockHeroAccentBrush,
                ProgressValue = TimeClockStateKey == "out" ? 0 : 100
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = TimeClockStateKey == "out" ? "Last Action" : "Since",
                Value = BuildStatusTimeValue(),
                DetailText = TimeClockStateKey == "out" ? "Last service punch" : "Current state started",
                IconText = "T",
                AccentBrush = TimeClockHeroAccentBrush,
                ProgressValue = 100
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Elapsed",
                Value = BuildElapsedSummaryValue(),
                DetailText = TimeClockStateKey == "out" ? "No active timer" : "Current state duration",
                IconText = "E",
                AccentBrush = TimeClockHeroAccentBrush,
                ProgressValue = TimeClockStateKey == "out" ? 0 : 100
            };
            yield return new WorkstationTimeClockSummaryRow
            {
                Label = "Sync Status",
                Value = BuildSyncSummaryValue(),
                DetailText = HasPendingSyncItems ? "Pending local items" : "Last sync clean",
                IconText = "C",
                AccentBrush = HasPendingSyncItems ? "#F5A623" : "#58E58B",
                ProgressValue = HasPendingSyncItems ? 55 : 100
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

        private static double GetProgressPercent(TimeSpan value, TimeSpan target)
        {
            if (target <= TimeSpan.Zero)
                return 0;

            return Math.Max(0, Math.Min(100, value.TotalSeconds / target.TotalSeconds * 100));
        }

        private static TimeSpan GetOvertime(TimeSpan worked, TimeSpan target)
        {
            return worked > target ? worked - target : TimeSpan.Zero;
        }

        private string BuildHeroSinceLine()
        {
            var local = ToLocalTime(_timeclockSnapshot?.StatusSinceUtc ?? "");
            if (!local.HasValue)
                return TimeClockStateKey == "out" ? "Ready when you are." : "Current state start time unavailable.";

            return TimeClockStateKey switch
            {
                "working" => $"Working since {local.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)}",
                "break" => $"On break since {local.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)}",
                "lunch" => $"At lunch since {local.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)}",
                _ => $"Last action {local.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)}"
            };
        }

        private string BuildHeroClockInTime()
        {
            var today = DateTime.Now.Date;
            var summary = SummarizePunchRange(today, today.AddDays(1));
            return summary.FirstClockIn.HasValue
                ? summary.FirstClockIn.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)
                : "--";
        }

        private string BuildHeroClockInDate()
        {
            var today = DateTime.Now.Date;
            var summary = SummarizePunchRange(today, today.AddDays(1));
            return summary.FirstClockIn.HasValue
                ? summary.FirstClockIn.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
                : DateTime.Now.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        }

        private string BuildHeroElapsedValue()
        {
            var elapsed = GetElapsedSince(_timeclockSnapshot?.StatusSinceUtc);
            return elapsed.HasValue && TimeClockStateKey != "out"
                ? FormatDuration(elapsed.Value)
                : "--";
        }

        private string BuildHeroShiftLine()
        {
            var detail = (CurrentShiftDetail ?? "").Trim();
            if (detail.Length == 0 ||
                detail.StartsWith("SHIFT:", StringComparison.OrdinalIgnoreCase) ||
                detail.StartsWith("Since ", StringComparison.OrdinalIgnoreCase))
            {
                return "Current shift";
            }

            return detail;
        }

        private static string BuildWeeklyDateRange()
        {
            var now = DateTime.Now;
            var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(6);
            return $"{startOfWeek.ToString("MMM d", CultureInfo.InvariantCulture)} - {endOfWeek.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
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
                    IconText = GetActivityIconText((punch.EventType ?? "").Trim()),
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
            return $"{totalHours}h {value.Minutes:00}m";
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
                return "#FF5B6E";
            if (string.Equals(syncState, "pending", StringComparison.OrdinalIgnoreCase))
                return "#F5A623";

            return (eventType ?? "").Trim().ToUpperInvariant() switch
            {
                "BREAK_START" or "BREAK_END" => "#F5A623",
                "LUNCH_START" or "LUNCH_END" => "#49A7FF",
                "CLOCK_IN" => "#58E58B",
                "CLOCK_OUT" => "#FF5B6E",
                _ => "#35C8FF"
            };
        }

        private static string GetActivityIconText(string eventType)
        {
            return (eventType ?? "").Trim().ToUpperInvariant() switch
            {
                "BREAK_START" or "BREAK_END" => "B",
                "LUNCH_START" or "LUNCH_END" => "L",
                "CLOCK_IN" => "IN",
                "CLOCK_OUT" => "OUT",
                _ => "T"
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
                StatusText = string.IsNullOrWhiteSpace(_authCache?.LastRefreshError)
                    ? "Employee auth cache is not available yet."
                    : _authCache.LastRefreshError;
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

        private void BindToServiceAuthorityOnBoot()
        {
            try
            {
                var health = _api.GetLocalHealthAsync(Settings, CancellationToken.None).GetAwaiter().GetResult();
                if (!ApplyServiceAuthorityHealth(health, "boot"))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                BlockServiceAuthority("boot", "unavailable", ex.Message, "", "");
                DebugLogService.Write($"Workstation boot could not bind shop from Service health | {ex.Message}");
            }
        }

        private bool ApplyServiceAuthorityHealth(RunBookWorkstationApiClient.LocalHealthResponse health, string source)
        {
            if (health == null)
                return false;

            var expectedFolder = string.IsNullOrWhiteSpace(health.SafeCompanyName)
                ? (health.CompanyDataFolderName ?? "").Trim()
                : $"{health.SafeCompanyName.Trim().ToUpperInvariant()}_DATA";
            var actualFolder = string.IsNullOrWhiteSpace(health.ActiveCompanyDataRoot)
                ? ""
                : (Path.GetFileName(health.ActiveCompanyDataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "").Trim();
            var hasRootMismatch = expectedFolder.Length > 0 &&
                                  actualFolder.Length > 0 &&
                                  !string.Equals(expectedFolder, actualFolder, StringComparison.OrdinalIgnoreCase);

            if (!health.Ok ||
                string.Equals(health.Status, "company_data_missing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(health.Status, "no_company_context", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(health.Status, "invalid_context", StringComparison.OrdinalIgnoreCase) ||
                hasRootMismatch ||
                string.IsNullOrWhiteSpace(health.ShopId) ||
                string.IsNullOrWhiteSpace(health.CompanyName))
            {
                var blockedMessage = string.Equals(health.Status, "company_data_missing", StringComparison.OrdinalIgnoreCase)
                    ? "Active company data folder is missing."
                    : hasRootMismatch
                        ? "Service company context/root mismatch was detected."
                        : string.IsNullOrWhiteSpace(health.ErrorMessage)
                            ? "Service has no valid company context. Select a shop in Desktop and restart Service."
                            : health.ErrorMessage;
                BlockServiceAuthority(source, hasRootMismatch ? "context_root_mismatch" : health.Status, blockedMessage, health.ActiveCompanyDataRoot, health.MissingFolderName);
                return false;
            }

            var previousShopId = (Settings.ShopId ?? "").Trim();
            if (previousShopId.Length > 0 &&
                !string.Equals(previousShopId, health.ShopId, StringComparison.OrdinalIgnoreCase))
            {
                WorkstationStorageService.ClearShopScopedCache(previousShopId);
                DebugLogService.Write($"Workstation discarded stale cached shop scope | source={source} | old_shop_id={previousShopId} | new_shop_id={health.ShopId}");
            }

            Settings.ShopId = health.ShopId;
            Settings.ShopName = string.IsNullOrWhiteSpace(health.CompanyName) ? Settings.ShopName : health.CompanyName;
            SettingsShopId = Settings.ShopId;
            SettingsShopName = Settings.ShopName;
            WorkstationStorageService.SaveSettings(Settings);
            WorkstationStorageService.ClearAllOtherShopScopedCaches(Settings.ShopId, health.SafeCompanyName);
            _serviceWriteBlocked = false;
            OfflineStatus = "RunBook.Service local authority connected.";

            DebugLogService.Write(
                $"StartupCompanyContext | app=RunBook.Workstation | source={source} | selected_shop_id={health.ShopId} | company_name={health.CompanyName} | safe_company_name={health.SafeCompanyName} | company_data_folder_name={health.CompanyDataFolderName} | resolved_data_root={health.ActiveCompanyDataRoot} | context_file_path=service-health | context_loaded_timestamp={health.ContextLoadedUtc} | context_source={health.ContextSource} | status={health.Status}");
            return true;
        }

        private void BlockServiceAuthority(string source, string status, string message, string root, string missingFolder)
        {
            _serviceWriteBlocked = true;
            var userMessage = string.Equals(status, "unavailable", StringComparison.OrdinalIgnoreCase)
                ? "Device trust could not be validated because RunBook.Service is unavailable. Continuing with the last saved workstation pairing."
                : string.Equals(status, "company_data_missing", StringComparison.OrdinalIgnoreCase)
                    ? "Device trust could not be validated because RunBook.Service cannot find the active company data folder. Continuing with the last saved workstation pairing."
                    : string.IsNullOrWhiteSpace(message)
                        ? "Device trust could not be validated because RunBook.Service has no valid company context. Continuing with the last saved workstation pairing."
                        : $"Device trust could not be validated right now. Continuing with the last saved workstation pairing. {message}";
            OfflineStatus = userMessage;
            StatusText = userMessage;
            ShowRunBookIssueAlert(
                $"service-authority-{status}",
                "RunBook.Service Needs Attention",
                userMessage,
                Registration == null
                    ? "Start RunBook.Service and confirm the correct shop/company is selected. If this workstation has never been paired, pair it after Service is healthy."
                    : "Start RunBook.Service and confirm it can see the active company data folder. This workstation will keep using the saved pairing while Service is temporarily unavailable.");

            CurrentSession = null;
            _timeclockSnapshot = null;
            MarkRegistrationValidationDegraded(status, userMessage);
            WorkstationStorageService.SaveSession(null);
            WorkstationStorageService.SaveTimeclockState(null);
            if (CurrentSession != null)
                Logout();
            else
                RaiseCommandStates();

            DebugLogService.Write($"Workstation service authority blocked | source={source} | status={status} | paired={Registration != null} | shop_id={Registration?.ShopId ?? Settings.ShopId} | workstation_id={Registration?.WorkstationId ?? Settings.WorkstationId} | trust_status={Registration?.TrustStatus ?? "missing"} | validation_failure={status} | folder={missingFolder} | root={root}");
        }

        private bool EnsureServiceWritable()
        {
            if (!_serviceWriteBlocked)
                return true;

            OfflineStatus = "RunBook Service cannot find the active company data folder.";
            StatusText = "RunBook Service cannot find the active company data folder.";
            ShowRunBookIssueAlert(
                "service-write-blocked",
                "RunBook.Service Needs Attention",
                "RunBook.Service cannot find the active company data folder, so Workstation cannot write or sync safely.",
                "Start RunBook.Service and confirm the active company data folder exists for the selected shop.");
            return false;
        }

        private bool HasUsableSavedRegistration()
        {
            return Registration != null &&
                   Registration.IsActive &&
                   string.Equals(Registration.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(Registration.DeviceToken) &&
                   !string.IsNullOrWhiteSpace(Registration.ShopId) &&
                   !string.IsNullOrWhiteSpace(Settings.ShopId) &&
                   string.Equals(Registration.ShopId, Settings.ShopId, StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(Registration.WorkstationId) &&
                   !string.IsNullOrWhiteSpace(Settings.WorkstationId) &&
                   string.Equals(Registration.WorkstationId, Settings.WorkstationId, StringComparison.OrdinalIgnoreCase);
        }

        private void MarkRegistrationTrusted(string reason)
        {
            if (Registration == null)
                return;

            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            Registration.TrustStatus = "trusted";
            Registration.LastValidatedUtc = now;
            Registration.LastSyncUtc = now;
            Registration.LastValidationError = "";
            Registration.PairingRequiredReason = "";
            Registration.RefreshAvailable = false;
            WorkstationStorageService.SaveRegistration(Registration);
            DebugLogService.Write($"Workstation trust diagnostics | paired=true | shop_id={Registration.ShopId} | workstation_id={Registration.WorkstationId} | trust_status={Registration.TrustStatus} | last_validated_utc={Registration.LastValidatedUtc} | pairing_required_reason= | validation_failure= | reason={reason}");
            RefreshConnectionStatuses();
        }

        private void MarkRegistrationValidationDegraded(string reason, string message)
        {
            if (Registration == null)
            {
                DebugLogService.Write($"Workstation trust diagnostics | paired=false | shop_id={Settings.ShopId} | workstation_id={Settings.WorkstationId} | trust_status=missing | last_validated_utc= | pairing_required_reason=no_local_trust | validation_failure={reason}");
                return;
            }

            Registration.TrustStatus = "degraded";
            Registration.LastValidationError = string.IsNullOrWhiteSpace(message)
                ? "Device trust could not be validated right now. Continuing with the last saved workstation pairing."
                : message.Trim();
            Registration.PairingRequiredReason = "";
            Registration.RefreshAvailable = false;
            WorkstationStorageService.SaveRegistration(Registration);
            DebugLogService.Write($"Workstation trust diagnostics | paired=true | shop_id={Registration.ShopId} | workstation_id={Registration.WorkstationId} | trust_status={Registration.TrustStatus} | last_validated_utc={Registration.LastValidatedUtc} | pairing_required_reason= | validation_failure={reason}");
            RefreshConnectionStatuses();
        }

        private void HandleTrustFailure(string message)
        {
            MarkLocalHostRequestSuccess();
            var code = GetErrorCode(message);
            if (Registration != null)
            {
                Registration.TrustStatus = "pairing_required";
                Registration.LastValidationError = GetPairingRequiredMessageForTrustFailure(code);
                Registration.PairingRequiredReason = code;
                WorkstationStorageService.SaveRegistration(Registration);
            }
            DebugLogService.Write($"Workstation registration cleared | paired=false | shop_id={Registration?.ShopId ?? Settings.ShopId} | workstation_id={Registration?.WorkstationId ?? Settings.WorkstationId} | trust_status=pairing_required | pairing_required_reason={code} | validation_failure={code}");
            Registration = null;
            WorkstationStorageService.SaveRegistration(null);
            _authCache = null;
            WorkstationStorageService.SaveAuthCache(null);
            RosterEmployees.Clear();
            if (CurrentSession != null)
                Logout();
            OfflineStatus = "Workstation enrollment is no longer valid for this Service-linked local host.";
            UpdateAuthCacheStatus(new WorkstationAuthCacheHealth
            {
                State = WorkstationAuthCacheState.Expired,
                Message = "Local workstation trust was revoked or replaced. Re-enroll with a new pairing code."
            });
            StatusText = message;
            ShowRunBookIssueAlert(
                $"workstation-trust-{code}",
                "Workstation Pairing Required",
                GetPairingRequiredMessageForTrustFailure(code),
                "Ask a supervisor or administrator for a new pairing code, then pair this workstation again from Settings.");
        }

        private static string GetErrorCode(string message)
        {
            var trimmed = (message ?? "").Trim();
            var separator = trimmed.IndexOf(':');
            return separator > 0 ? trimmed.Substring(0, separator).Trim() : trimmed;
        }

        private static string GetPairingRequiredMessageForTrustFailure(string code)
        {
            return code switch
            {
                "WORKSTATION_REVOKED" => "This workstation was unpaired or disabled by an administrator. Enter a new pairing code to reconnect.",
                "WORKSTATION_TOKEN_INVALID" => "This workstation's saved pairing could not be verified. Please pair again.",
                "WORKSTATION_NOT_ENROLLED" => "This workstation has not been paired yet. Enter a pairing code to connect it to your shop.",
                "WORKSTATION_IDENTITY_MISMATCH" => "This workstation's saved pairing no longer matches this machine. Please pair again.",
                _ => "This workstation's saved pairing could not be verified. Please pair again.",
            };
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

        private void NormalizeDesktopBaseUrl()
        {
            if (WorkstationStorageService.ShouldForceLocalService())
            {
                Settings.DesktopBaseUrl = "http://localhost:30112";
                SettingsDesktopBaseUrl = Settings.DesktopBaseUrl;
                return;
            }

            var current = (Settings.DesktopBaseUrl ?? "").Trim();
            if (string.IsNullOrWhiteSpace(current))
                return;

            if (!Uri.TryCreate(current, UriKind.Absolute, out var uri))
                return;

            if (!IsLoopbackHost(uri.Host))
                return;

            var workstationHost = (Settings.WorkstationName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(workstationHost) || IsLoopbackHost(workstationHost))
                return;

            var builder = new UriBuilder(uri)
            {
                Host = workstationHost
            };

            Settings.DesktopBaseUrl = builder.Uri.ToString().TrimEnd('/');
            SettingsDesktopBaseUrl = Settings.DesktopBaseUrl;
        }

        private static bool IsLoopbackHost(string host)
        {
            var normalized = (host ?? "").Trim();
            return normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("::1", StringComparison.OrdinalIgnoreCase);
        }

        private async Task InitializeFromControlSessionAsync()
        {
            ControlSessionStatus = ControlSessionService.GetStatusLabel();
            OnPropertyChanged(nameof(HasSupervisorSession));
            OnPropertyChanged(nameof(ShowSupervisorSignInFields));
            OnPropertyChanged(nameof(ShowSupervisorEnrollmentActions));
            OnPropertyChanged(nameof(SupervisorDialogTitle));
            OnPropertyChanged(nameof(SupervisorDialogSubtitle));
            RefreshConnectionStatuses();
            _isControlConfirmedAvailable = false;
            await RunBusyAsync(async () =>
            {
                await RefreshAuthPackageAsyncCore(false);
                if (ControlSessionService.HasSession())
                    await LoadRosterCoreAsync();
            });
        }

        private async Task SyncCurrentShopAsync()
        {
            if (!ControlSessionService.HasSession() || string.IsNullOrWhiteSpace(Settings.ControlBaseUrl))
                return;

            var currentShop = await _api.GetCurrentShopAsync(Settings.ControlBaseUrl, CancellationToken.None);
            var serviceShopId = (Settings.ShopId ?? "").Trim();
            if (!currentShop.Found || string.IsNullOrWhiteSpace(currentShop.ShopId))
            {
                DebugLogService.Write("Workstation Control shop sync ignored | Control reported no active shop. Service remains the local shop authority.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(serviceShopId) &&
                !string.Equals(currentShop.ShopId, serviceShopId, StringComparison.OrdinalIgnoreCase))
            {
                DebugLogService.Write($"Workstation Control shop sync ignored | service_shop_id={serviceShopId} | control_shop_id={currentShop.ShopId}");
                return;
            }

            DebugLogService.Write($"Workstation Control shop reported informational match | shop_id={currentShop.ShopId} | shop_name={currentShop.ShopName}");
        }

        private async Task TrySyncCurrentShopIfConfirmedAsync()
        {
            if (!_isControlConfirmedAvailable || !ControlSessionService.HasSession() || string.IsNullOrWhiteSpace(Settings.ControlBaseUrl))
                return;

            try
            {
                await SyncCurrentShopAsync();
            }
            catch (Exception ex)
            {
                _isControlConfirmedAvailable = false;
                DebugLogService.Write($"Optional Control sync skipped | {ex.Message}");
            }
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
            OnPropertyChanged(nameof(RosterDiagnosticLine));
            OnPropertyChanged(nameof(RosterAvatarStatusLine));
            OnPropertyChanged(nameof(RecentRosterEmployees));
            OnPropertyChanged(nameof(HasMoreRosterEmployees));
            OnPropertyChanged(nameof(HasRosterEmployees));
            OnPropertyChanged(nameof(ShowRecentEmployeeEmptyState));
            OnPropertyChanged(nameof(RecentEmployeeEmptyText));
            OnPropertyChanged(nameof(RecentEmployeeEmptyDetailText));
            OnPropertyChanged(nameof(QuickSearchHelperText));
            OnPropertyChanged(nameof(EmployeeBrowserEmployees));
            RaiseCommandStates();
            if (RosterEmployees.Count == 0)
            {
                StatusText = _authCache?.Package == null
                    ? (string.IsNullOrWhiteSpace(_authCache?.LastRefreshError)
                        ? "Employee auth cache is not available yet."
                        : _authCache.LastRefreshError)
                    : "RunBook Service auth is loaded, but no workstation-ready employees are available to show.";
            }
            return Task.CompletedTask;
        }

        private bool CanLoadRoster()
        {
            if (string.IsNullOrWhiteSpace(Settings.ShopId))
            {
                return false;
            }

            if (_authCache?.Package == null)
            {
                return false;
            }

            if (!string.Equals(_authCache.Package.ShopId, Settings.ShopId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private void LoadRosterFromAuthCache()
        {
            RosterEmployees.Clear();
            if (!CanLoadRoster())
            {
                OnPropertyChanged(nameof(RosterDiagnosticLine));
                OnPropertyChanged(nameof(RosterAvatarStatusLine));
                return;
            }

            var employees = _authCache!.Package.Employees
                .Where(employee => employee.IsActive)
                .Where(employee => employee.WorkstationAccessEnabled)
                .Take(20)
                .Select(MapRosterEmployee)
                .ToList();

            foreach (var employee in employees)
            {
                employee.AvatarDisplayUrl = ResolveRosterAvatarDisplayUrl(employee.AvatarDisplayUrl, employee.EmployeeAvatarRef);
            }

            foreach (var employee in employees)
                RosterEmployees.Add(employee);

            RefreshRosterEmployeeStatuses();

            foreach (var employee in employees.Take(3))
            {
                var imageSourceResolves = DoesAvatarImageSourceResolve(employee.AvatarDisplayUrl);
                DebugLogService.Write(
                    $"RosterAvatarTrace | DisplayName={employee.DisplayName} | HasAvatarDisplayUrl={employee.HasAvatarDisplayUrl} | AvatarDisplayUrl={employee.AvatarDisplayUrl} | ImageSourceResolves={imageSourceResolves} | FallbackInitialsActive={!employee.HasAvatarDisplayUrl}");
            }

            OnPropertyChanged(nameof(RosterDiagnosticLine));
            OnPropertyChanged(nameof(RosterAvatarStatusLine));
        }

        private void RefreshRosterEmployeeStatuses()
        {
            var activeEmployeeId = CurrentSession?.Employee?.EmployeeId ?? "";
            var activeRemoteEmployeeId = CurrentSession?.Employee?.RemoteEmployeeId ?? "";
            var activeEmployeeCode = CurrentSession?.Employee?.EmployeeCode ?? "";

            foreach (var employee in RosterEmployees)
            {
                var isCurrent = (!string.IsNullOrWhiteSpace(activeRemoteEmployeeId) && string.Equals(employee.RemoteEmployeeId, activeRemoteEmployeeId, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(activeEmployeeId) && string.Equals(employee.EmployeeId, activeEmployeeId, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(activeEmployeeCode) && string.Equals(employee.EmployeeCode, activeEmployeeCode, StringComparison.OrdinalIgnoreCase));

                if (isCurrent)
                {
                    ApplyEmployeeStatusChip(employee, TimeClockStateKey switch
                    {
                        "working" => "CLOCKED IN",
                        "break" => "ON BREAK",
                        "lunch" => "AT LUNCH",
                        _ => "SIGNED IN"
                    });
                }
                else
                {
                    ApplyEmployeeStatusChip(employee, "AVAILABLE");
                }
            }
        }

        private static void ApplyEmployeeStatusChip(WorkstationRosterEmployee employee, string state)
        {
            employee.StatusChipText = state;
            switch (state)
            {
                case "CLOCKED IN":
                    employee.StatusChipForeground = "#2BD576";
                    employee.StatusChipBackground = "#142BD576";
                    employee.StatusChipBorder = "#4A2BD576";
                    break;
                case "ON BREAK":
                case "AT LUNCH":
                    employee.StatusChipForeground = "#F5B642";
                    employee.StatusChipBackground = "#19F5B642";
                    employee.StatusChipBorder = "#52F5B642";
                    break;
                case "SIGNED IN":
                    employee.StatusChipForeground = "#38D5FF";
                    employee.StatusChipBackground = "#1838D5FF";
                    employee.StatusChipBorder = "#4A38D5FF";
                    break;
                default:
                    employee.StatusChipForeground = "#AAB6C8";
                    employee.StatusChipBackground = "#151C2836";
                    employee.StatusChipBorder = "#2F425B";
                    break;
            }
        }

        private static string ResolveRosterAvatarDisplayUrl(string avatarDisplayUrl, WorkstationAvatarRef? avatarRef)
        {
            if (string.IsNullOrWhiteSpace(avatarDisplayUrl))
                return "";

            try
            {
                var resolvedPath = AvatarImageCacheService.ResolveDisplayPathAsync(avatarDisplayUrl, avatarRef).GetAwaiter().GetResult();
                return string.IsNullOrWhiteSpace(resolvedPath) ? avatarDisplayUrl : resolvedPath;
            }
            catch
            {
                return avatarDisplayUrl;
            }
        }

        private static bool DoesAvatarImageSourceResolve(string avatarDisplayUrl)
        {
            if (string.IsNullOrWhiteSpace(avatarDisplayUrl))
                return false;

            if (avatarDisplayUrl.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Application.GetResourceStream(new Uri(avatarDisplayUrl, UriKind.Absolute)) != null;
                }
                catch
                {
                    return false;
                }
            }

            if (Uri.TryCreate(avatarDisplayUrl, UriKind.Absolute, out var absoluteUri))
            {
                if (absoluteUri.IsFile)
                    return File.Exists(absoluteUri.LocalPath);

                return absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps;
            }

            return File.Exists(avatarDisplayUrl);
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
            var savedPairingMessage = GetSavedPairingDegradedMessage();
            if (!string.IsNullOrWhiteSpace(savedPairingMessage) && (_authCache?.Package == null || _authCache.Package.Employees.Count == 0))
            {
                AuthCacheStatus = savedPairingMessage;
                return;
            }

            health ??= GetAuthCacheHealth();
            var lastError = ToUserFacingAuthCacheError(_authCache?.LastRefreshError);
            var suffix = string.IsNullOrWhiteSpace(lastError)
                ? ""
                : $" Last error: {lastError}";
            AuthCacheStatus = health.Message + suffix;
        }

        private string? GetSavedPairingDegradedMessage()
        {
            if (Registration == null)
                return null;

            if (!string.Equals(Registration.TrustStatus, "degraded", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Registration.TrustStatus, "offline", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Registration.TrustStatus, "blocked", StringComparison.OrdinalIgnoreCase))
                return null;

            return string.IsNullOrWhiteSpace(Registration.LastValidationError)
                ? "Device trust could not be validated right now. Continuing with the last saved workstation pairing."
                : Registration.LastValidationError;
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
                AvatarDisplayUrl = FirstNonEmpty(employee.EmployeeAvatarRef?.LocalApiUrl, employee.AvatarDisplayUrl),
                EmployeeAvatarRef = MapAvatarRef(employee.EmployeeAvatarRef),
                UpdatedUtc = employee.UpdatedUtc,
            };
        }

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

        private static WorkstationAvatarRef MapAvatarRef(RunBookWorkstationApiClient.AvatarRefDto? avatarRef)
            => avatarRef == null
                ? WorkstationAvatarRef.Placeholder()
                : new WorkstationAvatarRef
                {
                    AvatarAssetId = avatarRef.AvatarAssetId ?? "",
                    EmployeePublicId = avatarRef.EmployeePublicId ?? "",
                    MachinePublicId = avatarRef.MachinePublicId ?? "",
                    LocalApiUrl = avatarRef.LocalApiUrl ?? "",
                    ContentHash = avatarRef.ContentHash ?? "",
                    UpdatedAtUtc = avatarRef.UpdatedAtUtc ?? "",
                    ContentType = avatarRef.ContentType ?? "",
                    IsPlaceholder = avatarRef.IsPlaceholder,
                };

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
                EmployeeAvatarRef = employee.EmployeeAvatarRef,
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
                Tasks = package.Tasks.Select(MapInspectionTask).ToList()
            };
        }

        private static WorkstationInspectionTask MapInspectionTask(RunBookWorkstationApiClient.InspectionTask task)
        {
            return new WorkstationInspectionTask
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
            };
        }

        private static bool IsInProcessInspection(WorkstationInspectionTaskPackage? package)
        {
            if (package == null)
                return false;

            var label = $"{package.TemplateKey} {package.FeatureSetName}";
            return label.IndexOf("process", StringComparison.OrdinalIgnoreCase) >= 0;
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

                if (StartsWithErrorCode(rawMessage, "PAIRING_REQUIRED"))
                {
                    var userMessage = GetUserFacingErrorMessage(ex);
                    StatusText = userMessage;
                    ShowRunBookIssueAlert(
                        "pairing-required",
                        "Workstation Pairing Required",
                        userMessage,
                        "Open Settings, enter a current workstation pairing code from a supervisor or administrator, then refresh registration.");
                    return;
                }

                StatusText = GetUserFacingErrorMessage(ex);
                if (ShouldAttributeToLocalHostFailure(ex))
                {
                    MarkLocalHostRequestFailure(ex.Message);
                    OfflineStatus = "Unable to reach RunBook service.";
                    ShowRunBookIssueAlert(
                        "local-service-unreachable",
                        "RunBook.Service Unreachable",
                        "Workstation could not reach the local RunBook.Service authority.",
                        Registration == null
                            ? "Start RunBook.Service. If this workstation has never been paired, pair it after Service is reachable."
                            : "Start RunBook.Service or check the local network/port. The saved workstation pairing remains on this device while Service is unavailable.");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool ShouldAttributeToLocalHostFailure(Exception ex)
        {
            if (!IsConnectivityFailure(ex))
                return false;

            var message = ex.Message ?? "";
            if (message.IndexOf("30112", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("RunBook.Service", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("workstation-local", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("local workstation authority", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private string BuildStartOperationUnavailableMessage()
        {
            if (SelectedWorkOrder?.IsBackupJob == true ||
                (SelectedWorkOrder?.AssignmentLabel ?? "").IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (WorkOrderDetail?.AssignmentLabel ?? "").IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "This job is visible as a backup job, but it is not currently executable for you.\nAsk a supervisor to assign it before starting production time.";
            }

            var status = (SelectedOperation?.Status ?? "").Trim();
            if (status.Equals("In Progress", StringComparison.OrdinalIgnoreCase))
                return "This operation is already in progress.";

            if (status.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                return "This operation has already been completed.";

            return "This operation cannot be started right now.\nRefresh the work order or ask a supervisor to confirm your assignment.";
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

            if (IsWorkstationRegistrationRequiredMessage(message))
                return WorkstationRegistrationRequiredUserMessage;

                return message switch
                {
                    "Workstation request does not match the active Desktop shop." => "This workstation is pointed at a different Service-selected local shop. Refresh registration and employee auth, then try again.",
                    "Workstation request does not match the active shop." => "This workstation is pointed at a different local shop. Refresh registration and employee auth, then try again.",
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

        private static bool IsConnectivityFailureMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf("actively refused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("No connection could be made", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("Unable to connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0 && message.IndexOf("30112", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetServiceFailureStatus(string endpoint, string? message, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                if (IsWorkstationRegistrationRequiredMessage(message))
                    return WorkstationRegistrationRequiredUserMessage;

                if (message.IndexOf("endpoint not implemented on Service", StringComparison.OrdinalIgnoreCase) >= 0)
                    return $"Service endpoint failed: {endpoint} (endpoint not implemented on Service)";

                if (message.IndexOf("auth cache", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Employee auth cache is not available yet.";
            }

            return fallback;
        }

        private static bool IsWorkstationRegistrationRequiredMessage(string? message)
        {
            return !string.IsNullOrWhiteSpace(message)
                && message.IndexOf(WorkstationRegistrationRequiredRawMessage, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ToUserFacingAuthCacheError(string? message)
        {
            var trimmed = (message ?? "").Trim();
            if (trimmed.Length == 0)
                return "";

            return IsWorkstationRegistrationRequiredMessage(trimmed)
                ? WorkstationRegistrationRequiredUserMessage
                : trimmed;
        }

        private void HandleEmployeeSessionFailure(string message)
        {
            var userMessage = GetUserFacingErrorMessage(new InvalidOperationException(message));
            MarkLocalHostRequestSuccess();
            Logout();
            OfflineStatus = "RunBook.Service is reachable, but the workstation sign-in is no longer valid.";
            StatusText = userMessage;
        }

        private void MarkLocalHostRequestSuccess()
        {
            _lastLocalHostSuccessUtc = DateTime.UtcNow;
            _lastLocalHostFailureUtc = null;
            _lastLocalHostFailureReason = "";
            UpdateConnectionStatuses();
        }

        private void MarkLocalHostRequestFailure(string? reason)
        {
            _lastLocalHostFailureUtc = DateTime.UtcNow;
            _lastLocalHostFailureReason = reason ?? "";
            UpdateConnectionStatuses();
        }

        private void MarkDesktopRequestSuccess()
        {
            _lastDesktopSuccessUtc = DateTime.UtcNow;
            _lastDesktopFailureReason = "";
            UpdateConnectionStatuses();
        }

        private void MarkDesktopRequestFailure(string? reason)
        {
            _lastDesktopFailureUtc = DateTime.UtcNow;
            _lastDesktopFailureReason = reason ?? "";
            UpdateConnectionStatuses();
        }

        private void RefreshConnectionStatuses(bool force = true)
        {
            var nowUtc = DateTime.UtcNow;
            if (!force && nowUtc - _lastConnectionStatusRefreshUtc < TimeSpan.FromSeconds(5))
            {
                UpdateConnectionStatuses();
                return;
            }

            RefreshLocalHostProbe(nowUtc, force);

            var desktopProbeInterval = force ? DesktopProbeForceInterval : DesktopProbePassiveInterval;
            if (!RegistrationMissingDesktopProbePrerequisites() &&
                (!_lastDesktopProbeCheckedUtc.HasValue || nowUtc - _lastDesktopProbeCheckedUtc.Value >= desktopProbeInterval))
            {
                var desktopProbe = _connectionStatusService.ProbeDesktop(Settings, Registration);
                _lastDesktopProbeCheckedUtc = desktopProbe.CheckedUtc;

                if (desktopProbe.Attempted)
                {
                    if (desktopProbe.Succeeded)
                    {
                        _lastDesktopProbeSuccessUtc = desktopProbe.CheckedUtc;
                        _lastDesktopProbeFailureReason = "";
                    }
                    else
                    {
                        _lastDesktopProbeFailureUtc = desktopProbe.CheckedUtc;
                        _lastDesktopProbeFailureReason = desktopProbe.FailureReason;
                    }
                }
            }

            UpdateConnectionStatuses();
            _lastConnectionStatusRefreshUtc = DateTime.UtcNow;
        }

        private void UpdateConnectionStatuses()
        {
            var statuses = _connectionStatusService.BuildStatuses(
                Settings,
                Registration,
                CurrentSession,
                _lastLocalHostProbeCheckedUtc,
                _lastLocalHostSuccessUtc,
                _lastLocalHostFailureUtc,
                _lastLocalHostFailureReason,
                _lastDesktopProbeCheckedUtc,
                _lastDesktopProbeSuccessUtc,
                _lastDesktopProbeFailureUtc,
                _lastDesktopProbeFailureReason);

            ConnectionStatuses.Clear();
            foreach (var status in statuses)
                ConnectionStatuses.Add(status);
        }

        private bool RegistrationMissingDesktopProbePrerequisites()
        {
            return string.IsNullOrWhiteSpace(Settings.DesktopBaseUrl) ||
                   Registration == null ||
                   !Registration.IsActive ||
                   string.IsNullOrWhiteSpace(Registration.DeviceToken);
        }

        private bool LocalHostProbeMissingPrerequisites()
            => false;

        private void RefreshLocalHostProbe(DateTime nowUtc, bool force)
        {
            if (LocalHostProbeMissingPrerequisites())
            {
                _pendingLocalHostProbeRefresh = false;
                return;
            }

            var probeDue = !_lastLocalHostProbeCheckedUtc.HasValue ||
                           force ||
                           nowUtc - _lastLocalHostProbeCheckedUtc.Value >= TimeSpan.FromSeconds(2);
            if (!probeDue)
                return;

            if (_isLocalHostProbeInFlight)
            {
                _pendingLocalHostProbeRefresh = true;
                return;
            }

            StartLocalHostProbe();
        }

        private void StartLocalHostProbe()
        {
            _isLocalHostProbeInFlight = true;
            _pendingLocalHostProbeRefresh = false;
            var probeGeneration = ++_localHostProbeGeneration;
            var settings = new WorkstationSettings
            {
                DesktopBaseUrl = Settings.DesktopBaseUrl
            };

            _ = RunLocalHostProbeAsync(settings, probeGeneration);
        }

        private async Task RunLocalHostProbeAsync(WorkstationSettings settings, int probeGeneration)
        {
            var localHostProbe = await _connectionStatusService.ProbeLocalHostAsync(settings).ConfigureAwait(false);
            RunBookWorkstationApiClient.LocalHealthResponse? health = null;
            if (localHostProbe.Attempted && localHostProbe.Succeeded)
            {
                try
                {
                    health = await _api.GetLocalHealthAsync(settings, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (probeGeneration != _localHostProbeGeneration)
                    return;

                _isLocalHostProbeInFlight = false;
                _lastLocalHostProbeCheckedUtc = localHostProbe.CheckedUtc;

                if (localHostProbe.Attempted)
                {
                    if (localHostProbe.Succeeded)
                    {
                        _lastLocalHostSuccessUtc = localHostProbe.CheckedUtc;
                        _lastLocalHostFailureUtc = null;
                        _lastLocalHostFailureReason = "";
                        if (health != null)
                            ApplyServiceAuthorityHealth(health, "probe");
                    }
                    else
                    {
                        _lastLocalHostFailureUtc = localHostProbe.CheckedUtc;
                        _lastLocalHostFailureReason = localHostProbe.FailureReason;
                        BlockServiceAuthority("probe", "unavailable", localHostProbe.FailureReason ?? "RunBook Service is unavailable.", "", "");
                    }
                }

                UpdateConnectionStatuses();

                if (_pendingLocalHostProbeRefresh)
                    StartLocalHostProbe();
            });
        }

        private string BuildLocalHostEndpointLabel()
            => "http://localhost:30112";

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

        private WorkstationWorkOrderOperationSummary? GetActiveOperationForCurrentSession()
        {
            var employeeName = (CurrentSession?.Employee?.DisplayName ?? "").Trim();
            return WorkOrderDetail?.Operations?
                .Where(operation => operation.CanStop || operation.IsInProgress)
                .FirstOrDefault(operation =>
                    string.IsNullOrWhiteSpace(operation.OperatorName) ||
                    employeeName.Length == 0 ||
                    string.Equals(operation.OperatorName.Trim(), employeeName, StringComparison.OrdinalIgnoreCase));
        }

        private WorkstationCurrentJobContext? BuildResumeOperationContext(WorkstationWorkOrderOperationSummary operation)
        {
            if (WorkOrderDetail == null || operation == null)
                return null;

            return new WorkstationCurrentJobContext
            {
                WorkOrderId = WorkOrderDetail.WorkOrderId,
                WorkOrderNumber = WorkOrderDetail.WorkOrderNumber,
                PartNumber = WorkOrderDetail.PartNumber,
                PartDescription = WorkOrderDetail.PartDescription,
                VisibilitySource = WorkOrderDetail.VisibilitySource,
                OperationId = operation.OperationId,
                OperationNumber = operation.OperationNumber,
                OperationTitle = operation.Title,
                OperationStatus = operation.Status,
                StartedUtc = operation.StartedUtc,
                AssignmentLabel = WorkOrderDetail.AssignmentLabel,
            };
        }

        private async Task PromptToResumePausedOperationAsync()
        {
            var candidate = _pendingResumeOperation;
            var capturedFromPunchOut = _pendingResumeOperationCapturedFromPunchOut;
            if (candidate == null && HasWorkOrdersAccess)
            {
                await RefreshWorkOrdersAsync(false);
                candidate = RecentJob;
                capturedFromPunchOut = false;
            }

            if (!capturedFromPunchOut && !CanResumePausedOperation(candidate))
                return;

            var resume = await ShowRunBookDecisionAsync(
                "Resume Operation?",
                $"Do you want to resume OP{candidate!.OperationNumber:000} - {candidate.OperationTitle}?",
                "Resume Operation",
                "Keep Paused");

            if (!resume)
            {
                _pendingResumeOperation = null;
                _pendingResumeOperationCapturedFromPunchOut = false;
                return;
            }

            await ResumePausedOperationAsync(candidate);
        }

        private async Task ResumePausedOperationAsync(WorkstationCurrentJobContext candidate)
        {
            if (!EnsureServiceWritable())
                return;

            if (CurrentSession == null || !HasOperationExecutionAccess || candidate.WorkOrderId <= 0 || candidate.OperationId <= 0)
                return;

            SelectedModule = VisibleModules.FirstOrDefault(module => string.Equals(module.Key, "workorders", StringComparison.OrdinalIgnoreCase)) ?? SelectedModule;
            await RunBusyAsync(async () =>
            {
                var response = await _api.StartOperationAsync(
                    Settings,
                    CurrentSession,
                    candidate.WorkOrderId,
                    candidate.OperationId,
                    "Resumed after clock in.",
                    CancellationToken.None);
                ApplyWorkOrderMutationResponse(response, candidate.OperationId);
                _pendingResumeOperation = null;
                _pendingResumeOperationCapturedFromPunchOut = false;
                StatusText = $"Resumed OP{candidate.OperationNumber:000} - {candidate.OperationTitle}.";
            });

            if (CurrentSession != null && HasWorkOrdersAccess)
                await RefreshWorkOrdersAsync(false);
        }

        private static bool CanResumePausedOperation(WorkstationCurrentJobContext? candidate)
        {
            if (candidate == null || candidate.WorkOrderId <= 0 || candidate.OperationId <= 0)
                return false;

            var status = (candidate.OperationStatus ?? "").Trim();
            return status.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   status.IndexOf("pause", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsProductionPausePunch(string eventType)
        {
            var normalized = (eventType ?? "").Trim().ToUpperInvariant();
            return normalized == "CLOCK_OUT" || normalized == "BREAK_START" || normalized == "LUNCH_START";
        }

        private static bool IsProductionResumePunch(string eventType)
        {
            var normalized = (eventType ?? "").Trim().ToUpperInvariant();
            return normalized == "CLOCK_IN" || normalized == "BREAK_END" || normalized == "LUNCH_END";
        }

        private static bool IsClockOutPunch(string eventType)
            => string.Equals((eventType ?? "").Trim(), "clock_out", StringComparison.OrdinalIgnoreCase);

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

        private static string FormatCaptureExpiry(string value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return $"Expires {parsed.ToLocalTime():h:mm tt}";

            return string.IsNullOrWhiteSpace(value) ? "Expires soon" : $"Expires {value}";
        }

        private void UpdateOperationAttachmentIntakeQr()
        {
            if (SelectedWorkOrder == null || SelectedOperation == null)
            {
                OperationAttachmentIntakeUrl = "";
                OperationAttachmentIntakeQrImage = null;
                OperationAttachmentIntakeStatusText = "Select an operation to create an attachment intake QR.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Settings.ShopId))
            {
                OperationAttachmentIntakeUrl = "";
                OperationAttachmentIntakeQrImage = null;
                OperationAttachmentIntakeStatusText = "Shop identity is required before Mobile can attach operation files.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Settings.ControlBaseUrl))
            {
                OperationAttachmentIntakeUrl = "";
                OperationAttachmentIntakeQrImage = null;
                OperationAttachmentIntakeStatusText = "Control URL is required before Mobile can attach operation files.";
                return;
            }

            if (SelectedWorkOrder.WorkOrderId <= 0 || SelectedOperation.OperationId <= 0)
            {
                OperationAttachmentIntakeUrl = "";
                OperationAttachmentIntakeQrImage = null;
                OperationAttachmentIntakeStatusText = "The selected operation is not eligible for Mobile attachment intake yet.";
                return;
            }

            var issuedAt = DateTimeOffset.UtcNow;
            var expiresAt = issuedAt.AddMinutes(30);
            // TODO(release): this is a temporary Control/Mobile attachment intake compatibility QR.
            // The final always-visible operation QR should open the Service-owned operation context/menu;
            // short-lived action QR/session handoff should be generated only after an action is selected.
            var payload = new OperationAttachmentIntakeQrPayload
            {
                Kind = "operation-attachment-intake",
                ShopPublicId = Settings.ShopId.Trim(),
                WorkOrderRef = BuildOperationAttachmentWorkOrderRef(Settings.ShopId, SelectedWorkOrder.WorkOrderId),
                OperationRef = BuildOperationAttachmentOperationRef(Settings.ShopId, SelectedWorkOrder.WorkOrderId, SelectedOperation.OperationId),
                ExpiresAt = expiresAt.ToString("O", CultureInfo.InvariantCulture)
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var encoded = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
            var url = $"runbookmobile:///operation-attachment-intake?payload={Uri.EscapeDataString(encoded)}";
            OperationAttachmentIntakeUrl = url;
            OperationAttachmentIntakeQrImage = BuildQrImage(url);
            OperationAttachmentIntakeStatusText = "Temporary: scan to add operation attachment";
        }

        // TODO(release): replace these temporary attachment target refs with canonical remote work order/operation public ids
        // once Service/Desktop exposes the canonical public-id contract to Workstation.
        private static string BuildOperationAttachmentWorkOrderRef(string shopId, int workOrderId)
        {
            return "wo_" + BuildOperationAttachmentRefHash("work-order", shopId, workOrderId.ToString(CultureInfo.InvariantCulture));
        }

        private static string BuildOperationAttachmentOperationRef(string shopId, int workOrderId, int operationId)
        {
            return "op_" + BuildOperationAttachmentRefHash(
                "operation",
                shopId,
                workOrderId.ToString(CultureInfo.InvariantCulture),
                operationId.ToString(CultureInfo.InvariantCulture));
        }

        private static string BuildOperationAttachmentRefHash(params string[] parts)
        {
            var seed = "runbook-operation-attachment-target-ref-v1|" + string.Join("|", parts ?? Array.Empty<string>());
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToLowerInvariant()));
            var builder = new StringBuilder(24);
            for (var index = 0; index < 12; index++)
                builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static BitmapImage BuildQrImage(string value)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(value ?? "", QRCodeGenerator.ECCLevel.M);
            var qr = new PngByteQRCode(data);
            var bytes = qr.GetGraphic(18);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private sealed class OperationAttachmentIntakeQrPayload
        {
            public string Kind { get; set; } = "";
            public string ShopPublicId { get; set; } = "";
            public string WorkOrderRef { get; set; } = "";
            public string OperationRef { get; set; } = "";
            public string ExpiresAt { get; set; } = "";
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

        private async Task InitializeFromControlSessionSafeAsync()
        {
            try
            {
                await InitializeFromControlSessionAsync();
            }
            catch (Exception ex)
            {
                DebugLogService.WriteException("InitializeFromControlSessionSafeAsync", ex);
                if (IsConnectivityFailure(ex))
                {
                    MarkLocalHostRequestFailure(ex.Message);
                    OfflineStatus = "RunBook.Service is unavailable right now.";
                    StatusText = "RunBook could not reach the local workstation authority.";
                    UpdateAuthCacheStatus();
                    return;
                }

                StatusText = GetUserFacingErrorMessage(ex);
            }
        }

        private void RaiseCommandStates()
        {
            if (LoginCommand is RelayCommand login) login.RaiseCanExecuteChanged();
            if (LogoutCommand is RelayCommand logout) logout.RaiseCanExecuteChanged();
            if (SaveSettingsCommand is RelayCommand save) save.RaiseCanExecuteChanged();
            if (ConnectControlCommand is RelayCommand connect) connect.RaiseCanExecuteChanged();
            if (DisconnectControlCommand is RelayCommand disconnect) disconnect.RaiseCanExecuteChanged();
            if (RefreshRegistrationCommand is RelayCommand refresh) refresh.RaiseCanExecuteChanged();
            if (RefreshEmployeeAuthCommand is RelayCommand refreshEmployeeAuth) refreshEmployeeAuth.RaiseCanExecuteChanged();
            if (OpenSupervisorDialogCommand is RelayCommand openSupervisor) openSupervisor.RaiseCanExecuteChanged();
            if (CloseSupervisorDialogCommand is RelayCommand closeSupervisor) closeSupervisor.RaiseCanExecuteChanged();
            if (CloseRunBookAlertCommand is RelayCommand closeRunBookAlert) closeRunBookAlert.RaiseCanExecuteChanged();
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
            if (SaveDataEntryCommand is RelayCommand saveDataEntry) saveDataEntry.RaiseCanExecuteChanged();
            if (SubmitQuantityCommand is RelayCommand submitQuantity) submitQuantity.RaiseCanExecuteChanged();
            if (SubmitScrapCommand is RelayCommand submitScrap) submitScrap.RaiseCanExecuteChanged();
            if (SubmitOperationNoteCommand is RelayCommand submitNote) submitNote.RaiseCanExecuteChanged();
            if (SubmitOperationHelpCommand is RelayCommand submitHelp) submitHelp.RaiseCanExecuteChanged();
            if (RefreshInspectionTasksCommand is RelayCommand refreshInspection) refreshInspection.RaiseCanExecuteChanged();
            if (SelectInspectionTaskCommand is RelayCommand<WorkstationInspectionTask> selectInspection) selectInspection.RaiseCanExecuteChanged();
            if (SubmitInspectionResultCommand is RelayCommand submitInspection) submitInspection.RaiseCanExecuteChanged();
            if (OpenOperationInspectionCommand is RelayCommand openOperationInspection) openOperationInspection.RaiseCanExecuteChanged();
            if (CloseOperationInspectionCommand is RelayCommand closeOperationInspection) closeOperationInspection.RaiseCanExecuteChanged();
            if (OpenMobileCaptureCommand is RelayCommand openMobileCapture) openMobileCapture.RaiseCanExecuteChanged();
            if (CloseMobileCaptureCommand is RelayCommand closeMobileCapture) closeMobileCapture.RaiseCanExecuteChanged();
            if (RefreshOperationAttachmentsCommand is RelayCommand refreshOperationAttachments) refreshOperationAttachments.RaiseCanExecuteChanged();
            if (PrimaryOperatorActionCommand is RelayCommand primaryOperatorAction) primaryOperatorAction.RaiseCanExecuteChanged();
            if (OpenRosterEmployeeCommand is RelayCommand<WorkstationRosterEmployee> openRoster) openRoster.RaiseCanExecuteChanged();
            if (LaunchWelcomeFlowCommand is RelayCommand<string> launchWelcomeFlow) launchWelcomeFlow.RaiseCanExecuteChanged();
            if (OpenEmployeeBrowserCommand is RelayCommand openEmployeeBrowser) openEmployeeBrowser.RaiseCanExecuteChanged();
            if (CloseEmployeeBrowserCommand is RelayCommand closeEmployeeBrowser) closeEmployeeBrowser.RaiseCanExecuteChanged();
            if (SubmitEmployeeQuickSearchCommand is RelayCommand submitEmployeeQuickSearch) submitEmployeeQuickSearch.RaiseCanExecuteChanged();
            if (AppendPasscodeDigitCommand is RelayCommand<string> append) append.RaiseCanExecuteChanged();
            if (BackspacePasscodeCommand is RelayCommand backspace) backspace.RaiseCanExecuteChanged();
            if (ClearPasscodeCommand is RelayCommand clear) clear.RaiseCanExecuteChanged();
            if (CancelPasscodeCommand is RelayCommand cancel) cancel.RaiseCanExecuteChanged();
        }

        private UnlockTimingTrace? GetOrCreateUnlockTrace(WorkstationRosterEmployee? employee)
        {
            if (_unlockTimingTrace != null && !_unlockTimingTrace.ShellVisibleLogged)
                return _unlockTimingTrace;

            if (employee == null)
                return _unlockTimingTrace;

            _unlockTimingTrace = UnlockTimingTrace.Start(employee);
            return _unlockTimingTrace;
        }

        private static void LogUnlockTrace(UnlockTimingTrace? trace, string step, string? details = null)
        {
            if (trace == null)
                return;

            var suffix = string.IsNullOrWhiteSpace(details) ? "" : $" | {details}";
            DebugLogService.Write($"UnlockTrace | {trace.TraceId} | {step} | elapsed_ms={trace.Stopwatch.ElapsedMilliseconds}{suffix}");
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private sealed class UnlockTimingTrace
        {
            public string TraceId { get; }
            public Stopwatch Stopwatch { get; }
            public bool ShellVisibleLogged { get; set; }

            private UnlockTimingTrace(string traceId)
            {
                TraceId = traceId;
                Stopwatch = Stopwatch.StartNew();
            }

            public static UnlockTimingTrace Start(WorkstationRosterEmployee employee)
            {
                var employeeKey = string.IsNullOrWhiteSpace(employee.EmployeeCode) ? employee.EmployeeId : employee.EmployeeCode;
                var traceId = $"unlock-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{(employeeKey ?? "employee").Trim()}";
                return new UnlockTimingTrace(traceId);
            }
        }
    }
}




