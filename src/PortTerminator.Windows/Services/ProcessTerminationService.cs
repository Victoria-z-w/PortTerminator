using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;
using PortTerminator.Core.Services;

namespace PortTerminator.Windows.Services;

public class ProcessTerminationService : IProcessTerminationService
{
    private readonly IProtectedProcessService _protectedProcessService;
    private readonly IProcessInfoService _processInfoService;
    private readonly IElevatedClient _elevatedClient;
    private readonly IAdminHelper _adminHelper;

    public ProcessTerminationService(
        IProtectedProcessService protectedProcessService,
        IProcessInfoService processInfoService,
        IElevatedClient elevatedClient,
        IAdminHelper adminHelper)
    {
        _protectedProcessService = protectedProcessService;
        _processInfoService = processInfoService;
        _elevatedClient = elevatedClient;
        _adminHelper = adminHelper;
    }

    public async Task<ServiceResult> TerminateAsync(
        ProcessIdentity identity, bool forceKill, bool killTree, CancellationToken cancellationToken = default)
    {
        var protection = _protectedProcessService.ValidateTermination(identity, forceKill);
        if (!protection.Success)
            return protection;

        var verifyResult = await VerifyIdentityAsync(identity, cancellationToken);
        if (!verifyResult.Success)
            return verifyResult;

        var current = verifyResult.Data!;

        var localResult = await KillLocallyAsync(current, forceKill, killTree, cancellationToken);
        if (localResult.Success)
        {
            _processInfoService.InvalidateCache(current.Pid);
            return localResult;
        }

        if (localResult.ErrorCode != ServiceErrorCode.AccessDenied)
            return localResult;

        var request = new ElevatedRequest
        {
            Command = killTree ? ElevatedCommand.KillProcessTree : ElevatedCommand.KillProcess,
            Pid = current.Pid,
            ProcessName = current.ProcessName,
            ProcessStartTime = current.StartTime,
            SessionToken = _elevatedClient.GenerateSessionToken()
        };

        var response = await _elevatedClient.SendRequestAsync(request, cancellationToken);
        if (!response.Success)
            return ServiceResult.Fail(response.ErrorCode, response.Message);

        if (response.Data is null || !response.Data.Success)
            return ServiceResult.Fail(
                response.Data?.ErrorCode ?? ServiceErrorCode.Unknown,
                response.Data?.Message ?? "操作失败");

        _processInfoService.InvalidateCache(current.Pid);
        return ServiceResult.Ok($"已结束 {current.ProcessName}");
    }

