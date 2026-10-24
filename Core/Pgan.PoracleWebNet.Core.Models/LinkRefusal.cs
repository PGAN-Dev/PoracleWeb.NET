namespace Pgan.PoracleWebNet.Core.Models;

public enum LinkRefusalCode
{
    WrongType,
    BadId,
    TypeMismatch,
    Taken,
    Expired,
    NotPermitted,
    ProvisionFailed,
    LinkFailed,
    LinkCancelled,
    LinkDisabled,
}

/// <summary>A link was refused. <see cref="WireCode"/> is the machine-readable code the SPA reads.</summary>
public class LinkRefusalException(LinkRefusalCode code, string message, Exception? inner = null) : Exception(message, inner)
{
    public LinkRefusalCode Code { get; } = code;

    public string WireCode => Wire(this.Code);

    public static string Wire(LinkRefusalCode code) => code switch
    {
        LinkRefusalCode.WrongType => "link_wrong_type",
        LinkRefusalCode.BadId => "link_bad_id",
        LinkRefusalCode.TypeMismatch => "link_type_mismatch",
        LinkRefusalCode.Taken => "link_taken",
        LinkRefusalCode.Expired => "link_expired",
        LinkRefusalCode.NotPermitted => "link_not_permitted",
        LinkRefusalCode.ProvisionFailed => "link_provision_failed",
        LinkRefusalCode.LinkFailed => "link_failed",
        LinkRefusalCode.LinkCancelled => "link_cancelled",
        LinkRefusalCode.LinkDisabled => "link_disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };
}
