namespace PortTerminator.Core.Models;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public class OperationLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Action { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public int? Pid { get; set; }
    public string Result { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
}

public enum WhitelistType
{
    Port,
    ProcessName,
    ExecutablePath
}

public class WhitelistItem
{
    public long Id { get; set; }
    public WhitelistType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsEnabled { get; set; } = true;
}

public class PortRule
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProcessNameContains { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string ListenAddress { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Medium;
    public string Message { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public class AppSettings
{
    public bool AutoStart { get; set; }
    public bool StartMinimized { get; set; }
    public bool RealTimeMonitoring { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 3;
    public bool AutoScanOnStart { get; set; } = true;
    public bool ConfirmBeforeKill { get; set; } = true;
    public bool ConfirmBeforeForceKill { get; set; } = true;
    public bool SystemProcessProtection { get; set; } = true;
    public bool HighRiskAlert { get; set; } = true;
    public bool DesktopNotification { get; set; } = true;
    public bool HighRiskNotification { get; set; } = true;
    public bool AutoSaveLog { get; set; } = true;
    public int LogRetentionDays { get; set; } = 30;
    public bool DontShowForceKillConfirm { get; set; }
}

public enum NavigationPage
{
    PortMonitor,
    ProcessManager,
    Rules,
    Logs,
    Settings
}
