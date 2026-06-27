namespace HostsFileEditor;

public class DiffPair
{
    public required HostsEntry Before { get; init; }
    public required HostsEntry After { get; init; }
}

public class ProfileDiffResult
{
    public required IReadOnlyList<HostsEntry> Added { get; init; }
    public required IReadOnlyList<HostsEntry> Removed { get; init; }
    public required IReadOnlyList<DiffPair> Modified { get; init; }
    public required IReadOnlyList<DiffPair> Toggled { get; init; }

    public bool IsEmpty =>
        Added.Count == 0 && Removed.Count == 0 &&
        Modified.Count == 0 && Toggled.Count == 0;

    public int TotalChanges =>
        Added.Count + Removed.Count + Modified.Count + Toggled.Count;
}

public static class ProfileDiff
{
    /// <summary>
    /// Computes the diff between the currently loaded entries and those in an incoming file.
    /// Only enabled, valid entries affect DNS resolution and are included in the comparison.
    /// Comment-only and disabled entries are excluded.
    /// </summary>
    public static ProfileDiffResult Compute(HostsEntryList current, HostsEntryList incoming)
    {
        var currentEnabled = current.Where(e => e.Valid && e.Enabled).ToList();
        var incomingEnabled = incoming.Where(e => e.Valid && e.Enabled).ToList();

        // hostname (lower) → first entry  — for detecting IP changes
        var currentByHost = currentEnabled
            .GroupBy(e => e.HostNames.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var incomingByHost = incomingEnabled
            .GroupBy(e => e.HostNames.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        // Modified: same hostname in both, different IP
        var modified = currentByHost
            .Where(kvp =>
                incomingByHost.TryGetValue(kvp.Key, out var inc) &&
                !string.Equals(kvp.Value.IpAddress, inc.IpAddress, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => new DiffPair { Before = kvp.Value, After = incomingByHost[kvp.Key] })
            .ToList();

        var modifiedHosts = modified
            .Select(m => m.Before.HostNames.Trim().ToLowerInvariant())
            .ToHashSet();

        // Full (ip, hostname) key sets — for Add/Remove detection
        var currentKeys = currentEnabled.Select(EntryKey).ToHashSet();
        var incomingKeys = incomingEnabled.Select(EntryKey).ToHashSet();

        // Added: enabled in incoming, no matching full key in current, not a hostname rebind
        var added = incomingEnabled
            .Where(e =>
                !currentKeys.Contains(EntryKey(e)) &&
                !modifiedHosts.Contains(e.HostNames.Trim().ToLowerInvariant()))
            .ToList();

        // Removed: enabled in current, no matching full key in incoming, not a hostname rebind
        var removed = currentEnabled
            .Where(e =>
                !incomingKeys.Contains(EntryKey(e)) &&
                !modifiedHosts.Contains(e.HostNames.Trim().ToLowerInvariant()))
            .ToList();

        // Toggled: same (ip, hostname) in both but enabled state differs
        var currentAllByKey = current
            .Where(e => e.Valid && !string.IsNullOrWhiteSpace(e.IpAddress))
            .GroupBy(EntryKey)
            .ToDictionary(g => g.Key, g => g.First());

        var incomingAllByKey = incoming
            .Where(e => e.Valid && !string.IsNullOrWhiteSpace(e.IpAddress))
            .GroupBy(EntryKey)
            .ToDictionary(g => g.Key, g => g.First());

        var toggled = currentAllByKey
            .Where(kvp =>
                incomingAllByKey.TryGetValue(kvp.Key, out var inc) &&
                kvp.Value.Enabled != inc.Enabled)
            .Select(kvp => new DiffPair { Before = kvp.Value, After = incomingAllByKey[kvp.Key] })
            .ToList();

        return new ProfileDiffResult
        {
            Added = added,
            Removed = removed,
            Modified = modified,
            Toggled = toggled
        };
    }

    private static (string Ip, string Host) EntryKey(HostsEntry e) =>
        (e.IpAddress.Trim().ToLowerInvariant(), e.HostNames.Trim().ToLowerInvariant());
}
