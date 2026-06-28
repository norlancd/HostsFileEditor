using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HostsFileEditor;

public class AuditLogger : IAuditLogger
{
    private static readonly Lazy<AuditLogger> _instance = new(() => new AuditLogger());
    public static AuditLogger Instance => _instance.Value;

    private static readonly string _logDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HostsFileEditor");

    public static readonly string LogPath = Path.Combine(_logDir, "audit.log");

    private static readonly object _lock = new();

    private int _nextSeq = 1;
    private string _lastHash = "genesis";
    private bool _initialized;
    private bool _disabled;
    private static string? _cachedAppVersion;

    public event EventHandler<string>? IntegrityFailed;
    public event EventHandler<string>? LogError;

    private AuditLogger() { }

    /// <summary>
    /// Call once on startup: verifies chain integrity and logs a ChainBreakDetected entry if broken.
    /// </summary>
    public void Initialize()
    {
        lock (_lock)
        {
            EnsureInitialized();
            if (!VerifyChainInternal(100, out _))
            {
                IntegrityFailed?.Invoke(this, "Audit log integrity check failed — possible tampering detected.");
                LogInternal(new AuditEntry
                {
                    Action = nameof(AuditActionType.ChainBreakDetected),
                    Source = AuditSource.MainForm
                });
            }
        }
    }

    public void Log(AuditEntry entry)
    {
        if (_disabled) return;
        lock (_lock)
        {
            EnsureInitialized();
            LogInternal(entry);
        }
    }

    /// <summary>
    /// Convenience overload — every call site was independently constructing the same
    /// "new AuditEntry { Action = nameof(...), Source = ... }" wrapper around a Detail
    /// that's genuinely call-site-specific. This collapses the repeated wrapper while
    /// still letting each caller build its own <see cref="AuditDetail"/> inline.
    /// </summary>
    public void Log(AuditActionType action, string source, AuditDetail? detail = null) =>
        Log(new AuditEntry { Action = action.ToString(), Source = source, Detail = detail });

    public IEnumerable<AuditEntry> ReadEntries(DateTime? from = null, DateTime? to = null)
    {
        var results = new List<AuditEntry>();
        var files = GetLogFilesNewestFirst();

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize(line, AuditJsonContext.Default.AuditEntry);
                        if (entry == null) continue;
                        if (from.HasValue && entry.Timestamp < from.Value) continue;
                        if (to.HasValue && entry.Timestamp > to.Value) continue;
                        results.Add(entry);
                    }
                    catch (JsonException) { }
                }
            }
            catch (IOException) { }
        }

        results.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        return results;
    }

    public bool VerifyChain(int lastN = 100) => VerifyChainInternal(lastN, out _);

    private bool VerifyChainInternal(int lastN, out int breakAtLine)
    {
        breakAtLine = -1;
        if (!File.Exists(LogPath)) return true;

        try
        {
            var lines = File.ReadAllLines(LogPath);
            var start = Math.Max(0, lines.Length - lastN);
            string? prevLine = null;

            for (int i = start; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                AuditEntry? entry;
                try { entry = JsonSerializer.Deserialize(line, AuditJsonContext.Default.AuditEntry); }
                catch (JsonException) { breakAtLine = i; return false; }

                if (entry == null) continue;

                if (prevLine != null)
                {
                    var expected = "sha256:" + ComputeHash(prevLine + "\n");
                    if (entry.PreviousHash != expected) { breakAtLine = i; return false; }
                }

                prevLine = line;
            }

            return true;
        }
        catch (IOException) { return true; }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Directory.CreateDirectory(_logDir);
            if (!File.Exists(LogPath)) return;

            var lastLine = ReadLastLine(LogPath);
            if (lastLine == null) return;

            try
            {
                var entry = JsonSerializer.Deserialize(lastLine, AuditJsonContext.Default.AuditEntry);
                if (entry != null)
                {
                    _nextSeq = entry.Seq + 1;
                    _lastHash = "sha256:" + ComputeHash(lastLine + "\n");
                }
            }
            catch (JsonException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _disabled = true;
        }
    }

    private void LogInternal(AuditEntry entry)
    {
        try
        {
            RotateIfNeeded();

            entry.Seq = _nextSeq++;
            entry.PreviousHash = _lastHash;
            entry.Timestamp = DateTime.UtcNow;
            entry.Actor = $"{Environment.UserDomainName}\\{Environment.UserName}";
            entry.AppVersion = GetAppVersion();
            entry.MachineHostname = Environment.MachineName;

            var line = JsonSerializer.Serialize(entry, AuditJsonContext.Default.AuditEntry);
            AppendLine(line);
            _lastHash = "sha256:" + ComputeHash(line + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _disabled = true;
            LogError?.Invoke(this, $"Audit log write failed ({ex.Message}). Logging disabled for this session.");
        }
    }

    private static void AppendLine(string line)
    {
        Exception? lastEx = null;
        for (int retries = 3; retries > 0; retries--)
        {
            try
            {
                using var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(fs, Encoding.UTF8, leaveOpen: true);
                writer.WriteLine(line);
                return;
            }
            catch (IOException ex) when (retries > 1)
            {
                lastEx = ex;
                Thread.Sleep(50);
            }
        }
        throw lastEx!;
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogPath)) return;

        var info = new FileInfo(LogPath);
        if (info.Length < 10L * 1024 * 1024) return;

        var lastLine = ReadLastLine(LogPath);
        var newPrevHash = lastLine != null ? "sha256:" + ComputeHash(lastLine + "\n") : "genesis";

        // Shift: delete .5, move .4→.5 ... .1→.2, current→.1
        var log5 = LogPath + ".5";
        if (File.Exists(log5)) File.Delete(log5);
        for (int i = 4; i >= 1; i--)
        {
            var from = LogPath + "." + i;
            var to = LogPath + "." + (i + 1);
            if (File.Exists(from)) File.Move(from, to, overwrite: true);
        }
        File.Move(LogPath, LogPath + ".1", overwrite: true);

        _lastHash = newPrevHash;
    }

    private static string? ReadLastLine(string path)
    {
        try
        {
            string? last = null;
            foreach (var line in File.ReadLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line)) last = line;
            }
            return last;
        }
        catch (IOException) { return null; }
    }

    private static IEnumerable<string> GetLogFilesNewestFirst()
    {
        yield return LogPath;
        for (int i = 1; i <= 5; i++)
            yield return LogPath + "." + i;
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetAppVersion()
    {
        if (_cachedAppVersion != null) return _cachedAppVersion;
        _cachedAppVersion = typeof(AuditLogger).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        return _cachedAppVersion;
    }
}
