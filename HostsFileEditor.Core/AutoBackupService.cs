using HostsFileEditor.Utilities;
using System.Security.Cryptography;

namespace HostsFileEditor;

public class AutoBackupService
{
    private static readonly Lazy<AutoBackupService> _instance = new(() => new AutoBackupService());
    public static AutoBackupService Instance => _instance.Value;

    public static readonly string AutoBackupDirectory =
        Path.Combine(HostsFile.DefaultHostFileDirectory, "archive", "__autobak");

    public static bool Enabled { get; set; } = true;
    public static int MaxCount { get; set; } = 20;

    private string? _lastBackupHash;
    private bool _diskFullWarningShown;

    private AutoBackupService() { }

    public void CreateBackup()
    {
        if (!Enabled || MaxCount <= 0) return;
        if (!File.Exists(HostsFile.DefaultHostFilePath)) return;

        byte[] content;
        try { content = File.ReadAllBytes(HostsFile.DefaultHostFilePath); }
        catch (IOException) { return; }

        var hash = ComputeHash(content);
        if (hash == _lastBackupHash) return;

        try
        {
            EnsureDirectory();

            var name = $"hosts_{DateTime.Now:yyyyMMdd_HHmmss}_host_backup";
            var backupPath = ResolveUniquePath(AutoBackupDirectory, name);

            using (FileEx.DisableAttributes(backupPath, FileAttributes.ReadOnly))
                File.WriteAllBytes(backupPath, content);

            _lastBackupHash = hash;
            _diskFullWarningShown = false;

            EnforceRetentionLimit();
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            if (!_diskFullWarningShown)
            {
                _diskFullWarningShown = true;
                BackupError?.Invoke(this, "Auto-backup failed: disk full. Save was NOT blocked.");
            }
        }
        catch (IOException)
        {
            // Backup failure must never block the save
        }
    }

    public event EventHandler<string>? BackupError;

    private static void EnsureDirectory()
        => Directory.CreateDirectory(AutoBackupDirectory);

    private static string ResolveUniquePath(string dir, string baseName)
    {
        var path = Path.Combine(dir, baseName);
        if (!File.Exists(path)) return path;

        for (int i = 1; i <= 999; i++)
        {
            path = Path.Combine(dir, $"{baseName}_{i}");
            if (!File.Exists(path)) return path;
        }

        return Path.Combine(dir, baseName);
    }

    private void EnforceRetentionLimit()
    {
        if (!Directory.Exists(AutoBackupDirectory)) return;

        var files = Directory.GetFiles(AutoBackupDirectory)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        while (files.Count > MaxCount)
        {
            try { File.Delete(files[0]); }
            catch (IOException) { }
            files.RemoveAt(0);
        }
    }

    private static string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }

    private static bool IsDiskFull(IOException ex)
    {
        const int ErrorDiskFull = unchecked((int)0x80070070);
        const int ErrorHandleDiskFull = unchecked((int)0x80070027);
        return ex.HResult == ErrorDiskFull || ex.HResult == ErrorHandleDiskFull;
    }
}
