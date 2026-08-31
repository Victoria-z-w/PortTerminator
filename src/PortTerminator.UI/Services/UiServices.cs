using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;

namespace PortTerminator.UI.Services;

public class NotificationService : INotificationService
{
    public event Action<string, LogLevel>? ToastRequested;

    public void ShowToast(string message, LogLevel level = LogLevel.Info)
    {
        ToastRequested?.Invoke(message, level);
    }

    public void ShowDesktopNotification(string title, string message)
    {
        try
        {
            // Simple tray balloon fallback
            ToastRequested?.Invoke($"{title}: {message}", LogLevel.Warning);
        }
        catch { }
    }
}

public class TrayService : IDisposable
{
    private System.Windows.Forms.NotifyIcon? _icon;

    public void Initialize(Window mainWindow, Action onShow, Action onScan, Action onExit)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        var trayIcon = File.Exists(iconPath)
            ? new System.Drawing.Icon(iconPath)
            : System.Drawing.SystemIcons.Shield;

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Port Terminator",
            Visible = true,
            Icon = trayIcon
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开 Port Terminator", null, (_, _) => onShow());
        menu.Items.Add("扫描端口", null, (_, _) => onScan());
        menu.Items.Add("-");
        menu.Items.Add("退出", null, (_, _) => onExit());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => onShow();

        mainWindow.StateChanged += (_, _) =>
        {
            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.Hide();
                _icon.ShowBalloonTip(2000, "Port Terminator", "程序已最小化到系统托盘", System.Windows.Forms.ToolTipIcon.Info);
            }
        };
    }

    public void Dispose()
    {
        _icon?.Dispose();
    }
}
