using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
        public string StatusChipText { get; set; } = "AVAILABLE";
        public string StatusChipForeground { get; set; } = "#AAB6C8";
        public string StatusChipBackground { get; set; } = "#151C2836";
        public string StatusChipBorder { get; set; } = "#2F425B";
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
        public bool IsSelected { get; set; }
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
        public string QueueCardBackgroundBrush => IsSelected ? "#FF142033" : "#FF101824";
        public string QueueCardBorderBrush => IsSelected ? "#FF38D5FF" : IsCurrentJob ? "#FF3F6EB7" : "#FF243246";
        public string StatusBadgeText => NormalizeStatus(Status);
        public string StatusBadgeBackgroundBrush => StatusBadgeText switch
        {
            "READY" => "#162BD576",
            "IN PROGRESS" => "#1E38D5FF",
            "WAITING" => "#1EA970FF",
            "BLOCKED" => "#1EFF5A6A",
            "COMPLETED" => "#183F6EB7",
            _ => "#1A6F7C8F"
        };
        public string StatusBadgeBorderBrush => StatusBadgeText switch
        {
            "READY" => "#662BD576",
            "IN PROGRESS" => "#6638D5FF",
            "WAITING" => "#66A970FF",
            "BLOCKED" => "#66FF5A6A",
            "COMPLETED" => "#663F6EB7",
            _ => "#66243246"
        };
        public string StatusBadgeForegroundBrush => StatusBadgeText switch
        {
            "READY" => "#FF2BD576",
            "IN PROGRESS" => "#FF38D5FF",
            "WAITING" => "#FFA970FF",
            "BLOCKED" => "#FFFF5A6A",
            "COMPLETED" => "#FF8FD7FF",
            _ => "#FFAAB6C8"
        };
        public string DueDateDisplay => string.IsNullOrWhiteSpace(DueDate) ? "No due date" : $"Due {DueDate}";
        public string RevisionDisplay => string.IsNullOrWhiteSpace(Revision) ? "Rev -" : $"Rev {Revision}";
        public string QuantityDisplay => Quantity > 0 ? $"Qty {Quantity}" : "Qty --";
        public string ProgressPercentDisplay => $"{Math.Max(0, ProgressPercent)}%";
        public string AssignmentChipText => string.IsNullOrWhiteSpace(AssignmentLabel)
            ? (IsAssigned ? "ASSIGNED" : IsBackupJob ? "AVAILABLE" : "QUEUE")
            : AssignmentLabel.ToUpperInvariant();
        public string AssignmentChipBackgroundBrush => IsAssigned ? "#1438D5FF" : "#140B111A";
        public string AssignmentChipBorderBrush => IsAssigned ? "#6638D5FF" : "#66243246";
        public string AssignmentChipForegroundBrush => IsAssigned ? "#FF38D5FF" : "#FFAAB6C8";

        private static string NormalizeStatus(string? status)
        {
            var value = (status ?? "").Trim();
            if (value.Length == 0) return "PENDING";
            if (value.IndexOf("progress", StringComparison.OrdinalIgnoreCase) >= 0) return "IN PROGRESS";
            if (value.IndexOf("ready", StringComparison.OrdinalIgnoreCase) >= 0) return "READY";
            if (value.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0) return "WAITING";
            if (value.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0) return "BLOCKED";
            if (value.IndexOf("complete", StringComparison.OrdinalIgnoreCase) >= 0) return "COMPLETED";
            return value.ToUpperInvariant();
        }
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
        public bool IsSelected { get; set; }
        public string SetupMinutesEstimate { get; set; } = "";
        public string CycleMinutesEstimate { get; set; } = "";
        public bool IsPaused => ContainsStatus("stop") || ContainsStatus("pause");
        public bool IsInProgress => ContainsStatus("progress") || CanStop;
        public bool IsCompleted => ContainsStatus("complete") || !string.IsNullOrWhiteSpace(CompletedUtc);
        public bool IsReady => ContainsStatus("ready") || CanStart;
        public bool IsBlocked => ContainsStatus("block");
        public bool IsWaiting => ContainsStatus("wait") || ContainsStatus("hold") || IsPaused;
        public bool IsPending => !IsInProgress && !IsCompleted && !IsReady && !IsBlocked && !IsWaiting;
        public string DisplayStatus => IsPaused ? "Paused" : Status;
        public bool IsInspection => ContainsStatus("inspection") ||
            (Title ?? "").IndexOf("inspection", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (Department ?? "").IndexOf("quality", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (Department ?? "").IndexOf("qa", StringComparison.OrdinalIgnoreCase) >= 0;
        public string OperationNumberDisplay => OperationNumber > 0 ? OperationNumber.ToString("000", CultureInfo.InvariantCulture) : "--";
        public string WorkCenterDisplay => string.IsNullOrWhiteSpace(WorkCenter) ? "Work center pending" : WorkCenter;
        public string MachineDisplay => string.IsNullOrWhiteSpace(MachineName) ? WorkCenterDisplay : MachineName;
        public string DepartmentDisplay => string.IsNullOrWhiteSpace(Department) ? "Department pending" : Department;
        public string SetupMinutesDisplay => BuildMinutesDisplay("Setup", SetupMinutesEstimate);
        public string CycleMinutesDisplay => BuildMinutesDisplay("Cycle", CycleMinutesEstimate);
        public string StartedDisplay => FormatUtcDisplay(StartedUtc, "Started");
        public string CompletedDisplay => FormatUtcDisplay(CompletedUtc, "Completed");
        public string TimelineLineBrush => IsSelected ? "#FF38D5FF" : "#FF243246";
        public string TimelineNodeFillBrush => IsSelected ? "#FF38D5FF" : "#FF101824";
        public string TimelineNodeBorderBrush => IsSelected ? "#FF38D5FF" : StatusBadgeBorderBrush;
        public string TimelineNodeTextBrush => IsSelected ? "#FF070A0F" : "#FFF4F7FB";
        public string RowBackgroundBrush => IsSelected ? "#FF142033" : "#FF101824";
        public string RowBorderBrush => IsSelected ? "#FF38D5FF" : "#FF243246";
        public string StatusBadgeText => NormalizeStatus(DisplayStatus);
        public string StatusBadgeBackgroundBrush => StatusBadgeText switch
        {
            "READY" => "#162BD576",
            "IN PROGRESS" => "#1638D5FF",
            "WAITING" => "#1EA970FF",
            "BLOCKED" => "#1EFF5A6A",
            "COMPLETED" => "#183F6EB7",
            _ => "#1A6F7C8F"
        };
        public string StatusBadgeBorderBrush => StatusBadgeText switch
        {
            "READY" => "#662BD576",
            "IN PROGRESS" => "#6638D5FF",
            "WAITING" => "#66A970FF",
            "BLOCKED" => "#66FF5A6A",
            "COMPLETED" => "#663F6EB7",
            _ => "#66243246"
        };
        public string StatusTextBrush => StatusBadgeText switch
        {
            "READY" => "#FF2BD576",
            "IN PROGRESS" => "#FF38D5FF",
            "WAITING" => "#FFA970FF",
            "BLOCKED" => "#FFFF5A6A",
            "COMPLETED" => "#FF8FD7FF",
            _ => "#FFAAB6C8"
        };

        private bool ContainsStatus(string value)
            => (Status ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string NormalizeStatus(string? status)
        {
            var value = (status ?? "").Trim();
            if (value.Length == 0) return "PENDING";
            if (value.IndexOf("pause", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0) return "WAITING";
            if (value.IndexOf("progress", StringComparison.OrdinalIgnoreCase) >= 0) return "IN PROGRESS";
            if (value.IndexOf("ready", StringComparison.OrdinalIgnoreCase) >= 0) return "READY";
            if (value.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("hold", StringComparison.OrdinalIgnoreCase) >= 0) return "WAITING";
            if (value.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0) return "BLOCKED";
            if (value.IndexOf("complete", StringComparison.OrdinalIgnoreCase) >= 0) return "COMPLETED";
            return value.ToUpperInvariant();
        }

        private static string FormatUtcDisplay(string utc, string prefix)
        {
            if (string.IsNullOrWhiteSpace(utc))
                return "";

            return DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? $"{prefix} {parsed.ToLocalTime():g}"
                : $"{prefix} {utc}";
        }

        private static string BuildMinutesDisplay(string label, string value)
            => string.IsNullOrWhiteSpace(value) ? $"{label} --" : $"{label} {value}";
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

    public sealed class WorkstationQuantityEventEntry
    {
        public string EventId { get; set; } = "";
        public string EventType { get; set; } = "";
        public double Quantity { get; set; }
        public double? GoodQuantity { get; set; }
        public double? ScrapQuantity { get; set; }
        public string EmployeeName { get; set; } = "";
        public string Source { get; set; } = "";
        public string Notes { get; set; } = "";
        public string CreatedUtc { get; set; } = "";
        public string EventTypeDisplay => string.IsNullOrWhiteSpace(EventType) ? "EVENT" : EventType.Replace("_", " ").ToUpperInvariant();
        public string QuantityDisplay => Quantity.ToString("0.####", CultureInfo.InvariantCulture);
        public string GoodQuantityDisplay => GoodQuantity.HasValue ? GoodQuantity.Value.ToString("0.####", CultureInfo.InvariantCulture) : "--";
        public string ScrapQuantityDisplay => ScrapQuantity.HasValue ? ScrapQuantity.Value.ToString("0.####", CultureInfo.InvariantCulture) : "--";
        public string SourceDisplay => string.IsNullOrWhiteSpace(Source) ? "Workstation" : Source;
        public string CreatedDisplay
        {
            get
            {
                if (!DateTime.TryParse(CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                    return CreatedUtc ?? "";
                return parsed.ToLocalTime().ToString("g", CultureInfo.InvariantCulture);
            }
        }
        public string SummaryLine
        {
            get
            {
                var parts = new List<string> { $"Qty {QuantityDisplay}" };
                if (GoodQuantity.HasValue)
                    parts.Add($"Good {GoodQuantityDisplay}");
                if (ScrapQuantity.HasValue)
                    parts.Add($"Scrap {ScrapQuantityDisplay}");
                return string.Join("  |  ", parts);
            }
        }
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
        public WorkstationEmployeeBenefitsSummary Benefits { get; set; } = new WorkstationEmployeeBenefitsSummary();
        public List<WorkstationPunchRecord> RecentPunches { get; set; } = new List<WorkstationPunchRecord>();
        public List<WorkstationTimeOffRequestRecord> RecentTimeOffRequests { get; set; } = new List<WorkstationTimeOffRequestRecord>();
    }

    public sealed class WorkstationEmployeeBenefitsSummary
    {
        public string PolicyName { get; set; } = "";
        public bool IsEligible { get; set; }
        public string EligibleDate { get; set; } = "";
        public string EligibilitySummary { get; set; } = "";
        public string AccrualSummary { get; set; } = "";
        public string CarryoverSummary { get; set; } = "";
        public string ResetDate { get; set; } = "";
        public decimal TotalPaidHours { get; set; }
        public List<WorkstationBenefitBucket> Buckets { get; set; } = new List<WorkstationBenefitBucket>();
        public List<WorkstationBenefitLedgerEntry> RecentLedger { get; set; } = new List<WorkstationBenefitLedgerEntry>();
    }

    public sealed class WorkstationBenefitBucket
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public decimal AvailableHours { get; set; }
        public decimal UsedHours { get; set; }
        public decimal PendingHours { get; set; }
        public decimal AccruedHours { get; set; }
        public decimal CarryoverHours { get; set; }
        public string ExpiresOn { get; set; } = "";
    }

    public sealed class WorkstationBenefitLedgerEntry
    {
        public string Date { get; set; } = "";
        public string Type { get; set; } = "";
        public string Label { get; set; } = "";
        public decimal Hours { get; set; }
        public string Note { get; set; } = "";
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

    public sealed class WorkstationShiftTimelineEntry
    {
        public int SequenceNumber { get; set; }
        public string Title { get; set; } = "";
        public string TimestampDisplay { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string AccentBrush { get; set; } = "#38D5FF";
        public string BackgroundBrush { get; set; } = "#101824";
        public string BorderBrush { get; set; } = "#243246";
        public string LeftConnectorBrush { get; set; } = "#243246";
        public string RightConnectorBrush { get; set; } = "#243246";
        public double LeftConnectorOpacity { get; set; } = 0.5;
        public double RightConnectorOpacity { get; set; } = 0.5;
        public bool IsFirst { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsLast { get; set; }
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
