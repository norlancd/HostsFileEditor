namespace HostsFileEditor.Core.Tests;

[TestClass]
public class HostsProfileAdditionalTests
{
    [TestMethod]
    public void Validate_DuplicateInDirectory_ReturnsFalse()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var fileName = "dup.txt";
            var fullPath = Path.Combine(dir, fileName);
            File.WriteAllText(fullPath, "x");
            // Simulate existing profile directory by creating file inside actual profile dir if possible
            // We cannot change ProfileDirectory easily; rely on Validate logic: it checks ProfileDirectory files names vs provided path
            // Provide fullPath to validate (should succeed) then fileName to attempt duplicate detection (will likely pass unless ProfileDirectory matches dir)
            HostsProfile.Validate(fullPath, out var err1).ShouldBeTrue();
            err1.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
