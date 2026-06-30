using HostsFileEditor.Properties;
using HostsFileEditor.Utilities;

namespace HostsFileEditor;

/// <summary>
/// The program class containing the main entry point.
/// </summary>
internal static class Program
{
    /// <summary>
    /// The application's main form.
    /// </summary>
    private static Form? _mainForm;

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        // Ensure only one copy of the application is running at a time
        using var program = ProgramSingleInstance.Start();
        if (program.IsOnlyInstance)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += OnApplicationThreadException;
            Application.ApplicationExit += (_, _) => HotkeyRegistry.Instance.UnregisterAll();

            // Must run before anything touches HostsFile.Instance, so HostsEntryList/HostsEntry/
            // SaveAsProfile register undo actions and save profiles against these exact
            // instances — not a second one resolved later.
            HostsFile.Configure(UndoManager.Instance, HostsProfileList.Instance);

            // ProfileSwitcher moved to Core (shared with WinUI) and no longer has a static
            // Instance — each UI's composition root owns the one instance and backs it with
            // its own settings persistence (here: WinFormsSettingsStore).
            var profileSwitcher = new ProfileSwitcher(HostsFile.Instance, HostsProfileList.Instance, new WinFormsSettingsStore(), AuditLogger.Instance);
            var exportImportService = new ProfileExportImportService(HostsProfileList.Instance, AuditLogger.Instance);

            _mainForm = new MainForm(
                AuditLogger.Instance, RollbackTimerService.Instance, UndoManager.Instance,
                HostsProfileList.Instance, profileSwitcher, HotkeyRegistry.Instance, exportImportService);
            Application.Run(_mainForm);
        }
        else
        {
            ProgramSingleInstance.ShowFirstInstance();
        }
    }

    /// <summary>
    /// The on application thread exception.
    /// </summary>
    /// <param name="sender">
    /// The sender.
    /// </param>
    /// <param name="e">
    /// The event arguments.
    /// </param>
    private static void OnApplicationThreadException(object sender, ThreadExceptionEventArgs e)
    {
        MessageBox.Show(
            null,
            e.Exception.Message,
            Resources.ErrorCaption,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.DefaultDesktopOnly);
    }
}
