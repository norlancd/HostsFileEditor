namespace HostsFileEditor.Core.Tests;

[TestClass]
public class ProfileDiffTests
{
    private static HostsEntryList Lines(params string[] lines) =>
        new(lines, filterDefault: false);

    [TestMethod]
    public void Compute_DetectsModifiedIp()
    {
        var current = Lines("127.0.0.1 localhost", "192.168.1.10 myserver.local");
        var incoming = Lines("127.0.0.1 localhost", "192.168.1.20 myserver.local");

        var diff = ProfileDiff.Compute(current, incoming);

        diff.IsEmpty.ShouldBeFalse();
        diff.Modified.Count.ShouldBe(1);
        diff.Modified[0].Before.IpAddress.ShouldBe("192.168.1.10");
        diff.Modified[0].After.IpAddress.ShouldBe("192.168.1.20");
    }

    [TestMethod]
    public void Compute_DetectsAddedAndRemoved()
    {
        var current = Lines("127.0.0.1 localhost", "10.0.0.5 old.local");
        var incoming = Lines("127.0.0.1 localhost", "10.0.0.9 new.local");

        var diff = ProfileDiff.Compute(current, incoming);

        diff.IsEmpty.ShouldBeFalse();
        diff.Added.Count.ShouldBe(1);
        diff.Added[0].HostNames.ShouldBe("new.local");
        diff.Removed.Count.ShouldBe(1);
        diff.Removed[0].HostNames.ShouldBe("old.local");
    }

    [TestMethod]
    public void Compute_IdenticalProfiles_IsEmpty()
    {
        var current = Lines("127.0.0.1 localhost", "192.168.1.10 myserver.local");
        var incoming = Lines("127.0.0.1 localhost", "192.168.1.10 myserver.local");

        var diff = ProfileDiff.Compute(current, incoming);

        diff.IsEmpty.ShouldBeTrue();
    }

    [TestMethod]
    public void Compute_DetectsToggledEnabledState()
    {
        var current = Lines("127.0.0.1 localhost", "10.0.0.5 svc.local");
        var incoming = Lines("127.0.0.1 localhost", "#10.0.0.5 svc.local");

        var diff = ProfileDiff.Compute(current, incoming);

        diff.IsEmpty.ShouldBeFalse();
        diff.Toggled.Count.ShouldBe(1);
    }

    [TestMethod]
    public void Compute_RealisticClonedAndEditedProfile_DetectsChanges()
    {
        // Simulates: clone a profile, then add one entry and change another, before activating
        var current = Lines(
            "# My Hosts File",
            "127.0.0.1 localhost",
            "192.168.10.5 api.dev",
            "192.168.10.6 web.dev");

        var incoming = Lines(
            "# My Hosts File",
            "127.0.0.1 localhost",
            "192.168.10.50 api.dev",
            "192.168.10.6 web.dev",
            "192.168.10.7 admin.dev");

        var diff = ProfileDiff.Compute(current, incoming);

        diff.IsEmpty.ShouldBeFalse();
        diff.Modified.Count.ShouldBe(1);
        diff.Added.Count.ShouldBe(1);
        diff.Removed.Count.ShouldBe(0);
    }
}
