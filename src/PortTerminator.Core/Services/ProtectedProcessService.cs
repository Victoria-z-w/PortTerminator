using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;

namespace PortTerminator.Core.Services;

public class ProtectedProcessService : IProtectedProcessService
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe",
        "winlogon.exe", "services.exe", "lsass.exe", "dwm.exe"
    };

    private static readonly HashSet<int> ProtectedPids = new() { 0, 4 };

    public ServiceResult ValidateTermination(ProcessIdentity identity, bool forceKill)
    {
        if (ProtectedPids.Contains(identity.Pid))
        {
            return ServiceResult.Fail(ServiceErrorCode.ProtectedProcess,
                "系统关键进程受到保护，为了保证 Windows 稳定性，该进程默认禁止终止。");
        }

        if (ProtectedNames.Contains(identity.ProcessName))
        {
            return ServiceResult.Fail(ServiceErrorCode.ProtectedProcess,
                "系统关键进程受到保护，为了保证 Windows 稳定性，该进程默认禁止终止。");
        }

        if (string.Equals(identity.ProcessName, "svchost.exe", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult.Fail(ServiceErrorCode.ProtectedProcess,
                "该进程可能承载一个或多个 Windows 服务，强制终止可能导致系统功能异常。");
        }

        if (IsWindowsSystemPath(identity.ExecutablePath) && forceKill)
        {
            return ServiceResult.Fail(ServiceErrorCode.ProtectedProcess,
                "该进程位于 Windows 系统目录，强制终结前需要额外确认。");
        }

        return ServiceResult.Ok();
    }

    public bool IsProtected(ProcessIdentity identity)
    {
        return ProtectedPids.Contains(identity.Pid)
               || ProtectedNames.Contains(identity.ProcessName)
               || string.Equals(identity.ProcessName, "svchost.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsSystemPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = path.Replace('/', '\\');
        return normalized.StartsWith(@"C:\Windows\System32\", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(@"C:\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase);
    }
}
