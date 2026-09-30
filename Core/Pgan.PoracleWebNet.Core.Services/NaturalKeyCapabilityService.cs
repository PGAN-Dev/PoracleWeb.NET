using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Whether PoracleNG's database still refuses a second lure or invasion rule on the natural key.
/// </summary>
/// <remarks>
/// <para>
/// Read from the database itself rather than inferred from a version: <see cref="IPoracleServerProfileService"/>
/// lists the tracking tables that carry a unique key besides their primary key. That is the capability the
/// guards depend on, and it is the only signal that holds on an adopted legacy database, where migration 8
/// drops the keys by name and a key named anything else survives it.
/// </para>
/// <para>
/// When that list could not be read the applied migration stands in (below 8 means the keys are there),
/// and when neither is known the answer is "enforced": the natural-key refusal every release before this one
/// made. Over-refusing a legitimate Add is the defect this replaces, but the alternative on a server that
/// does carry the key is a 500 on every one of those Adds, and on an edit a refused create after a delete.
/// </para>
/// </remarks>
public class NaturalKeyCapabilityService(IPoracleServerProfileService serverProfile) : INaturalKeyCapabilityService
{
    /// <summary>The PoracleNG migration that drops <c>lure_tracking</c> and <c>invasion_tracking</c>.</summary>
    public const long DropsNaturalKeysSchema = 8;

    private readonly IPoracleServerProfileService _serverProfile = serverProfile;

    /// <inheritdoc />
    public async Task<bool> IsEnforcedAsync(string trackingType, CancellationToken cancellationToken = default)
    {
        var table = TableFor(trackingType);
        if (table is null)
        {
            return false;
        }

        try
        {
            var profile = await this._serverProfile.GetAsync(cancellationToken);

            if (profile.UniqueKeyedTrackingTables is { } keyed)
            {
                return keyed.Contains(table, StringComparer.OrdinalIgnoreCase);
            }

            return profile.SchemaVersion is not { } schema || schema < DropsNaturalKeysSchema;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return true;
        }
    }

    /// <summary>
    /// The table behind a tracking type, for the three that ever had a natural key. Pokestop events
    /// (<c>incident</c>) are rows of the <c>invasion</c> table.
    /// </summary>
    internal static string? TableFor(string trackingType) => trackingType switch
    {
        "lure" => "lures",
        "invasion" or "incident" => "invasion",
        _ => null,
    };
}
