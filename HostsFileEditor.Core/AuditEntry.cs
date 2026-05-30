using System.Text.Json.Serialization;

namespace HostsFileEditor;

public enum AuditActionType
{
    ProfileSwitch,
    EntryAdded,
    EntryRemoved,
    EntryModified,
    HostsFileEnabled,
    HostsFileDisabled,
    TimedProfileSwitch,
    RollbackExecuted,
    TimerCancelled,
    DefaultRestored,
    FileImported,
    UrlImported,
    AliasUpdated,
    ChainBreakDetected
}

public static class AuditSource
{
    public const string MainForm = "MainForm";
    public const string TrayMenu = "TrayMenu";
    public const string TrayHotkey = "TrayHotkey";
    public const string CLI = "CLI";
    public const string RollbackTimer = "RollbackTimer";
    public const string UrlScheduler = "UrlScheduler";
}

public class AuditEntrySnapshot
{
    public string Ip { get; set; } = string.Empty;
    public string Hostnames { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public class AuditEntryModification
{
    public AuditEntrySnapshot Before { get; set; } = new();
    public AuditEntrySnapshot After { get; set; } = new();
}

public class AuditDetail
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileFrom { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileTo { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? BypassedDiff { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RollbackTimerMinutes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AuditEntrySnapshot>? EntriesAdded { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AuditEntrySnapshot>? EntriesRemoved { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AuditEntryModification>? EntriesModified { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuditEntrySnapshot? Entry { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuditEntrySnapshot? Before { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuditEntrySnapshot? After { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourcePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoReverted { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileReverted { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileKept { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AliasName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NewValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AffectedEntries { get; set; }
}

public class AuditEntry
{
    public int Seq { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string PreviousHash { get; set; } = "genesis";
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string MachineHostname { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuditDetail? Detail { get; set; }
}

[JsonSerializable(typeof(AuditEntry))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
internal partial class AuditJsonContext : JsonSerializerContext { }
