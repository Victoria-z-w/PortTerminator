using System.Diagnostics;
using System.Security.Principal;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;

namespace PortTerminator.Windows.Helpers;

public class AdminHelper : IAdminHelper
{
    public bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void RestartAsAdmin()
    {
        var exePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("无法获取程序路径");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas"
        };
        Process.Start(startInfo);
        Environment.Exit(0);
    }
}
