namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>How a running component compares to what has been published.</summary>
public enum UpdateState
{
    /// <summary>Nothing could be established — no network, check switched off, unparseable answer.</summary>
    Unknown = 0,

    /// <summary>Running exactly what is published.</summary>
    UpToDate = 1,

    /// <summary>Something newer has been released.</summary>
    Behind = 2,

    /// <summary>
    /// Running something newer than the latest release: a development build.
    /// </summary>
    /// <remarks>
    /// This is what makes the branch answerable after all. PoracleNG bumps <c>processor/version.go</c>
    /// at the start of a cycle — <c>main</c> reads 5.1.0 while <c>develop</c> already reads 5.2.0 — so a
    /// binary reporting more than the released version is by definition not built from a release.
    /// </remarks>
    PreRelease = 3,
}

/// <summary>Whether one component is behind, and what it would be moving to.</summary>
public sealed class UpdateStatus
{
    /// <summary>The version this instance is running, as it reports itself.</summary>
    public string? Running
    {
        get; init;
    }

    /// <summary>The newest published version, or null when it could not be read.</summary>
    public string? Latest
    {
        get; init;
    }

    public UpdateState State
    {
        get; init;
    }

    /// <summary>Nothing is known: the check is off, or it failed.</summary>
    public static UpdateStatus Unknown(string? running = null) => new()
    {
        Running = running,
        State = UpdateState.Unknown,
    };

    /// <summary>
    /// Compares two version strings, tolerating a leading v and a suffix.
    /// </summary>
    /// <remarks>
    /// A version that will not parse leaves the state Unknown rather than guessing a direction. Telling
    /// somebody they are behind when they are not is worse than saying nothing, because the next real
    /// warning gets ignored too.
    /// </remarks>
    public static UpdateStatus Compare(string? running, string? latest)
    {
        var runningVersion = Parse(running);
        var latestVersion = Parse(latest);

        if (runningVersion is null || latestVersion is null)
        {
            return new UpdateStatus { Running = running, Latest = latest, State = UpdateState.Unknown };
        }

        var state = runningVersion.CompareTo(latestVersion) switch
        {
            0 => UpdateState.UpToDate,
            < 0 => UpdateState.Behind,
            _ => UpdateState.PreRelease,
        };

        return new UpdateStatus { Running = running, Latest = latest, State = state };
    }

    private static Version? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.TrimStart('v', 'V');
        var numeric = new string(trimmed.TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray()).TrimEnd('.');

        // "0.0.0" is what an un-stamped local build reports, and "beta" is PoracleWeb's own rolling
        // channel. Neither is a point on the release line, so neither gets compared to one.
        return numeric.Length == 0 || numeric.StartsWith("0.0.0", StringComparison.Ordinal)
            ? null
            : Version.TryParse(numeric, out var parsed) ? parsed : null;
    }
}
