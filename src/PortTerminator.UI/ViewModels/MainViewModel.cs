using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PortTerminator.Core.Helpers;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;
using PortTerminator.Core.Services;
using PortTerminator.UI.Services;

namespace PortTerminator.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPortScannerService _portScanner;
    private readonly IPortSnapshotComparer _snapshotComparer;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly INotificationService _notificationService;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private PortSnapshot? _previousSnapshot;
    private CancellationTokenSource? _monitorCts;
    private DispatcherTimer? _refreshTimer;

    [ObservableProperty] private NavigationPage _currentPage = NavigationPage.PortMonitor;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isDetailPanelOpen;
    [ObservableProperty] private PortEntry? _selectedPort;
    [ObservableProperty] private string _activeFilter = "全部";
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private LogLevel _toastLevel = LogLevel.Info;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _terminatePortText = string.Empty;
    [ObservableProperty] private bool _isReleasing;

    public bool CanReleasePort => !IsReleasing;

    public bool ShowPortDetailPanel => IsDetailPanelOpen && CurrentPage == NavigationPage.PortMonitor;
    public bool ShowPortMonitorToolbar => CurrentPage == NavigationPage.PortMonitor;
    public bool ShowPageHeader => CurrentPage != NavigationPage.PortMonitor;

    public string PageTitle => CurrentPage switch
    {
        NavigationPage.PortMonitor => "端口监控",
        NavigationPage.ProcessManager => "进程管理",
        NavigationPage.Rules => "规则中心",
        NavigationPage.Logs => "操作日志",
        NavigationPage.Settings => "设置",
        _ => string.Empty
    };

    public ObservableCollection<PortEntry> AllPorts { get; } = new();
    public ObservableCollection<PortEntry> FilteredPorts { get; } = new();
    public ObservableCollection<OperationLog> RecentLogs { get; } = new();
    public ObservableCollection<string> FilterTabs { get; } = new();

    public PortMonitorViewModel PortMonitor { get; }
    public ProcessManagerViewModel ProcessManager { get; }
    public RulesViewModel Rules { get; }
    public LogsViewModel Logs { get; }
    public SettingsViewModel Settings { get; }
    public PortDetailViewModel PortDetail { get; }

    public MainViewModel(
        IPortScannerService portScanner,
        IPortSnapshotComparer snapshotComparer,
        IProcessTerminationService terminationService,
        IWhitelistService whitelistService,
        ISettingsService settingsService,
        ILoggingService loggingService,
        INotificationService notificationService,
        IExportService exportService,
        IProcessManagerService processManagerService,
        IRuleService ruleService,
        IProcessInfoService processInfoService)
    {
        _portScanner = portScanner;
        _snapshotComparer = snapshotComparer;
        _settingsService = settingsService;
        _loggingService = loggingService;
        _notificationService = notificationService;

        PortDetail = new PortDetailViewModel(terminationService, whitelistService, loggingService, notificationService, processInfoService, this);
        PortMonitor = new PortMonitorViewModel(this);
        ProcessManager = new ProcessManagerViewModel(processManagerService, terminationService, loggingService, notificationService);
        Rules = new RulesViewModel(ruleService);
        Logs = new LogsViewModel(loggingService);
        Settings = new SettingsViewModel(settingsService, notificationService);

        if (notificationService is NotificationService ns)
            ns.ToastRequested += DisplayToast;
    }

    public async Task InitializeAsync()
    {
        Settings.LoadFromService();
        await RefreshLogsAsync();
    }

    partial void OnIsReleasingChanged(bool value) => OnPropertyChanged(nameof(CanReleasePort));

    [RelayCommand]
    private async Task ReleasePortByNumberAsync()
    {
        if (!TryParsePort(TerminatePortText, out var port)) return;
        await ExecutePortActionAsync(port, forceKill: false);
    }

    [RelayCommand]
    private async Task ForceKillPortByNumberAsync()
    {
        if (!TryParsePort(TerminatePortText, out var port)) return;
        await ExecutePortActionAsync(port, forceKill: true);
    }

    private bool TryParsePort(string text, out int port)
    {
        port = 0;
        if (int.TryParse(text.Trim(), out port) && port is > 0 and <= 65535)
            return true;

        ShowToast("请输入有效的端口号（1-65535）", LogLevel.Warning);
        return false;
    }

    private async Task ExecutePortActionAsync(int port, bool forceKill)
    {
        IsReleasing = true;
        try
        {
            var result = await _portScanner.ScanAsync();
            if (!result.Success || result.Data is null)
            {
                ShowToast(result.Message, LogLevel.Error);
                return;
            }

            var entry = result.Data.Entries
                .Where(e => e.Port == port && e.Pid > 0)
                .OrderByDescending(e => e.State is PortState.Listening or PortState.Bound)
                .ThenByDescending(e => e.State == PortState.Established)
                .FirstOrDefault();

            if (entry is null)
            {
                ShowToast($"未找到占用端口 {port} 的进程", LogLevel.Warning);
                return;
            }

            if (forceKill)
                PortDetail.PromptForceKill(entry);
            else
                await PortDetail.PromptReleaseAsync(entry);
        }
        finally
        {
            IsReleasing = false;
        }
    }

    public void StartMonitoring()
    {
        StopMonitoring();
        if (!_settingsService.Settings.RealTimeMonitoring) return;

        var interval = Math.Max(1, _settingsService.Settings.RefreshIntervalSeconds);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e) =>
        await ScanPortsAsync(silent: true);

    public void StopMonitoring()
    {
        if (_refreshTimer is not null)
        {
            _refreshTimer.Tick -= OnRefreshTimerTick;
            _refreshTimer.Stop();
            _refreshTimer = null;
        }
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    [RelayCommand]
    public async Task ScanPortsAsync(bool silent = false)
    {
        if (!await _scanLock.WaitAsync(0)) return;

        try
        {
            if (!silent)
                IsScanning = true;
            var result = await _portScanner.ScanAsync();
            if (!result.Success || result.Data is null)
            {
                if (!silent)
                    ShowToast(result.Message, LogLevel.Error);
                return;
            }

            ApplySnapshotDiff(result.Data);
            UpdateFilterTabs();
            ApplyFilter();

            if (!silent)
            {
                var listening = AllPorts.Count(p => p.State == PortState.Listening || p.State == PortState.Bound);
                var tcp = AllPorts.Count(p => p.Protocol == PortProtocol.TCP);
                var udp = AllPorts.Count(p => p.Protocol == PortProtocol.UDP);
                var high = AllPorts.Count(p => p.RiskLevel == RiskLevel.High);
                ShowToast($"扫描完成：发现 {listening} 个监听端口，{tcp} 个 TCP，{udp} 个 UDP，{high} 个高风险端口", LogLevel.Info);
                await AddLogAsync(LogLevel.Info, "端口扫描完成", null, string.Empty, null, "成功");
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                ShowToast($"扫描失败: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!silent)
                IsScanning = false;
            _scanLock.Release();
        }
    }

    private void ApplySnapshotDiff(PortSnapshot current)
    {
        var diff = _snapshotComparer.Compare(_previousSnapshot, current);
        var dict = AllPorts
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var removed in diff.Removed)
            if (dict.TryGetValue(removed.Key, out var existing))
                AllPorts.Remove(existing);

        foreach (var added in diff.Added)
            AllPorts.Add(added);

        foreach (var (old, updated) in diff.Updated)
        {
            if (dict.TryGetValue(old.Key, out var existing))
            {
                var index = AllPorts.IndexOf(existing);
                if (index >= 0)
                {
                    AllPorts[index] = updated;
                    if (SelectedPort?.Key == updated.Key)
                        SelectedPort = updated;
                }
            }
        }

        _previousSnapshot = current;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Search()
    {
        ApplyFilter();
        if (string.IsNullOrWhiteSpace(SearchText))
            return;

        var count = FilteredPorts.Count;
        ShowToast(count > 0 ? $"找到 {count} 条匹配结果" : "未找到匹配的端口", count > 0 ? LogLevel.Success : LogLevel.Warning);
    }

    partial void OnSelectedPortChanged(PortEntry? value)
    {
        IsDetailPanelOpen = value is not null;
        if (value is not null)
            PortDetail.Load(value);
    }

    private void ClearSelectionIfNeeded()
    {
        if (SelectedPort is null) return;
        if (!FilteredPorts.Any(p => p.Key == SelectedPort.Key))
        {
            SelectedPort = null;
            IsDetailPanelOpen = false;
        }
    }

    [RelayCommand]
    private void Navigate(NavigationPage page) => CurrentPage = page;

    partial void OnCurrentPageChanged(NavigationPage value)
    {
        if (value != NavigationPage.PortMonitor)
        {
            IsDetailPanelOpen = false;
            SelectedPort = null;
        }

        OnPropertyChanged(nameof(ShowPortDetailPanel));
        OnPropertyChanged(nameof(ShowPortMonitorToolbar));
        OnPropertyChanged(nameof(ShowPageHeader));
        OnPropertyChanged(nameof(PageTitle));
        _ = LoadPageDataAsync(value);
    }

    private CancellationTokenSource? _pageLoadCts;

    private async Task LoadPageDataAsync(NavigationPage page)
    {
        _pageLoadCts?.Cancel();
        _pageLoadCts?.Dispose();
        _pageLoadCts = new CancellationTokenSource();
        var token = _pageLoadCts.Token;

        try
        {
            switch (page)
            {
                case NavigationPage.ProcessManager:
                    await ProcessManager.LoadProcessesAsync(token);
                    break;
                case NavigationPage.Rules:
                    await Rules.LoadCommand.ExecuteAsync(null);
                    break;
                case NavigationPage.Logs:
                    await Logs.LoadCommand.ExecuteAsync(null);
                    break;
            }
        }
        catch (OperationCanceledException) { }
    }

    partial void OnIsDetailPanelOpenChanged(bool value) => OnPropertyChanged(nameof(ShowPortDetailPanel));

    [RelayCommand]
    private void SetFilter(string filter)
    {
        ActiveFilter = filter;
        ApplyFilter();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await ScanPortsAsync();

    [RelayCommand]
    private void CloseDetailPanel()
    {
        IsDetailPanelOpen = false;
        SelectedPort = null;
    }

    [RelayCommand]
    private async Task ClearLogsAsync()
    {
        await _loggingService.ClearAsync();
        RecentLogs.Clear();
    }

    public void ApplyFilter()
    {
        FilteredPorts.Clear();
        var query = AllPorts.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var s = SearchText.Trim();
            query = query.Where(p =>
                p.Port.ToString().Contains(s, StringComparison.OrdinalIgnoreCase)
                || p.LocalAddress.Contains(s, StringComparison.OrdinalIgnoreCase)
                || p.ProcessName.Contains(s, StringComparison.OrdinalIgnoreCase)
                || p.Pid.ToString().Contains(s, StringComparison.OrdinalIgnoreCase)
                || p.ExecutablePath.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        query = ActiveFilter switch
        {
            "监听中" => query.Where(p => p.State is PortState.Listening or PortState.Bound),
            "高风险" => query.Where(p => p.RiskLevel == RiskLevel.High),
            "系统进程" => query.Where(p => p.IsSystemProcess),
            "用户进程" => query.Where(p => !p.IsSystemProcess),
            _ => query
        };

        foreach (var item in query.OrderBy(p => p.Port))
            FilteredPorts.Add(item);

        ClearSelectionIfNeeded();
        UpdateFilterTabs();
    }

    private void UpdateFilterTabs()
    {
        FilterTabs.Clear();
        FilterTabs.Add($"全部 ({AllPorts.Count})");
        FilterTabs.Add($"监听中 ({AllPorts.Count(p => p.State is PortState.Listening or PortState.Bound)})");
        FilterTabs.Add($"高风险 ({AllPorts.Count(p => p.RiskLevel == RiskLevel.High)})");
        FilterTabs.Add($"系统进程 ({AllPorts.Count(p => p.IsSystemProcess)})");
        FilterTabs.Add($"用户进程 ({AllPorts.Count(p => !p.IsSystemProcess)})");
    }

    public async Task AddLogAsync(LogLevel level, string action, int? port, string processName, int? pid, string result)
    {
        var log = new OperationLog
        {
            Timestamp = DateTime.Now,
            Level = level,
            Action = action,
            Port = port,
            ProcessName = processName,
            Pid = pid,
            Result = result,
            Operator = Environment.UserName
        };
        await _loggingService.LogAsync(log);
        RecentLogs.Insert(0, log);
        while (RecentLogs.Count > 500) RecentLogs.RemoveAt(RecentLogs.Count - 1);
    }

    public async Task RefreshLogsAsync()
    {
        var logs = await _loggingService.GetRecentAsync(500);
        RecentLogs.Clear();
        foreach (var log in logs)
            RecentLogs.Add(log);
    }

    private DispatcherTimer? _toastTimer;

    public void ShowToast(string message, LogLevel level = LogLevel.Info) => DisplayToast(message, level);

    private void DisplayToast(string message, LogLevel level = LogLevel.Info)
    {
        ToastMessage = message;
        ToastLevel = level;
        IsToastVisible = true;

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            IsToastVisible = false;
            _toastTimer?.Stop();
        };
        _toastTimer.Start();
    }

    public void HideToast() => IsToastVisible = false;
}

public partial class PortDetailViewModel : ObservableObject
{
    private readonly IProcessTerminationService _terminationService;
    private readonly IWhitelistService _whitelistService;
    private readonly ILoggingService _loggingService;
    private readonly INotificationService _notificationService;
    private readonly MainViewModel _main;
    private readonly IProcessInfoService _processInfoService;
    private PortEntry? _entry;

    [ObservableProperty] private int _port;
    [ObservableProperty] private string _riskDisplay = string.Empty;
    [ObservableProperty] private RiskLevel _riskLevel;
    [ObservableProperty] private string _processName = string.Empty;
    [ObservableProperty] private int _pid;
    [ObservableProperty] private string _protocol = string.Empty;
    [ObservableProperty] private string _localAddress = string.Empty;
    [ObservableProperty] private string _executablePath = string.Empty;
    [ObservableProperty] private string _commandLine = string.Empty;
    [ObservableProperty] private string _startTimeDisplay = string.Empty;
    [ObservableProperty] private string _uptimeDisplay = string.Empty;
    [ObservableProperty] private string _networkStatus = string.Empty;
    [ObservableProperty] private string _signatureDisplay = string.Empty;
    [ObservableProperty] private bool _showConfirmDialog;
    [ObservableProperty] private string _confirmTitle = string.Empty;
    [ObservableProperty] private string _confirmMessage = string.Empty;
    [ObservableProperty] private string _confirmSubMessage = string.Empty;
    [ObservableProperty] private bool _dontShowAgain;
    [ObservableProperty] private string _pendingAction = string.Empty;

    public string ConfirmButtonText => PendingAction switch
    {
        "Kill" => "结束进程",
        "Release" => "确认释放",
        "ForceKill" => "强制终结",
        _ => "确认"
    };

    public PortDetailViewModel(
        IProcessTerminationService terminationService,
        IWhitelistService whitelistService,
        ILoggingService loggingService,
        INotificationService notificationService,
        IProcessInfoService processInfoService,
        MainViewModel main)
    {
        _terminationService = terminationService;
        _whitelistService = whitelistService;
        _loggingService = loggingService;
        _notificationService = notificationService;
        _processInfoService = processInfoService;
        _main = main;
    }

    public void Load(PortEntry entry)
    {
        ShowConfirmDialog = false;
        _entry = entry;
        Port = entry.Port;
        RiskDisplay = entry.RiskDisplay;
        RiskLevel = entry.RiskLevel;
        ProcessName = entry.ProcessName;
        Pid = entry.Pid;
        Protocol = entry.Protocol.ToString();
        LocalAddress = entry.LocalAddress;
        ExecutablePath = entry.ExecutablePath;
        CommandLine = entry.CommandLine;
        StartTimeDisplay = entry.ProcessStartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
        UptimeDisplay = PortStateHelper.FormatUptime(entry.ProcessStartTime);
        NetworkStatus = entry.IsExternallyAccessible ? "监听中 · 外部可访问" : "监听中 · 本地";
        SignatureDisplay = string.IsNullOrEmpty(entry.DigitalSignature) ? "加载中..." : entry.DigitalSignature;
        _ = LoadFullDetailsAsync(entry.Pid);
    }

    private async Task LoadFullDetailsAsync(int pid)
    {
        if (pid <= 0) return;
        var details = await _processInfoService.GetProcessDetailsAsync(pid);
        if (details is null || _entry?.Pid != pid) return;

        if (!string.IsNullOrEmpty(details.Identity.ExecutablePath))
            ExecutablePath = details.Identity.ExecutablePath;
        if (!string.IsNullOrEmpty(details.CommandLine))
            CommandLine = details.CommandLine;
        if (!string.IsNullOrEmpty(details.DigitalSignature))
            SignatureDisplay = details.DigitalSignature;
        if (details.Identity.StartTime.HasValue)
        {
            StartTimeDisplay = details.Identity.StartTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
            UptimeDisplay = PortStateHelper.FormatUptime(details.Identity.StartTime);
        }
    }

    private void ShowConfirm(string title, string message, string subMessage, string pendingAction)
    {
        _main.HideToast();
        ConfirmTitle = title;
        ConfirmMessage = message;
        ConfirmSubMessage = subMessage;
        PendingAction = pendingAction;
        OnPropertyChanged(nameof(ConfirmButtonText));
        ShowConfirmDialog = true;
    }

    [RelayCommand]
    private Task KillProcessAsync()
    {
        if (_entry is null) return Task.CompletedTask;
        ShowConfirm("确认结束进程", $"确认结束进程 {ProcessName}？", $"PID：{Pid}  占用端口：{Port}", "Kill");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ReleasePortAsync()
    {
        if (_entry is null) return;
        await PromptReleaseAsync(_entry);
    }

    public async Task PromptReleaseAsync(PortEntry entry)
    {
        Load(entry);
        var portsResult = await _terminationService.GetPortsByPidAsync(Pid);
        var ports = portsResult.Data ?? Array.Empty<int>();
        var portList = string.Join("、", ports);
        ShowConfirm(
            "确认释放端口",
            $"释放 {Port} 需要结束 PID {Pid} 的进程",
            ports.Count > 1
                ? $"{ProcessName} 当前占用：{portList}。这些端口也会同时关闭。"
                : "释放端口实际上需要结束对应进程。",
            "Release");
    }

    public void PromptForceKill(PortEntry entry)
    {
        Load(entry);
        ShowConfirm(
            "确认强制终结",
            $"确认强制终结占用 {entry.Port} 端口的进程吗？",
            "此操作可能导致数据丢失或服务异常，请谨慎操作！",
            "ForceKill");
    }

    [RelayCommand]
    private void ForceKill()
    {
        if (_entry is null) return;
        PromptForceKill(_entry);
    }

    [RelayCommand]
    private async Task AddToWhitelistAsync()
    {
        if (_entry is null) return;
        await _whitelistService.AddAsync(new WhitelistItem
        {
            Type = WhitelistType.ProcessName,
            Value = _entry.ProcessName,
            Description = $"端口 {_entry.Port}"
        });
        _main.ShowToast($"已将 {_entry.ProcessName} 加入白名单", LogLevel.Success);
        await _main.AddLogAsync(LogLevel.Success, $"加入白名单 {_entry.ProcessName}", _entry.Port, _entry.ProcessName, _entry.Pid, "成功");
    }

    [RelayCommand]
    private void CancelConfirm() => ShowConfirmDialog = false;

    [RelayCommand]
    private async Task ExecuteConfirmAsync()
    {
        if (_entry is null) return;
        ShowConfirmDialog = false;

        var details = await _processInfoService.GetProcessDetailsAsync(_entry.Pid);
        if (details is null)
        {
            _main.ShowToast("进程不存在或已退出，请重新扫描", LogLevel.Warning);
            return;
        }

        var identity = details.Identity;
        var force = PendingAction == "ForceKill";
        var killTree = force || PendingAction == "Release";
        var result = await _terminationService.TerminateAsync(identity, force, killTree);

        if (result.Success)
        {
            _main.ShowToast($"已释放 {Port} 端口", LogLevel.Success);
            await _main.AddLogAsync(LogLevel.Success, $"已释放 {Port} 端口", Port, ProcessName, Pid, "成功");
            await _main.RefreshLogsAsync();
        }
        else
        {
            _main.ShowToast(result.Message, result.ErrorCode == ServiceErrorCode.ProtectedProcess ? LogLevel.Warning : LogLevel.Error);
            await _main.AddLogAsync(LogLevel.Warning, PendingAction, Port, ProcessName, Pid, result.Message);
        }
    }
}

public partial class PortMonitorViewModel : ObservableObject
{
    public PortMonitorViewModel(MainViewModel main) => Main = main;
    public MainViewModel Main { get; }
}

public partial class ProcessManagerViewModel : ObservableObject
{
    private readonly IProcessManagerService _processManager;
    private readonly IProcessTerminationService _terminationService;
    private readonly ILoggingService _loggingService;
    private readonly INotificationService _notificationService;

    public ObservableCollection<ProcessListItem> Processes { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ProcessListItem? _selectedProcess;
    [ObservableProperty] private bool _isLoading;

    public ProcessManagerViewModel(
        IProcessManagerService processManager,
        IProcessTerminationService terminationService,
        ILoggingService loggingService,
        INotificationService notificationService)
    {
        _processManager = processManager;
        _terminationService = terminationService;
        _loggingService = loggingService;
        _notificationService = notificationService;
    }

    public async Task LoadProcessesAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var result = await _processManager.GetAllProcessesAsync(cancellationToken);
            Processes.Clear();
            if (result.Success && result.Data is not null)
            {
                foreach (var p in result.Data)
                    Processes.Add(p);
            }
            else if (!result.Success)
            {
                _notificationService.ShowToast(result.Message, LogLevel.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public Task RefreshAsync() => LoadProcessesAsync();

    partial void OnSearchTextChanged(string value)
    {
        // Filtering handled in view via CollectionView
    }

    [RelayCommand]
    private async Task KillSelectedAsync()
    {
        if (SelectedProcess is null) return;
        var result = await _terminationService.TerminateAsync(SelectedProcess.Identity, false, false);
        _notificationService.ShowToast(result.Message, result.Success ? LogLevel.Success : LogLevel.Error);
        if (result.Success) await LoadProcessesAsync();
    }
}

public partial class RulesViewModel : ObservableObject
{
    private readonly IRuleService _ruleService;
    public ObservableCollection<PortRule> Rules { get; } = new();

    public RulesViewModel(IRuleService ruleService)
    {
        _ruleService = ruleService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await _ruleService.LoadAsync();
        Rules.Clear();
        foreach (var rule in _ruleService.Rules)
            Rules.Add(rule);
    }

    [RelayCommand]
    private async Task DeleteRuleAsync(PortRule rule)
    {
        await _ruleService.DeleteAsync(rule.Id);
        await LoadAsync();
    }
}

public partial class LogsViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    public ObservableCollection<OperationLog> Logs { get; } = new();
    [ObservableProperty] private string _searchText = string.Empty;

    public LogsViewModel(ILoggingService loggingService) => _loggingService = loggingService;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var logs = await _loggingService.GetRecentAsync(500);
        Logs.Clear();
        foreach (var log in logs)
            Logs.Add(log);
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly Action? _onSettingsSaved;

    [ObservableProperty] private bool _realTimeMonitoring = true;
    [ObservableProperty] private int _refreshInterval = 3;
    [ObservableProperty] private bool _confirmBeforeKill = true;
    [ObservableProperty] private bool _confirmBeforeForceKill = true;
    [ObservableProperty] private bool _systemProcessProtection = true;
    [ObservableProperty] private bool _highRiskAlert = true;
    [ObservableProperty] private bool _autoScanOnStart = true;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _showSaveStatus;
    [ObservableProperty] private bool _saveSucceeded;
    [ObservableProperty] private string _saveStatusMessage = string.Empty;

    public bool CanSave => !IsSaving;
    public string SaveButtonText => IsSaving ? "保存中..." : "保存设置";

    public int[] RefreshIntervals { get; } = { 1, 3, 5, 10, 30 };

    public SettingsViewModel(ISettingsService settingsService, INotificationService notificationService, Action? onSettingsSaved = null)
    {
        _settingsService = settingsService;
        _notificationService = notificationService;
        _onSettingsSaved = onSettingsSaved;
    }

    public void LoadFromService()
    {
        var s = _settingsService.Settings;
        RealTimeMonitoring = s.RealTimeMonitoring;
        RefreshInterval = s.RefreshIntervalSeconds;
        ConfirmBeforeKill = s.ConfirmBeforeKill;
        ConfirmBeforeForceKill = s.ConfirmBeforeForceKill;
        SystemProcessProtection = s.SystemProcessProtection;
        HighRiskAlert = s.HighRiskAlert;
        AutoScanOnStart = s.AutoScanOnStart;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving) return;

        IsSaving = true;
        ShowSaveStatus = false;
        try
        {
            var s = _settingsService.Settings;
            s.RealTimeMonitoring = RealTimeMonitoring;
            s.RefreshIntervalSeconds = RefreshInterval;
            s.ConfirmBeforeKill = ConfirmBeforeKill;
            s.ConfirmBeforeForceKill = ConfirmBeforeForceKill;
            s.SystemProcessProtection = SystemProcessProtection;
            s.HighRiskAlert = HighRiskAlert;
            s.AutoScanOnStart = AutoScanOnStart;
            await _settingsService.SaveAsync();
            _onSettingsSaved?.Invoke();

            SaveSucceeded = true;
            SaveStatusMessage = "✓ 设置保存成功";
            ShowSaveStatus = true;
            _notificationService.ShowToast("设置保存成功", LogLevel.Success);
        }
        catch (Exception ex)
        {
            SaveSucceeded = false;
            SaveStatusMessage = $"✕ 设置保存失败：{ex.Message}";
            ShowSaveStatus = true;
            _notificationService.ShowToast($"设置保存失败：{ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsSaving = false;
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(SaveButtonText));
        }
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(SaveButtonText));
    }
}
