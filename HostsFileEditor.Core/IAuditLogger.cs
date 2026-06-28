namespace HostsFileEditor;

/// <summary>
/// Abstraction over <see cref="AuditLogger"/>'s instance surface — extracted so
/// consumers can eventually depend on this instead of the static <c>Instance</c>
/// accessor, without changing any behavior today.
/// </summary>
public interface IAuditLogger
{
    event EventHandler<string>? IntegrityFailed;

    event EventHandler<string>? LogError;

    void Initialize();

    void Log(AuditEntry entry);

    void Log(AuditActionType action, string source, AuditDetail? detail = null);

    IEnumerable<AuditEntry> ReadEntries(DateTime? from = null, DateTime? to = null);

    bool VerifyChain(int lastN = 100);
}
