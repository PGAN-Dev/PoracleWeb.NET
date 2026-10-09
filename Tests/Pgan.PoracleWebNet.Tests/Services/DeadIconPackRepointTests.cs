using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// whitewillem/PogoAssets no longer exists. #877 repointed the default an unset category falls back to,
/// which fixed instances that had never chosen a pack and did nothing for the ones that had: an upgraded
/// instance holding <c>uicons_*</c> rows under the dead repository requested every icon from it and drew
/// nothing. Those rows are rewritten at startup to the default each category would otherwise use.
/// </summary>
public class DeadIconPackRepointTests
{
    private const string Dead = "https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons";
    private const string Jms = "https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS";

    private readonly Mock<ISiteSettingService> _settings = new();
    private readonly Dictionary<string, SiteSetting> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SiteSetting> _writes = [];

    public DeadIconPackRepointTests()
    {
        this._settings.Setup(s => s.GetByKeyAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => this._rows.GetValueOrDefault(key));
        this._settings.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .Callback<SiteSetting>(s =>
            {
                this._writes.Add(s);
                this._rows[s.Key] = s;
            })
            .ReturnsAsync((SiteSetting s) => s);

        // custom_title exists, so the only writes a test sees are the ones it is about.
        this._rows["custom_title"] = new SiteSetting { Key = "custom_title", Value = "Mine" };
        this._rows["quick_picks_seeded"] = new SiteSetting { Key = "quick_picks_seeded", Value = "true" };
    }

    private SettingsMigrationService CreateSut() => new(
        new Mock<IPwebSettingService>().Object,
        this._settings.Object,
        new Mock<IWebhookDelegateService>().Object,
        new Mock<IQuickPickDefinitionRepository>().Object,
        new Mock<IQuickPickAppliedStateRepository>().Object,
        new Mock<ILogger<SettingsMigrationService>>().Object);

    private void Row(string key, string value) =>
        this._rows[key] = new SiteSetting { Key = key, Value = value, Category = "icons", ValueType = "url" };

    [Fact]
    public async Task EveryCategoryUnderTheDeadRepositoryIsRepointedToItsOwnFolder()
    {
        foreach (var (key, folder) in IconPackDefaults.FolderByKey)
        {
            this.Row(key, $"{Dead}/{folder}");
        }

        await this.CreateSut().SeedDefaultsAsync();

        foreach (var (key, folder) in IconPackDefaults.FolderByKey)
        {
            Assert.Equal($"{Jms}/{folder}", this._rows[key].Value);
        }
    }

    [Theory]
    [InlineData("https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon/")]
    [InlineData("http://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon")]
    [InlineData("https://raw.githubusercontent.com/WhiteWillem/PogoAssets/master/uicons/pokemon")]
    public async Task AnySpellingOfTheDeadRepositoryIsCaught(string value)
    {
        this.Row("uicons_pkmn", value);

        await this.CreateSut().SeedDefaultsAsync();

        Assert.Equal($"{Jms}/pokemon", this._rows["uicons_pkmn"].Value);
    }

    [Fact]
    public async Task TheRewriteKeepsTheRowsCategoryAndType()
    {
        this.Row("uicons_gym", $"{Dead}/gym");

        await this.CreateSut().SeedDefaultsAsync();

        Assert.Equal("icons", this._rows["uicons_gym"].Category);
        Assert.Equal("url", this._rows["uicons_gym"].ValueType);
    }

    /// <summary>
    /// What must not move: a live pack, a self-hosted pack, an empty value (which means "the default"),
    /// and a key the site does not read. Production points at jms412 and must see no write at all.
    /// </summary>
    [Fact]
    public async Task NothingElseIsTouched()
    {
        this.Row("uicons_pkmn", $"{Jms}/pokemon");
        this.Row("uicons_gym", "https://icons.example.org/whitewillem/PogoAssets/gym");
        this.Row("uicons_raid", "");
        this.Row("uicons_item", $"{Dead}/reward/item");
        this.Row("custom_page_url", $"{Dead}/readme");

        await this.CreateSut().SeedDefaultsAsync();

        Assert.Empty(this._writes);
    }

    [Fact]
    public async Task RunningItTwiceWritesOnce()
    {
        this.Row("uicons_type", $"{Dead}/type");
        var sut = this.CreateSut();

        await sut.SeedDefaultsAsync();
        await sut.SeedDefaultsAsync();

        Assert.Single(this._writes);
    }

    /// <summary>
    /// The server's copy of the default and folders has to agree with the SPA's, which is what renders an
    /// unset category. Read out of icon.service.ts rather than mirrored, because a mirror is a second copy
    /// of the thing that drifted in #877.
    /// </summary>
    [Fact]
    public void TheServerAgreesWithTheSpaOnTheDefaultPackAndItsFolders()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "Applications", "Pgan.PoracleWebNet.App", "ClientApp", "src", "app", "core", "services", "icon.service.ts"));

        var defaultBase = Regex.Match(source, @"const DEFAULT_UICONS = '([^']+)'").Groups[1].Value;
        Assert.Equal(defaultBase, IconPackDefaults.DefaultBase);

        var block = Regex.Match(source, @"const SOURCES = \{(?<body>[^}]*)\}").Groups["body"].Value;
        var spa = Regex.Matches(block, @"(\w+):\s*'([^']+)'")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.Equal(spa.OrderBy(kv => kv.Key), IconPackDefaults.FolderByKey.OrderBy(kv => kv.Key));
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Pgan.PoracleWebNet.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Solution root not found.");
    }
}
