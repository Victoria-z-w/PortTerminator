using System.Diagnostics;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;

namespace PortTerminator.Windows.Services;

public class ProcessManagerService : IProcessManagerService
{
    private readonly IPortScannerService _portScannerService;

    public ProcessManagerService(
        IProcessInfoService processInfoService,
        IPortScannerService portScannerService,
        IRiskAssessmentService riskAssessmentService)
    {
        _portScannerService = portScannerService;
    }

    public async Task<ServiceResult<IReadOnlyList<ProcessListItem>>> GetAllProcessesAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            try
            {
                var portCounts = await _portScannerService.GetPortCountsByPidAsync(cancellationToken);

                var items = new List<ProcessListItem>();
                foreach (var process in Process.GetProcesses())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var name = process.ProcessName;
                        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            name += ".exe";

                        items.Add(new ProcessListItem
                        {
                            ProcessName = name,
                            Pid = process.Id,
                            Owner = string.Empty,
                            MemoryBytes = TryGetMemory(process),
                            ThreadCount = TryGetThreadCount(process),
                            StartTime = TryGetStartTime(process),
                            ExecutablePath = TryGetPath(process),
                            PortCount = portCounts.GetValueOrDefault(process.Id),
                            RiskLevel = RiskLevel.Low
                        });
                    }
                    catch { }
                    finally
                    {
                        process.Dispose();
                    }
                }

                return ServiceResult<IReadOnlyList<ProcessListItem>>.Ok(
                    items.OrderBy(i => i.ProcessName).ToList());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ServiceResult<IReadOnlyList<ProcessListItem>>.Fail(ServiceErrorCode.Unknown, ex.Message, ex);
            }
        }, cancellationToken);
    }

    private static long TryGetMemory(Process process)
    {
        try { return process.WorkingSet64; }
        catch { return 0; }
    }

    private static int TryGetThreadCount(Process process)
    {
        try { return process.Threads.Count; }
        catch { return 0; }
    }

    private static DateTime? TryGetStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return null; }
    }

    private static string TryGetPath(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return string.IsNullOrEmpty(path) ? "不可访问" : path;
        }
        catch
        {
            return "不可访问";
        }
    }
}
