using System.Text.RegularExpressions;

namespace Pgan.PoracleWebNet.Tests.Architecture;

/// <summary>
/// A webhook's human id is its URL, token included, so every <c>[LoggerMessage]</c> argument that can carry a
/// human id must pass through <c>LogSafe.Id</c>. This finds each such parameter by name and checks every call
/// site, so the next <c>{UserId}</c> log line cannot arrive raw. See #1038.
/// </summary>
public sealed partial class LoggedHumanIdsAreMaskedTests
{
    private static readonly string[] ScannedRoots = ["Core", "Applications/Pgan.PoracleWebNet.Api"];

    /// <summary>Parameter names that hold a human id: a user, admin, impersonator, destination or grant.</summary>
    private static readonly Regex HumanIdParameter = new(
        "^(userId|adminId|humanId|ownerId|delegateId|destinationId|webhookId|webhook|targetId|target|impersonatedBy|grant)$",
        RegexOptions.Compiled);

    /// <summary>Parameters whose name matches but which hold no human id.</summary>
    private static readonly HashSet<string> NotHumanIds =
    [
        "OidcClient.LogTokenFailed.grant", // an OAuth grant type, such as refresh_token
    ];

    [Fact]
    public void EveryLoggedHumanIdGoesThroughLogSafe()
    {
        var root = FindSolutionRoot();
        var files = ScannedRoots
            .SelectMany(r => Directory.EnumerateFiles(Path.Combine(root, r), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllText);

        var (checkedArguments, offenders) = Scan(files);

        Assert.True(checkedArguments > 50, $"Checked only {checkedArguments} logged ids, so the scan is not finding the call sites.");
        Assert.True(offenders.Count == 0, "Logged without LogSafe.Id:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void ARawIdIsCaughtAndAWrappedOneIsNot()
    {
        // The scanner itself: without this, a parsing slip would pass the test above by finding nothing wrong.
        const string source = """
            public sealed partial class Sample
            {
                public void Run(string userId, string other)
                {
                    LogDone(this._logger, userId, other);
                    LogDone(this._logger, LogSafe.Id(userId), other);
                    LogFailed(this._logger, new Exception("x"), Pick(userId, "a,b"));
                }

                [LoggerMessage(Level = LogLevel.Information, Message = "Done {UserId} {Other}")]
                private static partial void LogDone(ILogger logger, string userId, string other);

                [LoggerMessage(Level = LogLevel.Warning, Message = "Failed {UserId}")]
                private static partial void LogFailed(ILogger logger, Exception ex, string userId);
            }
            """;

        var (checkedArguments, offenders) = Scan(new Dictionary<string, string> { ["Sample.cs"] = source });

        Assert.Equal(3, checkedArguments);
        Assert.Equal(2, offenders.Count);
        Assert.Contains(offenders, o => o.Contains("LogDone", StringComparison.Ordinal) && o.Contains("<- userId", StringComparison.Ordinal));
        Assert.Contains(offenders, o => o.Contains("LogFailed", StringComparison.Ordinal));
    }

    private static (int Checked, List<string> Offenders) Scan(IReadOnlyDictionary<string, string> files)
    {
        var offenders = new List<string>();
        var checkedArguments = 0;

        foreach (var (declFile, declText) in files)
        {
            var classes = ClassNames(declText);
            foreach (Match decl in Declaration().Matches(declText))
            {
                var method = decl.Groups["name"].Value;
                var names = decl.Groups["params"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1])
                    .ToList();
                var className = classes.FirstOrDefault() ?? Path.GetFileNameWithoutExtension(declFile);
                var idPositions = Enumerable.Range(0, names.Count)
                    .Where(i => HumanIdParameter.IsMatch(names[i]) && !NotHumanIds.Contains($"{className}.{method}.{names[i]}"))
                    .ToList();
                if (idPositions.Count == 0)
                {
                    continue;
                }

                // Partial classes split across files (PoracleTrackingProxy.*.cs) call each other's log methods.
                var dir = Path.GetDirectoryName(declFile);
                var siblings = files.Where(f => Path.GetDirectoryName(f.Key) == dir && ClassNames(f.Value).Overlaps(classes));
                foreach (var (callFile, callText) in siblings)
                {
                    foreach (Match call in new Regex(@"(?<![\w.])" + method + @"\(").Matches(callText))
                    {
                        if (Regex.IsMatch(callText[Math.Max(0, call.Index - 30)..call.Index], @"void\s+$"))
                        {
                            continue;
                        }

                        var args = SplitArguments(callText, call.Index + call.Length);
                        if (args.Count != names.Count)
                        {
                            offenders.Add($"{callFile}: {method} called with {args.Count} arguments, declared with {names.Count}");
                            continue;
                        }

                        foreach (var i in idPositions)
                        {
                            checkedArguments++;
                            if (!args[i].StartsWith("LogSafe.Id(", StringComparison.Ordinal))
                            {
                                offenders.Add($"{callFile}: {method} {names[i]} <- {args[i]}");
                            }
                        }
                    }
                }
            }
        }

        return (checkedArguments, offenders);
    }

    private static HashSet<string> ClassNames(string text) =>
        [.. ClassName().Matches(text).Select(m => m.Groups[1].Value)];

    /// <summary>Splits a call's arguments at top-level commas, starting just after its opening parenthesis.</summary>
    private static List<string> SplitArguments(string text, int start)
    {
        var args = new List<string>();
        var depth = 0;
        var from = start;
        char? quote = null;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = null;
                }

                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}' when depth > 0:
                    depth--;
                    break;
                case ')':
                    args.Add(text[from..i].Trim());
                    return args;
                case ',' when depth == 0:
                    args.Add(text[from..i].Trim());
                    from = i + 1;
                    break;
                default:
                    break;
            }
        }

        return args;
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Pgan.PoracleWebNet.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    [GeneratedRegex(@"\[LoggerMessage\(.*?\)\]\s*(?:private|internal|public)?\s*static\s+partial\s+void\s+(?<name>\w+)\((?<params>.*?)\);", RegexOptions.Singleline)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"partial\s+(?:class|record)\s+(\w+)")]
    private static partial Regex ClassName();
}
