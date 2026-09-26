namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>
/// Optional overrides for the address search and reverse lookup. Both are empty by default, which keeps
/// the old behaviour: the geocoder URL comes from PoracleNG's <c>providerURL</c> and is spoken to as
/// Nominatim.
/// </summary>
public class GeocodingSettings
{
    /// <summary>
    /// <c>nominatim</c> (default) or <c>photon</c>.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Geocoder base URL as this app can reach it. PoracleNG's <c>provider_url</c> is written for the bot,
    /// so a <c>localhost</c> address there points at the wrong machine from inside a container.
    /// </summary>
    public string ProviderUrl { get; set; } = string.Empty;
}
