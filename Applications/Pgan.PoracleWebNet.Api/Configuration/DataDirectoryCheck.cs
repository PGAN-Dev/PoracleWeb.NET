namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>
/// Answers, once at startup, whether <c>DATA_DIR</c> can be written.
/// </summary>
/// <remarks>
/// The image runs as <c>appuser</c> (uid 999), and Docker creates a bind-mounted <c>./data</c> that does
/// not exist yet as root on the host. Nothing then fails loudly: DataProtection logs a key-ring stack
/// trace and falls back to keys held in memory, so every container recreate signs out each SSO user,
/// and the avatar cache is rebuilt from Discord on every start. The check turns that into one log line
/// naming the fix. An absent directory is not a failure, since the app creates it.
/// </remarks>
public static class DataDirectoryCheck
{
    /// <summary>True when a file can be created and removed in <paramref name="dataDir"/>, creating the directory if absent.</summary>
    public static bool IsWritable(string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            var probe = Path.Join(dataDir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
