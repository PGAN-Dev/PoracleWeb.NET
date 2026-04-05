namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Valid option values for <see cref="FortChange"/> fields.
/// Mirrors the values accepted by PoracleNG's fort tracking endpoints.
/// </summary>
public static class FortChangeOptions
{
    public const string FortTypePokestop = "pokestop";
    public const string FortTypeGym = "gym";
    public const string FortTypeEverything = "everything";

    public const string ChangeTypeName = "name";
    public const string ChangeTypeLocation = "location";
    public const string ChangeTypeImageUrl = "image_url";
    public const string ChangeTypeRemoval = "removal";
    public const string ChangeTypeNew = "new";

    public static readonly IReadOnlySet<string> ValidFortTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        FortTypePokestop,
        FortTypeGym,
        FortTypeEverything,
    };

    public static readonly IReadOnlySet<string> ValidChangeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        ChangeTypeName,
        ChangeTypeLocation,
        ChangeTypeImageUrl,
        ChangeTypeRemoval,
        ChangeTypeNew,
    };
}