    public async Task<ServiceResult<IReadOnlyList<int>>> GetPortsByPidAsync(int pid, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var scanner = new PortScannerService(
                    _processInfoService,
                    new RiskAssessmentService(),
                    new EmptyWhitelistService(),
                    new EmptyRuleService());

                var result = scanner.ScanAsync(cancellationToken).GetAwaiter().GetResult();
                if (!result.Success || result.Data is null)
                    return ServiceResult<IReadOnlyList<int>>.Fail(ServiceErrorCode.Unknown, "扫描失败");

                var ports = result.Data.Entries
                    .Where(e => e.Pid == pid)
                    .Select(e => e.Port)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();

                return ServiceResult<IReadOnlyList<int>>.Ok(ports);
            }
            catch (Exception ex)
            {
                return ServiceResult<IReadOnlyList<int>>.Fail(ServiceErrorCode.Unknown, ex.Message, ex);
            }
        }, cancellationToken);
    }

    private async Task<ServiceResult<ProcessIdentity>> VerifyIdentityAsync(
        ProcessIdentity identity, CancellationToken cancellationToken)
    {
        var details = await _processInfoService.GetProcessDetailsAsync(identity.Pid, cancellationToken);
        if (details is null)
            return ServiceResult<ProcessIdentity>.Fail(ServiceErrorCode.ProcessNotFound, "进程不存在或已退出");

        var current = details.Identity;
        if (!string.Equals(current.ProcessName, identity.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<ProcessIdentity>.Fail(ServiceErrorCode.ProcessChanged,
                "目标进程状态已经发生变化，为防止误结束其他进程，请重新扫描。");
        }

        if (identity.StartTime.HasValue && current.StartTime.HasValue
            && identity.StartTime != current.StartTime)
        {
            return ServiceResult<ProcessIdentity>.Fail(ServiceErrorCode.ProcessChanged,
                "目标进程状态已经发生变化，为防止误结束其他进程，请重新扫描。");
        }

        return ServiceResult<ProcessIdentity>.Ok(current);
    }

    private static async Task<ServiceResult> KillLocallyAsync(
        ProcessIdentity identity, bool forceKill, bool killTree, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(identity.Pid);

                if (forceKill || killTree)
                {
                    process.Kill(entireProcessTree: killTree);
                }
                else if (process.MainWindowHandle != IntPtr.Zero)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000))
                        process.Kill(entireProcessTree: false);
                }
                else
                {
                    process.Kill(entireProcessTree: false);
                }

                process.WaitForExit(5000);
                return ServiceResult.Ok($"已结束 {identity.ProcessName}");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or Win32Exception)
            {
                return ServiceResult.Fail(ServiceErrorCode.AccessDenied,
                    "无法结束进程：权限不足。请尝试以管理员身份运行，或使用「强制终结」。");
            }
            catch (ArgumentException)
            {
                return ServiceResult.Fail(ServiceErrorCode.ProcessNotFound, "进程不存在或已退出");
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail(ServiceErrorCode.Unknown, ex.Message, ex);
            }
        }, cancellationToken);
    }

    private class EmptyWhitelistService : IWhitelistService
    {
        public IReadOnlyList<WhitelistItem> Items => Array.Empty<WhitelistItem>();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddAsync(WhitelistItem item, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(long id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsWhitelisted(PortEntry entry) => false;
    }

    private class EmptyRuleService : IRuleService
    {
        public IReadOnlyList<PortRule> Rules => Array.Empty<PortRule>();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddAsync(PortRule rule, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(PortRule rule, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public class ElevatedClient : IElevatedClient
{
    private string? _sessionToken;

    public string GenerateSessionToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        _sessionToken = Convert.ToBase64String(bytes);
        return _sessionToken;
    }

    public async Task<ServiceResult<ElevatedResponse>> SendRequestAsync(
        ElevatedRequest request, CancellationToken cancellationToken = default)
    {
        request.SessionToken = _sessionToken ?? GenerateSessionToken();

        var elevatedExe = FindElevatedExe();
        if (elevatedExe is null)
            return ServiceResult<ElevatedResponse>.Fail(ServiceErrorCode.InvalidRequest, "找不到提权组件 PortTerminator.Elevated.exe");

        var pipeName = $"PortTerminator.Elevated.{Environment.ProcessId}";
        request.SessionToken = _sessionToken!;

        var startInfo = new ProcessStartInfo
        {
            FileName = elevatedExe,
            Arguments = $"--pipe {pipeName} --token \"{request.SessionToken}\"",
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return ServiceResult<ElevatedResponse>.Fail(ServiceErrorCode.AccessDenied, "用户取消了 UAC 提权请求");
        }

        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            for (var attempt = 0; attempt < 60; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await client.ConnectAsync(500, cancellationToken);
                    break;
                }
                catch (TimeoutException) when (attempt < 59)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }

            if (!client.IsConnected)
                return ServiceResult<ElevatedResponse>.Fail(ServiceErrorCode.Unknown, "提权组件连接超时，请重试");

            var json = JsonSerializer.Serialize(request);
            var bytes = Encoding.UTF8.GetBytes(json);
            await client.WriteAsync(bytes, cancellationToken);
            await client.FlushAsync(cancellationToken);

            var buffer = new byte[4096];
            var read = await client.ReadAsync(buffer, cancellationToken);
            var responseJson = Encoding.UTF8.GetString(buffer, 0, read);
            var response = JsonSerializer.Deserialize<ElevatedResponse>(responseJson);

            if (response is null)
                return ServiceResult<ElevatedResponse>.Fail(ServiceErrorCode.Unknown, "提权组件无响应");

            return ServiceResult<ElevatedResponse>.Ok(response);
        }
        catch (Exception ex)
        {
            return ServiceResult<ElevatedResponse>.Fail(ServiceErrorCode.Unknown, ex.Message, ex);
        }
    }

    private static string? FindElevatedExe()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "PortTerminator.Elevated.exe");
        return File.Exists(path) ? path : null;
    }
}
