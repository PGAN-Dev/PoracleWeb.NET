using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Carries forward the properties of a stored alarm that PoracleWeb has no model for.
/// </summary>
/// <remarks>
/// <para>
/// An edit is sent as a create carrying the existing <c>uid</c>, which PoracleNG upserts. The body is
/// built by serializing the typed model, so every property the model does not declare was absent from the
/// write and PoracleNG stored the column's default over the user's value. PoracleNG 5.1.0 added
/// <c>override_location_label</c>, <c>override_areas</c> and <c>pvp_ranking_evolution</c>; 5.2.0 adds
/// <c>costume</c>. Set any of them with the bot, edit the alarm on the web, and they were gone. See #730.
/// </para>
/// <para>
/// This runs BEFORE the collision guards on purpose. <c>CountUpdatableDifferences</c> only compares the
/// properties present in the submission, so an unmodelled property could not tell two alarms apart and
/// the guard refused edits PoracleNG would have accepted, which is the #553 shape. Merging first means
/// the guard compares the row that is actually going to be written.
/// </para>
/// </remarks>
internal static class TrackingFieldPreserver
{
    /// <summary>
    /// Returns <paramref name="body"/> with any property the stored row carries and it does not.
    /// </summary>
    /// <remarks>
    /// A read failure returns the body untouched. Losing an override on an edit is bad; failing every
    /// edit whenever a read hiccups is worse, and the guards downstream still run either way.
    /// </remarks>
    public static async Task<JsonElement> PreserveStoredFieldsAsync(
        IPoracleTrackingProxy proxy,
        string trackingType,
        string userId,
        int uid,
        JsonElement body)
    {
        // uid <= 0 is a create: there is no stored row to carry anything forward from.
        if (uid <= 0 || body.ValueKind != JsonValueKind.Object)
        {
            return body;
        }

        JsonElement rows;
        try
        {
            rows = await proxy.GetByUserAsync(trackingType, userId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return body;
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return body;
        }

        foreach (var row in rows.EnumerateArray())
        {
            if (PoracleJsonHelper.UidOf(row) == uid)
            {
                return PoracleJsonHelper.PreserveUnmodelled(row, body);
            }
        }

        return body;
    }
}
