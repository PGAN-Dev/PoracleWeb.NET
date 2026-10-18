using Pgan.PoracleWebNet.Api.Configuration;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// The startup check behind the "DATA_DIR is not writable" log line. In Docker the bind-mounted
/// <c>./data</c> is created root-owned on the host while the image runs as <c>appuser</c>, so the
/// DataProtection keys and avatar cache silently fail to persist. The check has to tell that apart
/// from a directory that is merely absent, which the app creates itself.
/// </summary>
public class DataDirectoryCheckTests : IDisposable
{
    private readonly string _tempDir;

    public DataDirectoryCheckTests()
    {
        this._tempDir = Path.Join(Path.GetTempPath(), $"datadir-test-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDir))
        {
            Directory.Delete(this._tempDir, recursive: true);
        }
    }

    [Fact]
    public void AWritableDirectoryPasses()
    {
        Assert.True(DataDirectoryCheck.IsWritable(this._tempDir));
    }

    [Fact]
    public void AMissingDirectoryIsCreatedAndPasses()
    {
        var missing = Path.Join(this._tempDir, "not-yet");

        Assert.True(DataDirectoryCheck.IsWritable(missing));
        Assert.True(Directory.Exists(missing));
    }

    [Fact]
    public void TheProbeFileIsNotLeftBehind()
    {
        DataDirectoryCheck.IsWritable(this._tempDir);

        Assert.Empty(Directory.EnumerateFileSystemEntries(this._tempDir));
    }

    [Fact]
    public void APathThatIsAFileFails()
    {
        var file = Path.Join(this._tempDir, "a-file");
        File.WriteAllText(file, "x");

        Assert.False(DataDirectoryCheck.IsWritable(file));
    }
}
