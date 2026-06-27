using System.Text.Json.Serialization;

namespace HostsFileEditor;

public enum RollbackTimerStatus
{
    Active,
    Snoozed,
    Cancelled,
    Completed
}

public class RollbackTimer
{
    public string ActivatedProfileName { get; set; } = string.Empty;
    public string SnapshotFileName { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public RollbackTimerStatus Status { get; set; } = RollbackTimerStatus.Active;
    public DateTime? SnoozeUntil { get; set; }
    public string SnapshotHash { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
}

[JsonSerializable(typeof(RollbackTimer))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
internal partial class RollbackTimerJsonContext : JsonSerializerContext { }
