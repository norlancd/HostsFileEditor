using HostsFileEditor.Extensions;
using HostsFileEditor.Properties;
using HostsFileEditor.Utilities;
using HostsFileEditor.Win32;
using System.ComponentModel;

namespace HostsFileEditor;

public class HostsFile : IHostsFile
{
    public static readonly string DefaultHostFileDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"System32\drivers\etc");

    public static readonly string DefaultHostFilePath =
        Path.Combine(DefaultHostFileDirectory, @"hosts");

    public static readonly string DefaultBackupHostFilePath =
        DefaultHostFilePath + ".bak";

    public static readonly string DefaultDisabledHostFilePath =
        DefaultHostFilePath + ".disabled";

    // Internal test hook: override backup file path so unit tests do not need elevated permissions
    internal static string? TestBackupHostFilePathOverride { get; set; }

    private static readonly Lazy<HostsFile> _instance =
        new(() =>
        {
            UndoManager.Instance.ClearHistory();

            return new HostsFile(DefaultHostFilePath);
        });

    private readonly string _filePath;

    // The disk content as of the last load/save through this app — used to detect
    // edits made outside the app (e.g. a text editor) before they get clobbered.
    private string[] _lastKnownDiskLines = [];

    private HostsFile(string filePath)
    {
        _filePath = filePath;

        if (!File.Exists(filePath))
        {
            Entries = [];
        }
        else
        {
            var backupPath = TestBackupHostFilePathOverride ?? DefaultBackupHostFilePath;
            using (FileEx.DisableAttributes(backupPath, FileAttributes.ReadOnly))
            {
                File.Copy(filePath, backupPath, true);
            }

            var lines = File.ReadAllLines(filePath);
            Entries = new HostsEntryList(lines, RemoveDefaultText);
            _lastKnownDiskLines = lines;
        }

        Entries.ListChanged += OnHostsEntriesListChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static HostsFile Instance => _instance.Value;

    public static bool IsEnabled => File.Exists(DefaultHostFilePath);

    public static bool RemoveDefaultText { get; set; }

    public int EnabledCount => Entries.Count(entry => entry.Enabled);

    public HostsEntryList Entries { get; private set; }

    public int LineCount => Entries.Count;

    private bool _hasUnsavedChanges;

    /// <summary>
    /// True if in-memory <see cref="Entries"/> have changed since the last <see cref="Save"/>
    /// (or load). Drives the "unsaved changes" indicator in the UI.
    /// </summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (_hasUnsavedChanges == value) return;
            _hasUnsavedChanges = value;
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    public void Import(string importFilePath, bool? removeDefaultTextOverride = null)
    {
        if (_filePath != importFilePath)
        {
            Entries.BatchUpdate(() =>
            {
                Entries.Clear();
                Entries.AddLines(File.ReadAllLines(importFilePath), removeDefaultTextOverride ?? RemoveDefaultText);
            });
        }
    }

    public void ImportFromLines(IEnumerable<string> lines)
    {
        Entries.BatchUpdate(() =>
        {
            Entries.Clear();
            Entries.AddLines(lines, RemoveDefaultText);
        });
    }

    public void SaveAsProfile(string name)
    {
        var profile = new HostsProfile(name);
        SaveAs(profile.FilePath);
        HostsProfileList.Instance.Add(profile);
    }

    public void RestoreDefault()
    {
        UndoManager.Instance.ClearHistory();

        Entries.BatchUpdate(() =>
        {
            Entries.Clear();
            Entries.AddLines(
                Resources.hosts.Split([Environment.NewLine], StringSplitOptions.None),
                false);
        });
    }

    /// <summary>
    /// Disables hosts resolution entirely by moving the live hosts file out of the
    /// way (Windows then has no hosts file at all, unlike any profile which always
    /// has content, even "Default"). Clears the in-memory grid too, so a stray
    /// File &gt; Save while disabled can't silently resurrect the old content instead
    /// of leaving hosts genuinely absent.
    /// </summary>
    public void DisableAll()
    {
        UndoManager.Instance.ClearHistory();

        if (File.Exists(DefaultHostFilePath))
        {
            using (FileEx.DisableAttributes(DefaultHostFilePath, FileAttributes.ReadOnly))
            {
                if (File.Exists(DefaultDisabledHostFilePath))
                {
                    using (FileEx.DisableAttributes(DefaultDisabledHostFilePath, FileAttributes.ReadOnly))
                    {
                        File.Delete(DefaultDisabledHostFilePath);
                    }
                }

                File.Move(DefaultHostFilePath, DefaultDisabledHostFilePath);
            }
        }

        NativeMethods.FlushDns();

        Entries.BatchUpdate(() => Entries.Clear());
        _lastKnownDiskLines = [];
        HasUnsavedChanges = false;
    }

    public void Save()
    {
        SaveAs(_filePath);
        NativeMethods.FlushDns();
        _lastKnownDiskLines = Entries.Select(entry => entry.UnparsedText).ToArray();
        HasUnsavedChanges = false;
    }

    /// <summary>
    /// True if the on-disk hosts file has changed since this app last loaded or
    /// saved it (e.g. edited directly in a text editor). Check before <see cref="Save"/>
    /// to avoid silently clobbering an external edit.
    /// </summary>
    public bool WasModifiedExternally()
    {
        if (!File.Exists(_filePath)) return false;

        try
        {
            return !File.ReadAllLines(_filePath).SequenceEqual(_lastKnownDiskLines);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Acknowledges an external change without reloading it — keeps current in-memory
    /// edits as-is, but stops <see cref="WasModifiedExternally"/> from reporting the
    /// same already-dismissed edit again.
    /// </summary>
    public void AcknowledgeExternalChange()
    {
        try { _lastKnownDiskLines = File.ReadAllLines(_filePath); }
        catch (IOException) { }
    }

    public void SaveAs(string saveFilePath)
    {
        FileInfo info = new(saveFilePath);

        if (string.IsNullOrWhiteSpace(info.DirectoryName))
        {
            throw new ArgumentException("Invalid file path.", nameof(saveFilePath));
        }

        if (!Directory.Exists(info.DirectoryName))
        {
            Directory.CreateDirectory(info.DirectoryName);
        }

        using (FileEx.DisableAttributes(saveFilePath, FileAttributes.ReadOnly))
        {
            File.WriteAllLines(
                saveFilePath,
                Entries.Select(entry => entry.UnparsedText));
        }
    }

    public void Refresh(bool removeDefault = true)
    {
        UndoManager.Instance.ClearHistory();

        var lines = File.ReadAllLines(_filePath);

        Entries.BatchUpdate(() =>
        {
            Entries.Clear();
            Entries.AddLines(lines, removeDefault);
        });

        _lastKnownDiskLines = lines;

        NativeMethods.FlushDns();
        HasUnsavedChanges = false;
    }

    protected void OnPropertyChanged(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    private void OnHostsEntriesListChanged(object? sender, ListChangedEventArgs e)
    {
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(EnabledCount));

        // Don't trust the event alone — HostsEntry also raises PropertyChanged(IpAddress)
        // for purely cosmetic reasons (e.g. a failed background ping sets an error message
        // and re-raises IpAddress just to refresh the grid's error glyph, without actually
        // changing the IP). Compare real serialized content instead of reacting to the event.
        HasUnsavedChanges = !Entries.Select(entry => entry.UnparsedText).SequenceEqual(_lastKnownDiskLines);
    }
}
