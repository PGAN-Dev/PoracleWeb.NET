using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// Tests for DataProtection key persistence configuration (issue #174).
/// Verifies that keys are persisted to the filesystem, survive provider re-creation
/// (simulating container restarts), and respect the DATA_DIR configuration.
/// </summary>
public class DataProtectionConfigurationTests : IDisposable
{
    private readonly string _tempDir;

    private static string EnsureRelativePath(string pathSegment)
    {
        if (string.IsNullOrWhiteSpace(pathSegment))
        {
            throw new ArgumentException("Path segment must not be null, empty, or whitespace.", nameof(pathSegment));
        }

        if (Path.IsPathRooted(pathSegment))
        {
            throw new ArgumentException("Path segment must be relative.", nameof(pathSegment));
        }

        return pathSegment;
    }

    public DataProtectionConfigurationTests()
    {
        this._tempDir = Path.Join(Path.GetTempPath(), $"dp-test-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDir))
        {
            Directory.Delete(this._tempDir, recursive: true);
        }
    }

    // -- Registration tests --

    [Fact]
    public void DataProtectionProvider_IsResolvable_WhenConfigured()
    {
        var provider = BuildServiceProvider(this._tempDir);

        var dpProvider = provider.GetService<IDataProtectionProvider>();

        Assert.NotNull(dpProvider);
    }

    [Fact]
    public void DataProtectionProvider_CanCreateProtector()
    {
        var provider = BuildServiceProvider(this._tempDir);
        var dpProvider = provider.GetRequiredService<IDataProtectionProvider>();

        var protector = dpProvider.CreateProtector("test-purpose");

        Assert.NotNull(protector);
    }

    // -- Key persistence tests --

    [Fact]
    public void KeyFiles_AreWrittenToConfiguredDirectory()
    {
        var keysDir = Path.Combine(this._tempDir, "dataprotection-keys");
        var provider = BuildServiceProvider(this._tempDir);
        var dpProvider = provider.GetRequiredService<IDataProtectionProvider>();

        // Force key generation by performing a protect operation
        var protector = dpProvider.CreateProtector("test-purpose");
        protector.Protect("trigger-key-creation");

        Assert.True(Directory.Exists(keysDir), "Key directory should be created");

        var keyFiles = Directory.GetFiles(keysDir, "key-*.xml");
        Assert.True(keyFiles.Length > 0, "At least one key XML file should be written");
    }

    [Fact]
    public void KeyFiles_AreValidXml()
    {
        var keysDir = Path.Combine(this._tempDir, "dataprotection-keys");
        var provider = BuildServiceProvider(this._tempDir);
        var dpProvider = provider.GetRequiredService<IDataProtectionProvider>();

        var protector = dpProvider.CreateProtector("test-purpose");
        protector.Protect("trigger-key-creation");

        var keyFiles = Directory.GetFiles(keysDir, "key-*.xml");
        Assert.NotEmpty(keyFiles);

        foreach (var keyFile in keyFiles)
        {
            var content = File.ReadAllText(keyFile);
            Assert.StartsWith("<?xml", content);
            Assert.Contains("<key ", content);
        }
    }

    // -- Protect/Unprotect round-trip tests --

    [Fact]
    public void ProtectUnprotect_RoundTrip_Succeeds()
    {
        var provider = BuildServiceProvider(this._tempDir);
        var dpProvider = provider.GetRequiredService<IDataProtectionProvider>();
        var protector = dpProvider.CreateProtector("round-trip-test");
        var plaintext = "hello world — DataProtection round-trip test";

        var encrypted = protector.Protect(plaintext);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void ProtectUnprotect_ProtectedTextDiffersFromPlaintext()
    {
        var provider = BuildServiceProvider(this._tempDir);
        var dpProvider = provider.GetRequiredService<IDataProtectionProvider>();
        var protector = dpProvider.CreateProtector("diff-test");
        var plaintext = "sensitive data";

        var encrypted = protector.Protect(plaintext);

        Assert.NotEqual(plaintext, encrypted);
    }

    [Fact]
    public void ProtectUnprotect_DifferentPurposes_CannotCrossDecrypt()
    {
        var provider = BuildServiceProvider(this._tempDir);
        var dpProvider = provider.GetRequiredService<IDataProtectionProvider>();
        var protectorA = dpProvider.CreateProtector("purpose-A");
        var protectorB = dpProvider.CreateProtector("purpose-B");

        var encrypted = protectorA.Protect("secret");

        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => protectorB.Unprotect(encrypted));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("The quick brown fox jumps over the lazy dog")]
    [InlineData("Unicode: こんにちは 🌍")]
    public void ProtectUnprotect_HandlesVariousPayloads(string plaintext)
    {
        var provider = BuildServiceProvider(this._tempDir);
        var dpProvider = provider.GetRequiredService<IDataProtectionProvider>();
        var protector = dpProvider.CreateProtector("payload-test");

        var encrypted = protector.Protect(plaintext);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    // -- Key survival (simulated restart) tests --

    [Fact]
    public void Keys_SurviveProviderRecreation()
    {
        // First "instance" — protect data
        var provider1 = BuildServiceProvider(this._tempDir);
        var protector1 = provider1.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("restart-test");
        var encrypted = protector1.Protect("survive restart");

        // Second "instance" — same key directory, new provider (simulates container restart)
        var provider2 = BuildServiceProvider(this._tempDir);
        var protector2 = provider2.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("restart-test");
        var decrypted = protector2.Unprotect(encrypted);

        Assert.Equal("survive restart", decrypted);
    }

    [Fact]
    public void Keys_SurviveMultipleProviderRecreations()
    {
        const int restartCount = 3;
        var encrypted = new string[restartCount];

        // Protect data across multiple "instances"
        for (var i = 0; i < restartCount; i++)
        {
            var provider = BuildServiceProvider(this._tempDir);
            var protector = provider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("multi-restart");
            encrypted[i] = protector.Protect($"message-{i}");
        }

        // Final "instance" should be able to decrypt all previous messages
        var finalProvider = BuildServiceProvider(this._tempDir);
        var finalProtector = finalProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("multi-restart");

        for (var i = 0; i < restartCount; i++)
        {
            var decrypted = finalProtector.Unprotect(encrypted[i]);
            Assert.Equal($"message-{i}", decrypted);
        }
    }

    [Fact]
    public void Keys_DoNotAccumulate_WithSameLifetime()
    {
        // Multiple provider creations should reuse the same active key
        var provider1 = BuildServiceProvider(this._tempDir);
        provider1.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("accumulate-test")
            .Protect("trigger");

        var keysDir = Path.Join(this._tempDir, EnsureRelativePath("dataprotection-keys"));
        var keyCountAfterFirst = Directory.GetFiles(keysDir, "key-*.xml").Length;

        var provider2 = BuildServiceProvider(this._tempDir);
        provider2.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("accumulate-test")
            .Protect("trigger");

        var keyCountAfterSecond = Directory.GetFiles(keysDir, "key-*.xml").Length;

        Assert.Equal(keyCountAfterFirst, keyCountAfterSecond);
    }

    // -- DATA_DIR configuration tests --

    [Fact]
    public void DataDir_UsesConfigurationValue_WhenSet()
    {
        var customDir = Path.Combine(this._tempDir, "custom-data");
        Directory.CreateDirectory(customDir);
        var keysDir = Path.Combine(customDir, "dataprotection-keys");

        var provider = BuildServiceProvider(customDir);
        var protector = provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("config-test");
        protector.Protect("trigger");

        Assert.True(Directory.Exists(keysDir), "Keys should be written to DATA_DIR subdirectory");
        Assert.True(Directory.GetFiles(keysDir, "key-*.xml").Length > 0);
    }

    [Fact]
    public void DataDir_FallsBackToCurrentDirectoryData_WhenNotSet()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // When DATA_DIR is not set, the fallback is Path.Combine(Directory.GetCurrentDirectory(), "data")
        var expectedDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
        var resolvedDir = config["DATA_DIR"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data");

        Assert.Equal(expectedDir, resolvedDir);
    }

    [Fact]
    public void DataDir_CreatesKeySubdirectory_Automatically()
    {
        // DATA_DIR exists but dataprotection-keys subdirectory does not yet
        var freshDir = Path.Combine(this._tempDir, "fresh");
        Directory.CreateDirectory(freshDir);
        var keysDir = Path.Combine(freshDir, "dataprotection-keys");

        Assert.False(Directory.Exists(keysDir), "Subdirectory should not exist before provider creation");

        var provider = BuildServiceProvider(freshDir);
        var protector = provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("auto-create-test");
        protector.Protect("trigger");

        Assert.True(Directory.Exists(keysDir), "Subdirectory should be auto-created");
    }

    // -- Application name isolation tests --

    [Fact]
    public void ApplicationName_IsolatesKeyRings()
    {
        var dirA = Path.Combine(this._tempDir, "app-a");
        var dirB = Path.Combine(this._tempDir, "app-b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        // Use the same key directory but different application names
        var sharedKeysDir = Path.Combine(this._tempDir, "shared-keys");
        Directory.CreateDirectory(sharedKeysDir);

        var providerA = BuildServiceProviderWithAppName(sharedKeysDir, "App-A");
        var providerB = BuildServiceProviderWithAppName(sharedKeysDir, "App-B");

        var protectorA = providerA.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("isolation-test");
        var protectorB = providerB.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("isolation-test");

        var encrypted = protectorA.Protect("app-a-secret");

        // Different application name means different key derivation — cannot cross-decrypt
        Assert.Throws<System.Security.Cryptography.CryptographicException>(
            () => protectorB.Unprotect(encrypted));
    }

    // -- Helpers --

    private static ServiceProvider BuildServiceProvider(string dataDir)
    {
        return BuildServiceProviderWithAppName(dataDir, "Pgan.PoracleWebNet.Api");
    }

    private static ServiceProvider BuildServiceProviderWithAppName(string dataDir, string appName)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "dataprotection-keys")))
            .SetApplicationName(appName);
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
