using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RunBook.Workstation.Models
{
    public enum ConnectionHealth
    {
        Healthy,
        Degraded,
        Disconnected
    }

    public sealed class ConnectionStatusItem
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public ConnectionHealth Health { get; set; }
        public string Reason { get; set; } = "";
        public string LastCheckedUtc { get; set; } = "";
        public string DisplayLabel => (Label ?? "").ToUpperInvariant();
        public string DotColor => Health switch
        {
            ConnectionHealth.Healthy => "#43D17E",
            ConnectionHealth.Degraded => "#F2C75E",
            _ => "#FF5D66"
        };
        public string BackgroundColor => Health switch
        {
            ConnectionHealth.Healthy => "#123CC875",
            ConnectionHealth.Degraded => "#14E8BC52",
            _ => "#18F0525E"
        };
        public string BorderColor => Health switch
        {
            ConnectionHealth.Healthy => "#553CC875",
            ConnectionHealth.Degraded => "#5CE8BC52",
            _ => "#66F0525E"
        };
        public string DetailText => string.IsNullOrWhiteSpace(LastCheckedUtc)
            ? ""
            : $"Last checked: {LastCheckedUtc}";
    }

    public sealed class WorkstationSettings : INotifyPropertyChanged
    {
        private string _controlBaseUrl = "http://localhost:3000";
        private string _desktopBaseUrl = "http://localhost:30112";
        private string _shopId = "";
        private string _workstationId = "";
        private string _workstationName = Environment.MachineName;
        private string _shopName = "RunBook Shop";
        private string _pairingCode = "";

        public string ControlBaseUrl { get => _controlBaseUrl; set => SetField(ref _controlBaseUrl, value ?? ""); }
        public string DesktopBaseUrl { get => _desktopBaseUrl; set => SetField(ref _desktopBaseUrl, value ?? ""); }
        public string ShopId { get => _shopId; set => SetField(ref _shopId, value ?? ""); }
        public string WorkstationId { get => _workstationId; set => SetField(ref _workstationId, value ?? ""); }
        public string WorkstationName { get => _workstationName; set => SetField(ref _workstationName, value ?? ""); }
        public string ShopName { get => _shopName; set => SetField(ref _shopName, value ?? ""); }
        public string PairingCode { get => _pairingCode; set => SetField(ref _pairingCode, value ?? ""); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class WorkstationEmployeeIdentity
    {
        public string RemoteEmployeeId { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string ShopId { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Role { get; set; } = "";
    }

    public sealed class WorkstationSessionSnapshot
    {
        public string Token { get; set; } = "";
        public string IssuedAtUtc { get; set; } = "";
        public string ExpiresAtUtc { get; set; } = "";
        public WorkstationEmployeeIdentity Employee { get; set; } = new WorkstationEmployeeIdentity();
        public List<string> Modules { get; set; } = new List<string>();
        public Dictionary<string, bool> Actions { get; set; } = new Dictionary<string, bool>();
        public WorkstationCapabilityPayload Capabilities { get; set; } = new WorkstationCapabilityPayload();
        public int SessionTimeoutMinutes { get; set; } = 15;
        public string WorkstationId { get; set; } = "";
        public string WorkstationName { get; set; } = "";
        public string ShopId { get; set; } = "";
        public string ShopName { get; set; } = "";
    }

    public sealed class ControlSessionRecord
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string ExpiresAtUtc { get; set; } = "";
        public string UserId { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public sealed class WorkstationRuntimeAccessTokenRecord
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string ExpiresAtUtc { get; set; } = "";
        public string RefreshExpiresAtUtc { get; set; } = "";
        public string TokenType { get; set; } = "Bearer";
        public string ScopeMode { get; set; } = "workstation";
        public string ShopId { get; set; } = "";
        public string WorkstationId { get; set; } = "";
        public string WorkstationName { get; set; } = "";
        public string OperatorId { get; set; } = "";
        public string OperatorDisplayName { get; set; } = "";
        public string EmployeeSessionToken { get; set; } = "";
        public string IssuedAtUtc { get; set; } = "";
        public List<string> ScopeClaims { get; set; } = new List<string>();
        public List<string> AllowedChannels { get; set; } = new List<string>();
        public string Issuer { get; set; } = "desktop-local";
        public string Audience { get; set; } = "runbook-runtime";
    }

    public sealed class WorkstationRuntimeAccessRequest
    {
        public string ScopeMode { get; set; } = "workstation";
        public string ShopId { get; set; } = "";
        public string WorkstationId { get; set; } = "";
        public string WorkstationName { get; set; } = "";
        public string OperatorId { get; set; } = "";
        public string OperatorDisplayName { get; set; } = "";
        public string EmployeeSessionToken { get; set; } = "";
    }

    public sealed class WorkstationPunchRecord
    {
        public string Id { get; set; } = "";
        public string EventType { get; set; } = "";
        public string ClientTs { get; set; } = "";
        public string ServerTs { get; set; } = "";
        public string Source { get; set; } = "";
        public string Note { get; set; } = "";
        public bool IsPendingSync { get; set; }
        public string SyncState { get; set; } = "";
    }

    public sealed class WorkstationModuleCard
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Glyph { get; set; } = "";
        public bool IsPlaceholder { get; set; }
    }

    public sealed class WorkstationRegistrationSnapshot
    {
        public string WorkstationId { get; set; } = "";
        public string ShopId { get; set; } = "";
        public string WorkstationName { get; set; } = "";
        public string Status { get; set; } = "";
        public string DeviceToken { get; set; } = "";
        public string TokenIssuedUtc { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public int EnrollmentVersion { get; set; } = 1;
        public bool Existing { get; set; }
        public string LastSyncUtc { get; set; } = "";
        public string TrustStatus { get; set; } = "";
        public string LastValidatedUtc { get; set; } = "";
        public string LastValidationError { get; set; } = "";
        public string PairingRequiredReason { get; set; } = "";
        public string TokenExpiresUtc { get; set; } = "";
        public bool RefreshAvailable { get; set; }
    }

    public sealed class WorkstationEmployeeAuthCache
    {
        public WorkstationEmployeeAuthPackage Package { get; set; } = new WorkstationEmployeeAuthPackage();
        public string LastRefreshAttemptUtc { get; set; } = "";
        public string LastRefreshSuccessUtc { get; set; } = "";
        public string LastRefreshError { get; set; } = "";
        public string LastSuccessfulPackageHash { get; set; } = "";
    }

    public sealed class WorkstationEmployeeAuthPackage
    {
        public string ShopId { get; set; } = "";
        public string WorkstationId { get; set; } = "";
        public int AuthPackageVersion { get; set; } = 1;
        public string PackageHash { get; set; } = "";
        public string LastSyncedUtc { get; set; } = "";
        public string SourceState { get; set; } = "desktop_authoritative";
        public WorkstationEmployeeAuthPolicy Policy { get; set; } = new WorkstationEmployeeAuthPolicy();
        public List<WorkstationAuthPackageEmployee> Employees { get; set; } = new List<WorkstationAuthPackageEmployee>();
    }

    public sealed class WorkstationEmployeeAuthPolicy
    {
        public int WarningAfterHours { get; set; } = 24;
        public int HardStopAfterHours { get; set; } = 168;
    }

    public sealed class WorkstationAuthPackageEmployee
    {
        public string EmployeeId { get; set; } = "";
        public string RemoteEmployeeId { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Role { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsActive { get; set; }
        public bool WorkstationAccessEnabled { get; set; }
        public bool CanTimeClock { get; set; }
        public bool CanDashboardView { get; set; }
        public bool CanJobsModule { get; set; }
        public bool CanInspectionEntry { get; set; }
        public bool CanCameraView { get; set; }
        public bool HasWorkstationPasscode { get; set; }
        public int SessionTimeoutMinutes { get; set; } = 15;
        public string AvatarDisplayUrl { get; set; } = "";
        public WorkstationAvatarRef EmployeeAvatarRef { get; set; } = WorkstationAvatarRef.Placeholder();
        public string UpdatedUtc { get; set; } = "";
    }

    public sealed class WorkstationCapabilityPayload
    {
        public WorkstationCapabilityEmployee Employee { get; set; } = new WorkstationCapabilityEmployee();
        public WorkstationCapabilityWorkstation Workstation { get; set; } = new WorkstationCapabilityWorkstation();
        public Dictionary<string, bool> Modules { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, bool> Actions { get; set; } = new Dictionary<string, bool>();
        public WorkstationCurrentJobContext? CurrentJob { get; set; }
        public WorkstationCurrentJobContext? RecentJob { get; set; }
    }

    public sealed class WorkstationShopAwareness
    {
        public WorkstationShopSummary Summary { get; set; } = new WorkstationShopSummary();
        public List<WorkstationOperatorAwareness> Operators { get; set; } = new List<WorkstationOperatorAwareness>();
    }

    public sealed class WorkstationShopSummary
    {
        public int ActiveJobs { get; set; }
        public int ActiveOperators { get; set; }
        public int WaitingJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int IdleOperators { get; set; }
        public int VisibleJobs { get; set; }
    }

    public sealed class WorkstationOperatorAwareness
    {
        public int OperatorId { get; set; }
        public string OperatorName { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string Status { get; set; } = "idle";
        public int WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = "";
        public int OperationId { get; set; }
        public int OperationNumber { get; set; }
        public string WorkstationName { get; set; } = "";
        public string StartedUtc { get; set; } = "";
        public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
        public string ContextLine => IsActive && WorkOrderId > 0
            ? $"{WorkOrderNumber} | Op {OperationNumber:000}"
            : "No active job";
        public string WorkstationLine => string.IsNullOrWhiteSpace(WorkstationName)
            ? (IsActive ? "Active" : "Idle")
            : $"{(IsActive ? "Active" : "Idle")} | {WorkstationName}";
    }

    public sealed class WorkstationActiveContext
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public int OperationId { get; set; }
        public int OperationNumber { get; set; }
        public string OperationTitle { get; set; } = "";
    }

    public sealed class WorkstationCurrentJobContext
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public string VisibilitySource { get; set; } = "";
        public int OperationId { get; set; }
        public int OperationNumber { get; set; }
        public string OperationTitle { get; set; } = "";
        public string OperationStatus { get; set; } = "";
        public string StartedUtc { get; set; } = "";
        public string AssignmentLabel { get; set; } = "";
    }

    public sealed class WorkstationCapabilityEmployee
    {
        public string RemoteEmployeeId { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Role { get; set; } = "";
        public int SessionTimeoutMinutes { get; set; } = 15;
    }

    public sealed class WorkstationCapabilityWorkstation
    {
        public string ShopId { get; set; } = "";
        public string ShopName { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
    }

    public sealed class WorkstationRosterEmployee
    {
        public string RemoteEmployeeId { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Role { get; set; } = "";
        public string Initials { get; set; } = "";
        public string AvatarDisplayUrl { get; set; } = "";
        public WorkstationAvatarRef EmployeeAvatarRef { get; set; } = WorkstationAvatarRef.Placeholder();
        public bool HasAvatarDisplayUrl => !string.IsNullOrWhiteSpace(AvatarDisplayUrl);
        public string AvatarBrush { get; set; } = "#5E84C6";
        public string AccentBrush { get; set; } = "#7EA4E2";
        public string AccessSummary { get; set; } = "";
        public int SessionTimeoutMinutes { get; set; } = 15;
        public bool CanTimeClock { get; set; }
        public bool CanDashboardView { get; set; }
        public bool CanJobsModule { get; set; }
        public bool CanInspectionEntry { get; set; }
        public bool CanCameraView { get; set; }
        public bool HasWorkstationPasscode { get; set; }
    }

    public sealed class WorkstationAvatarRef
    {
        public string AvatarAssetId { get; set; } = "";
        public string EmployeePublicId { get; set; } = "";
        public string MachinePublicId { get; set; } = "";
        public string LocalApiUrl { get; set; } = "";
        public string ContentHash { get; set; } = "";
        public string UpdatedAtUtc { get; set; } = "";
        public string ContentType { get; set; } = "";
        public bool IsPlaceholder { get; set; } = true;

        public static WorkstationAvatarRef Placeholder()
            => new WorkstationAvatarRef { IsPlaceholder = true };
    }

    public sealed class WorkstationWorkOrderSummary
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public string Revision { get; set; } = "";
        public string Status { get; set; } = "";
        public int Quantity { get; set; }
        public string DueDate { get; set; } = "";
        public int OperationCount { get; set; }
        public bool HasDrawings { get; set; }
        public string ReleasedUtc { get; set; } = "";
        public string VisibilitySource { get; set; } = "";
        public bool IsCurrentJob { get; set; }
        public bool IsAssigned { get; set; }
        public bool IsDepartmentMatch { get; set; }
        public bool IsBackupJob { get; set; }
        public int ActiveOperationId { get; set; }
        public int ActiveOperationNumber { get; set; }
        public string ActiveOperationTitle { get; set; } = "";
        public string AssignmentLabel { get; set; } = "";
        public int ProgressPercent { get; set; }
        public int CompletedOperations { get; set; }
        public int TotalOperations { get; set; }
        public WorkstationOperationStatusSummary OperationSummary { get; set; } = new WorkstationOperationStatusSummary();
        public List<WorkstationActiveOperator> ActiveOperators { get; set; } = new List<WorkstationActiveOperator>();
        public int ActiveOperatorCount => ActiveOperators?.Count ?? 0;
        public string ProgressLine => TotalOperations <= 0
            ? "No released operations"
            : $"{ProgressPercent}% complete  •  {CompletedOperations}/{TotalOperations} ops";
        public string ActiveOperatorLine => ActiveOperatorCount switch
        {
            <= 0 => "No active operators",
            1 => $"{ActiveOperators[0].EmployeeName} active",
            _ => $"{ActiveOperatorCount} active operators"
        };
        public string CardBackgroundBrush => "#D40F1B2F";
        public string CardBorderBrush => IsCurrentJob ? "#5531C7FF" : "#3B526F";
        public string StatusPillBackgroundBrush => IsCurrentJob ? "#16F08A24" : "#15162636";
        public string StatusPillBorderBrush => IsCurrentJob ? "#66F08A24" : "#3B526F";
        public string StatusTextBrush => IsCurrentJob ? "#F08A24" : "#B0BDD0";
        public string AssignmentPillBackgroundBrush => IsCurrentJob ? "#123CC875" : "#15162636";
        public string AssignmentPillBorderBrush => IsCurrentJob ? "#553CC875" : "#3B526F";
        public string AssignmentTextBrush => IsCurrentJob ? "#3CC875" : "#B0BDD0";
    }

    public sealed class WorkstationWorkOrderDetail
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public string PartDescription { get; set; } = "";
        public string Revision { get; set; } = "";
        public string Status { get; set; } = "";
        public int Quantity { get; set; }
        public string DueDate { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string PoNumber { get; set; } = "";
        public string SourceComponentId { get; set; } = "";
        public string ReleaseState { get; set; } = "";
        public string ReleasedUtc { get; set; } = "";
        public string SnapshotLoadSource { get; set; } = "";
        public bool HasDrawings { get; set; }
        public string VisibilitySource { get; set; } = "";
        public bool IsCurrentJob { get; set; }
        public bool IsAssigned { get; set; }
        public string AssignmentLabel { get; set; } = "";
        public int ProgressPercent { get; set; }
        public int CompletedOperations { get; set; }
        public int TotalOperations { get; set; }
        public WorkstationOperationStatusSummary OperationSummary { get; set; } = new WorkstationOperationStatusSummary();
        public List<WorkstationActiveOperator> ActiveOperators { get; set; } = new List<WorkstationActiveOperator>();
        public List<WorkstationWorkOrderOperationSummary> Operations { get; set; } = new List<WorkstationWorkOrderOperationSummary>();
    }

    public sealed class WorkstationOperationStatusSummary
    {
        public int NotStarted { get; set; }
        public int InProgress { get; set; }
        public int Completed { get; set; }
    }

    public sealed class WorkstationActiveOperator
    {
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public int OperationId { get; set; }
        public int OperationNumber { get; set; }
        public string OperationTitle { get; set; } = "";
        public string WorkstationId { get; set; } = "";
        public string WorkstationName { get; set; } = "";
        public string StartedUtc { get; set; } = "";
        public string DisplayLine
        {
            get
            {
                var operatorName = string.IsNullOrWhiteSpace(EmployeeName) ? "Unknown operator" : EmployeeName;
                var location = string.IsNullOrWhiteSpace(WorkstationName) ? WorkstationId : WorkstationName;
                return string.IsNullOrWhiteSpace(location)
                    ? $"{operatorName}  •  Op {OperationNumber:000}"
                    : $"{operatorName}  •  Op {OperationNumber:000}  •  {location}";
            }
        }
    }

    public sealed class WorkstationWorkOrderOperationSummary
    {
        public int OperationId { get; set; }
        public int OperationNumber { get; set; }
        public string Title { get; set; } = "";
        public string Department { get; set; } = "";
        public string WorkCenter { get; set; } = "";
        public string Status { get; set; } = "";
        public string OperatorName { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string StartedUtc { get; set; } = "";
        public string CompletedUtc { get; set; } = "";
        public bool CanStart { get; set; }
        public bool CanStop { get; set; }
        public bool CanComplete { get; set; }
        public bool IsPaused => ContainsStatus("stop") || ContainsStatus("pause");
        public bool IsInProgress => ContainsStatus("progress") || CanStop;
        public bool IsCompleted => ContainsStatus("complete") || !string.IsNullOrWhiteSpace(CompletedUtc);
        public string DisplayStatus => IsPaused ? "Paused" : Status;
        public bool IsInspection => ContainsStatus("inspection") ||
            (Title ?? "").IndexOf("inspection", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (Department ?? "").IndexOf("quality", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (Department ?? "").IndexOf("qa", StringComparison.OrdinalIgnoreCase) >= 0;
        public string RowBackgroundBrush => IsInProgress ? "#16F08A24" : "#D40F1B2F";
        public string RowBorderBrush => IsInProgress ? "#66F08A24" : IsCompleted ? "#553CC875" : IsInspection ? "#5C8D63E6" : "#3B526F";
        public string MarkerBackgroundBrush => IsInProgress ? "#22F08A24" : IsCompleted ? "#123CC875" : IsInspection ? "#148D63E6" : "#15162636";
        public string MarkerBorderBrush => IsInProgress ? "#66F08A24" : IsCompleted ? "#553CC875" : IsInspection ? "#5C8D63E6" : "#3B526F";
        public string OperationNumberBrush => IsInProgress ? "#F08A24" : IsCompleted ? "#3CC875" : IsInspection ? "#8D63E6" : "#31C7FF";
        public string StatusPillBackgroundBrush => IsInProgress ? "#16F08A24" : IsCompleted ? "#123CC875" : "#1031C7FF";
        public string StatusPillBorderBrush => IsInProgress ? "#66F08A24" : IsCompleted ? "#553CC875" : "#5531C7FF";
        public string StatusTextBrush => IsInProgress ? "#F08A24" : IsCompleted ? "#3CC875" : "#31C7FF";

        private bool ContainsStatus(string value)
            => (Status ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class WorkstationDrawingPackage
    {
        public int WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public string Revision { get; set; } = "";
        public List<WorkstationDrawingReference> Drawings { get; set; } = new List<WorkstationDrawingReference>();
    }

    public sealed class WorkstationDrawingReference
    {
        public string DrawingId { get; set; } = "";
        public string Label { get; set; } = "";
        public int Revision { get; set; }
        public int PageCount { get; set; }
        public bool IsPrimary { get; set; }
        public string ContentType { get; set; } = "";
        public string DownloadRoute { get; set; } = "";
    }

    public sealed class WorkstationInspectionTaskPackage
    {
        public int WorkOrderId { get; set; }
        public int OperationId { get; set; }
        public int FeatureSetId { get; set; }
        public string FeatureSetName { get; set; } = "";
        public string TemplateKey { get; set; } = "TemplateA";
        public int SessionId { get; set; }
        public List<WorkstationInspectionTask> Tasks { get; set; } = new List<WorkstationInspectionTask>();
    }

    public sealed class WorkstationInspectionTask
    {
        public int FeatureId { get; set; }
        public int BalloonNumber { get; set; }
        public string ItemNumber { get; set; } = "";
        public string Zone { get; set; } = "";
        public string FeatureText { get; set; } = "";
        public string Nominal { get; set; } = "";
        public string TolPlus { get; set; } = "";
        public string TolMinus { get; set; } = "";
        public string Units { get; set; } = "";
        public string Classification { get; set; } = "";
        public string InspectionMethod { get; set; } = "";
        public string Frequency { get; set; } = "";
        public string InputType { get; set; } = "Text";
        public string InputOptionsJson { get; set; } = "";
        public string Notes { get; set; } = "";
        public int SampleIndex { get; set; } = 1;
        public string ActualValue { get; set; } = "";
        public string PassFail { get; set; } = "";
        public string Inspector { get; set; } = "";
        public string MeasuredUtc { get; set; } = "";
        public string ResultNotes { get; set; } = "";
    }

    public sealed class WorkstationDataEntrySection
    {
        public string Number { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string AccentBrush { get; set; } = "#31C7FF";
        public string BorderBrush { get; set; } = "#5531C7FF";
        public string BackgroundBrush { get; set; } = "#D40F1B2F";
        public List<WorkstationDataEntryRequirementRow> Items { get; set; } = new List<WorkstationDataEntryRequirementRow>();
    }

    public sealed class WorkstationDataEntryRequirementRow
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string State { get; set; } = "";
        public string StateBrush { get; set; } = "#B0BDD0";
        public string StateBorderBrush { get; set; } = "#3B526F";
        public string StateBackgroundBrush { get; set; } = "#15162636";
    }

    public sealed class WorkstationMaterialHeatLotEntry : INotifyPropertyChanged
    {
        private string _heatLotNumber = "";
        private string _quantityText = "";
        private string _unit = "Bars";
        private bool _certReceived;
        private string _notes = "";

        public string HeatLotNumber { get => _heatLotNumber; set { value ??= ""; if (_heatLotNumber != value) { _heatLotNumber = value; OnPropertyChanged(); } } }
        public string QuantityText { get => _quantityText; set { value ??= ""; if (_quantityText != value) { _quantityText = value; OnPropertyChanged(); } } }
        public string Unit { get => _unit; set { value ??= ""; if (_unit != value) { _unit = value; OnPropertyChanged(); } } }
        public bool CertReceived { get => _certReceived; set { if (_certReceived != value) { _certReceived = value; OnPropertyChanged(); } } }
        public string Notes { get => _notes; set { value ??= ""; if (_notes != value) { _notes = value; OnPropertyChanged(); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class WorkstationTimeclockSnapshot
    {
        public string RemoteEmployeeId { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string ShopId { get; set; } = "";
        public string WorkstationId { get; set; } = "";
        public string CurrentStatus { get; set; } = "OUT";
        public string StatusSinceUtc { get; set; } = "";
        public string LastPunchUtc { get; set; } = "";
        public bool SupportsLunch { get; set; } = true;
        public string LastSyncUtc { get; set; } = "";
        public string LastSyncMessage { get; set; } = "";
        public List<WorkstationPunchRecord> RecentPunches { get; set; } = new List<WorkstationPunchRecord>();
        public List<WorkstationTimeOffRequestRecord> RecentTimeOffRequests { get; set; } = new List<WorkstationTimeOffRequestRecord>();
    }

    public sealed class WorkstationTimeOffRequestRecord
    {
        public string Id { get; set; } = "";
        public string RequestType { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public decimal? HoursRequested { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = "";
        public int OperationId { get; set; }
        public int OperationNumber { get; set; }
        public string OperationTitle { get; set; } = "";
        public string EmployeeNote { get; set; } = "";
        public string ManagerNote { get; set; } = "";
        public string Status { get; set; } = "";
        public string SubmittedUtc { get; set; } = "";
        public bool IsPendingSync { get; set; }
        public string SyncState { get; set; } = "";
    }

    public sealed class WorkstationTimeClockSummaryRow
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
        public string DetailText { get; set; } = "";
        public string IconText { get; set; } = "";
        public string AccentBrush { get; set; } = "#7EABD9";
        public double ProgressValue { get; set; }
    }

    public sealed class WorkstationTimeClockActivityRow
    {
        public string Title { get; set; } = "";
        public string TimeText { get; set; } = "";
        public string StateText { get; set; } = "";
        public string DetailText { get; set; } = "";
        public string IconText { get; set; } = "";
        public string AccentBrush { get; set; } = "#7EABD9";
    }

    public sealed class WorkstationSyncQueueItem
    {
        public string QueueId { get; set; } = "";
        public string ClientEventId { get; set; } = "";
        public string ItemType { get; set; } = "";
        public string RemoteEmployeeId { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string ShopId { get; set; } = "";
        public string WorkstationId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string EffectiveUtc { get; set; } = "";
        public string CreatedLocalUtc { get; set; } = "";
        public string Note { get; set; } = "";
        public string RequestType { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public decimal? HoursRequested { get; set; }
        public int WorkOrderId { get; set; }
        public string WorkOrderNumber { get; set; } = "";
        public int OperationId { get; set; }
        public int OperationNumber { get; set; }
        public string OperationTitle { get; set; } = "";
        public string SyncStatus { get; set; } = "pending";
        public int RetryCount { get; set; }
        public string LastError { get; set; } = "";
        public string RemoteReceiptId { get; set; } = "";
    }
}
