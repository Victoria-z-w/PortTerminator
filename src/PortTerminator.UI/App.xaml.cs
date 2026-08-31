using System.Windows;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Services;
using PortTerminator.Infrastructure;
using PortTerminator.UI.Services;
using PortTerminator.UI.ViewModels;
using PortTerminator.Windows.Helpers;
using PortTerminator.Windows.Services;

namespace PortTerminator.UI;

public partial class App : System.Windows.Application
{
    public static MainViewModel MainViewModel { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "Port Terminator 错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        InitializeAndShow();
    }

    private async void InitializeAndShow()
    {
        try
        {
            var db = new DatabaseService();
            await db.InitializeAsync();

            var settingsService = new SettingsService(db);
            var loggingService = new LoggingService(db);
            var whitelistService = new WhitelistService(db);
            var ruleService = new RuleService(db);
            await Task.WhenAll(
                settingsService.LoadAsync(),
                whitelistService.LoadAsync(),
                ruleService.LoadAsync());

            var signatureCache = new SignatureCacheService();
            var processInfoService = new ProcessInfoService(signatureCache);
            var riskService = new RiskAssessmentService();
            var protectedService = new ProtectedProcessService();
            var snapshotComparer = new PortSnapshotComparer();
            var portScanner = new PortScannerService(processInfoService, riskService, whitelistService, ruleService);
            var adminHelper = new AdminHelper();
            var elevatedClient = new ElevatedClient();
            var terminationService = new ProcessTerminationService(protectedService, processInfoService, elevatedClient, adminHelper);
            var exportService = new ExportService();
            var processManager = new ProcessManagerService(processInfoService, portScanner, riskService);
            var notificationService = new NotificationService();

            MainViewModel = new MainViewModel(
                portScanner, snapshotComparer, terminationService, whitelistService,
                settingsService, loggingService, notificationService,
                exportService, processManager, ruleService, processInfoService);

            var mainWindow = new Views.MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            await MainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.ToString(), "Port Terminator 启动错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
