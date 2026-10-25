using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Services;

internal static class SiteSettingExtensions
{
    /// <summary>True only when the setting is stored as "false"; absent means on. The <c>enable_*</c> sign-in switches.</summary>
    public static async Task<bool> IsSwitchedOffAsync(this ISiteSettingService settings, string key) =>
        string.Equals(await settings.GetValueAsync(key), "false", StringComparison.OrdinalIgnoreCase);
}
