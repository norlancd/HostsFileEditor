using HostsFileEditor.Services;
using HostsFileEditor.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace HostsFileEditor;

public partial class App : Application
{
    private Window? _window;
    internal IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        // Must run before anything resolves IHostsFile/HostsFile.Instance, so
        // HostsEntryList/HostsEntry register undo actions against this exact
        // UndoManager instance — not a second one resolved later.
        HostsFile.Configure(UndoManager.Instance, HostsProfileList.Instance);

        var services = new ServiceCollection();
        services.AddSingleton<DialogService>();
        services.AddSingleton<AnimationService>();
        services.AddSingleton<IUndoManager>(_ => UndoManager.Instance);
        services.AddSingleton<IHostsFile>(_ => HostsFile.Instance);
        services.AddSingleton<IHostsProfileList>(_ => HostsProfileList.Instance);
        services.AddSingleton<ISettingsStore, WinUiSettingsStore>();
        services.AddSingleton<IAuditLogger>(_ => AuditLogger.Instance);
        services.AddSingleton<IProfileSwitcher, ProfileSwitcher>();
        services.AddSingleton<IHotkeyRegistry>(_ => HotkeyRegistry.Instance);
        services.AddSingleton<IRollbackTimerService>(_ => RollbackTimerService.Instance);
        services.AddSingleton<IProfileExportImportService, ProfileExportImportService>();
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Single instance using AppInstance
        var keyInstance = AppInstance.FindOrRegisterForKey("HostsFileEditor.SingleInstance");
        if (!keyInstance.IsCurrent)
        {
            _ = keyInstance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Environment.Exit(0);
            return;
        }

        var mw = Services.GetRequiredService<MainWindow>();

        _window = mw;
        _window.Activate();
    }
}
