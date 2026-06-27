namespace HostsFileEditor.Core.Tests;

[TestClass]
public class HostsProfileListTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Init()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        HostsProfileList.TestProfileDirectoryOverride = _tempDir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        HostsProfileList.TestProfileDirectoryOverride = null;
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [TestMethod]
    public void Refresh_LoadsFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "y");
        HostsProfileList.Instance.Refresh();
        HostsProfileList.Instance.Count.ShouldBe(2);
    }

    [TestMethod]
    public void Delete_RemovesFile()
    {
        var path = Path.Combine(_tempDir, "a.txt");
        File.WriteAllText(path, "x");
        HostsProfileList.Instance.Refresh();
        var item = HostsProfileList.Instance.First(a => a.FilePath == path);
        HostsProfileList.Instance.Delete(item);
        File.Exists(path).ShouldBeFalse();
    }
}
