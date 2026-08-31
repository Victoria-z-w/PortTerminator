using PortTerminator.Core.Models;

namespace PortTerminator.Core.Interfaces;

public interface IPortScannerService
{
    Task<ServiceResult<PortSnapshot>> ScanAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<int, int>> GetPortCountsByPidAsync(CancellationToken cancellationToken = default);
}

public interface IProcessInfoService
{
    Task<ProcessDetails?> GetProcessDetailsAsync(int pid, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, ProcessDetails>> GetScanDetailsBatchAsync(IEnumerable<int> pids, CancellationToken cancellationToken = default);
    void InvalidateCache(int pid);
    void ClearCache();
}

public interface IRiskAssessmentService
{
    RiskLevel Assess(PortEntry entry, IEnumerable<WhitelistItem> whitelist, IEnumerable<PortRule> rules);
    string GetRiskDisplay(RiskLevel level);
}

public interface IProtectedProcessService
{
    ServiceResult ValidateTermination(ProcessIdentity identity, bool forceKill);
    bool IsProtected(ProcessIdentity identity);
}

public interface IPortSnapshotComparer
{
    PortSnapshotDiff Compare(PortSnapshot? previous, PortSnapshot current);
    IReadOnlyList<PortChangeEvent> DetectChanges(PortSnapshotDiff diff);
}

public interface IProcessTerminationService
{
    Task<ServiceResult> TerminateAsync(ProcessIdentity identity, bool forceKill, bool killTree, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<int>>> GetPortsByPidAsync(int pid, CancellationToken cancellationToken = default);
}

public interface IElevatedClient
{
    Task<ServiceResult<ElevatedResponse>> SendRequestAsync(ElevatedRequest request, CancellationToken cancellationToken = default);
    string GenerateSessionToken();
}

public interface ILoggingService
{
    Task LogAsync(OperationLog log, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationLog>> GetRecentAsync(int count = 100, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    AppSettings Settings { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public interface IWhitelistService
{
    IReadOnlyList<WhitelistItem> Items { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task AddAsync(WhitelistItem item, CancellationToken cancellationToken = default);
    Task RemoveAsync(long id, CancellationToken cancellationToken = default);
    bool IsWhitelisted(PortEntry entry);
}

public interface IRuleService
{
    IReadOnlyList<PortRule> Rules { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PortRule rule, CancellationToken cancellationToken = default);
    Task UpdateAsync(PortRule rule, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public interface IDatabaseService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IExportService
{
    Task<ServiceResult<string>> ExportPortsAsync(IEnumerable<PortEntry> ports, string format, string filePath, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    void ShowToast(string message, LogLevel level = LogLevel.Info);
    void ShowDesktopNotification(string title, string message);
}

public interface IAdminHelper
{
    bool IsRunningAsAdmin();
    void RestartAsAdmin();
}

public interface ISignatureCacheService
{
    (string Publisher, SignatureStatus Status) GetOrQuery(string executablePath);
}

public interface IProcessManagerService
{
    Task<ServiceResult<IReadOnlyList<ProcessListItem>>> GetAllProcessesAsync(CancellationToken cancellationToken = default);
}

public class ProcessListItem
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public string Owner { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public long MemoryBytes { get; set; }
    public int ThreadCount { get; set; }
    public DateTime? StartTime { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public int PortCount { get; set; }
    public string DigitalSignature { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
    public ProcessIdentity Identity => new()
    {
        Pid = Pid,
        ProcessName = ProcessName,
        StartTime = StartTime,
        ExecutablePath = ExecutablePath
    };
}
