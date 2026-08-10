namespace Pgan.PoracleWebNet.Api.Configuration;

/// <summary>
/// Normalises the optional <c>PUBLIC_URL</c> setting into a bare origin (<c>scheme://host[:port]</c>).
///
/// OAuth providers require the callback URI to be registered in advance and to match byte-for-byte
/// between the authorize request and the token exchange, so it cannot be safely derived from the
/// incoming request when the deployment sits behind a proxy that has not been declared -- the scheme
/// comes back as <c>http</c> and the provider rejects the sign-in. Setting <c>PUBLIC_URL</c> states
/// the answer outright instead.
///
/// Left unset, callers fall back to the request scheme and host, which is the historical behaviour
/// and remains correct for a directly-exposed instance or one whose proxy is declared via
/// <c>PROXY_KNOWN_PROXIES</c> / <c>PROXY_KNOWN_NETWORKS</c>.
/// </summary>
internal static class PublicOrigin
{
    /// <summary>
    /// Validates a configured public URL. Returns <c>false</c> with a non-null <paramref name="error"/>
    /// when the value is present but unusable, and <c>false</c> with a null error when simply unset.
    /// </summary>
    public static bool TryNormalize(string? configured, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var trimmed = configured.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = $"'{trimmed}' is not an absolute URL. Expected something like https://poracle.example.com.";
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            error = $"'{trimmed}' uses the '{uri.Scheme}' scheme. Only http and https are supported.";
            return false;
        }

        // A path would silently produce callback URIs like https://host/poracle/api/auth/... which is
        // never what the app serves, so refuse it rather than emit a URI the provider will reject.
        if (uri.AbsolutePath.Trim('/').Length > 0 || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = $"'{trimmed}' must be an origin only, with no path, query or fragment " +
                    $"(e.g. {uri.Scheme}://{uri.Authority}).";
            return false;
        }

        normalized = $"{uri.Scheme}://{uri.Authority}";
        return true;
    }

    /// <summary>Returns the normalised origin, or null when unset or unusable.</summary>
    public static string? NormalizeOrNull(string? configured) =>
        TryNormalize(configured, out var normalized, out _) ? normalized : null;
}
