namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>What happens when a person links an account that has no Poracle human yet. See <see cref="AccountLinkKeys.RegisterMissing"/>.</summary>
public enum LinkRegisterMode
{
    /// <summary>The link waits until the person registers with Poracle the usual way.</summary>
    Off,

    /// <summary>
    /// The human is created only when Poracle's own registration rule would admit the person. Telegram only:
    /// a Discord side that is missing waits, as under <see cref="Off"/>.
    /// </summary>
    WhenQualified,

    /// <summary>The human is created at once, whatever Poracle's registration rule says.</summary>
    Always,
}

public static class LinkRegisterModes
{
    public const string OffWire = "off";
    public const string WhenQualifiedWire = "when-qualified";
    public const string AlwaysWire = "always";

    /// <summary>
    /// Reads the stored setting. The key was a boolean before it had three values, so "true" is <see cref="LinkRegisterMode.Always"/>
    /// and "false" or absent is <see cref="LinkRegisterMode.Off"/>. Anything unrecognised is off: a typo must never open registration.
    /// </summary>
    public static LinkRegisterMode Parse(string? stored)
    {
        var value = (stored ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "true" or AlwaysWire => LinkRegisterMode.Always,
            WhenQualifiedWire => LinkRegisterMode.WhenQualified,
            _ => LinkRegisterMode.Off,
        };
    }

    public static string Wire(LinkRegisterMode mode) => mode switch
    {
        LinkRegisterMode.Always => AlwaysWire,
        LinkRegisterMode.WhenQualified => WhenQualifiedWire,
        _ => OffWire,
    };
}
