using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;
using PortTerminator.Core.Services;
using PortTerminator.Windows.Helpers;

namespace PortTerminator.Windows.Services;

public class ProcessInfoService : IProcessInfoService
{
    private readonly ConcurrentDictionary<string, ProcessDetails> _cache = new();
    private readonly ISignatureCacheService _signatureCache;

    public ProcessInfoService(ISignatureCacheService signatureCache)
    {
        _signatureCache = signatureCache;
    }

    public async Task<IReadOnlyDictionary<int, ProcessDetails>> GetScanDetailsBatchAsync(
        IEnumerable<int> pids, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var distinctPids = pids.Where(p => p > 0).Distinct().ToList();
            if (distinctPids.Count == 0)
                return (IReadOnlyDictionary<int, ProcessDetails>)new Dictionary<int, ProcessDetails>();

            var commandLines = LoadCommandLineMap(cancellationToken);
            var result = new ConcurrentDictionary<int, ProcessDetails>();

            Parallel.ForEach(distinctPids, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(16, Environment.ProcessorCount * 2),
                CancellationToken = cancellationToken
            }, pid =>
            {
                var details = BuildScanDetails(pid, commandLines);
                if (details is not null)
                    result[pid] = details;
            });

            return (IReadOnlyDictionary<int, ProcessDetails>)result.ToDictionary(kv => kv.Key, kv => kv.Value);
        }, cancellationToken);
    }

    public async Task<ProcessDetails?> GetProcessDetailsAsync(int pid, CancellationToken cancellationToken = default)
    {
        if (pid <= 0) return null;

        return await Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                var startTime = SafeGetStartTime(process);
                var cacheKey = $"{pid}:{startTime?.Ticks ?? 0}";

                if (_cache.TryGetValue(cacheKey, out var cached))
                    return cached;

                var name = process.ProcessName;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name += ".exe";

                var path = TryGetExecutablePath(process);
                var details = new ProcessDetails
                {
                    Identity = new ProcessIdentity
                    {
                        Pid = pid,
                        ProcessName = name,
                        StartTime = startTime,
                        ExecutablePath = path
                    },
                    CommandLine = TryGetCommandLine(pid),
                    Owner = TryGetOwner(process),
                    SessionId = process.SessionId,
                    IntegrityLevel = TryGetIntegrityLevel(process),
                    IsSystemProcess = IsSystemProcess(path, process),
                    IsAccessible = true
                };

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var (publisher, status) = _signatureCache.GetOrQuery(path);
                    details.DigitalSignature = publisher;
                    details.SignatureStatus = status;
                }
                else
                {
                    details.DigitalSignature = "不可访问";
                    details.SignatureStatus = SignatureStatus.CannotVerify;
                }

                _cache[cacheKey] = details;
                TrimCache();
                return details;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (Exception)
            {
                return new ProcessDetails
                {
                    Identity = new ProcessIdentity { Pid = pid, ProcessName = $"PID {pid}" },
                    IsAccessible = false,
                    DigitalSignature = "权限不足",
                    SignatureStatus = SignatureStatus.CannotVerify,
                    CommandLine = "权限不足",
                    Owner = "权限不足"
                };
            }
        }, cancellationToken);
    }

    public void InvalidateCache(int pid)
    {
        var keys = _cache.Keys.Where(k => k.StartsWith($"{pid}:")).ToList();
        foreach (var key in keys)
            _cache.TryRemove(key, out _);
    }

    public void ClearCache() => _cache.Clear();

    private ProcessDetails? BuildScanDetails(int pid, Dictionary<int, string> commandLines)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            var startTime = SafeGetStartTime(process);
            var cacheKey = $"{pid}:{startTime?.Ticks ?? 0}:scan";

            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var name = process.ProcessName;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";

            var path = TryGetExecutablePath(process);
            var details = new ProcessDetails
            {
                Identity = new ProcessIdentity
                {
                    Pid = pid,
                    ProcessName = name,
                    StartTime = startTime,
                    ExecutablePath = path
                },
                CommandLine = commandLines.GetValueOrDefault(pid, string.Empty),
                Owner = string.Empty,
                SessionId = process.SessionId,
                IsSystemProcess = IsSystemProcess(path, process),
                IsAccessible = true,
                DigitalSignature = string.Empty,
                SignatureStatus = SignatureStatus.Unknown
            };

            _cache[cacheKey] = details;
            TrimCache();
            return details;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch
        {
            return new ProcessDetails
            {
                Identity = new ProcessIdentity { Pid = pid, ProcessName = $"PID {pid}" },
                IsAccessible = false,
                DigitalSignature = "权限不足",
                SignatureStatus = SignatureStatus.CannotVerify,
                CommandLine = string.Empty,
                Owner = string.Empty
            };
        }
    }

    private static Dictionary<int, string> LoadCommandLineMap(CancellationToken cancellationToken)
    {
        var map = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process");
            foreach (ManagementObject obj in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pidObj = obj["ProcessId"];
                if (pidObj is null) continue;
                var pid = Convert.ToInt32(pidObj);
                map[pid] = obj["CommandLine"]?.ToString() ?? string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch { }

        return map;
    }

    private void TrimCache()
    {
        if (_cache.Count <= 500) return;
        var toRemove = _cache.Keys.Take(_cache.Count - 400).ToList();
        foreach (var key in toRemove)
            _cache.TryRemove(key, out _);
    }

    private static DateTime? SafeGetStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return null; }
    }

    private static string TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            try
            {
                return GetProcessImagePath(process.Handle);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    private static string TryGetCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid);
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["CommandLine"]?.ToString() ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }

    private static string TryGetOwner(Process process)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_Process WHERE ProcessId = {process.Id}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var outParams = obj.InvokeMethod("GetOwner", null, null);
                if (outParams?["ReturnValue"]?.ToString() == "0")
                {
                    var user = outParams["User"]?.ToString();
                    var domain = outParams["Domain"]?.ToString();
                    return string.IsNullOrEmpty(domain) ? user ?? string.Empty : $"{domain}\\{user}";
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static string TryGetIntegrityLevel(Process process)
    {
        return string.Empty;
    }

    private static bool IsSystemProcess(string path, Process process)
    {
        if (!string.IsNullOrEmpty(path))
        {
            var normalized = path.Replace('/', '\\');
            if (normalized.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return process.SessionId == 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, System.Text.StringBuilder exeName, ref int size);

    private static string GetProcessImagePath(IntPtr handle)
    {
        var sb = new System.Text.StringBuilder(1024);
        var size = sb.Capacity;
        if (QueryFullProcessImageName(handle, 0, sb, ref size))
            return sb.ToString();
        return string.Empty;
    }
}

public class SignatureCacheService : ISignatureCacheService
{
    private readonly ConcurrentDictionary<string, (string Publisher, SignatureStatus Status, long LastWrite, long Size)> _cache = new();

    public (string Publisher, SignatureStatus Status) GetOrQuery(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            return ("不可访问", SignatureStatus.CannotVerify);

        try
        {
            var info = new FileInfo(executablePath);
            var cacheKey = executablePath.ToLowerInvariant();

            if (_cache.TryGetValue(cacheKey, out var cached)
                && cached.LastWrite == info.LastWriteTimeUtc.Ticks
                && cached.Size == info.Length)
            {
                return (cached.Publisher, cached.Status);
            }

            var result = FileSignatureHelper.Verify(executablePath);
            _cache[cacheKey] = (result.Publisher, result.Status, info.LastWriteTimeUtc.Ticks, info.Length);
            return result;
        }
        catch
        {
            return ("无法验证", SignatureStatus.CannotVerify);
        }
    }
}
