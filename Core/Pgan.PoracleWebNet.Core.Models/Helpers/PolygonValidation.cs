namespace Pgan.PoracleWebNet.Core.Models.Helpers;

/// <summary>
/// The one definition of a well-formed user geofence polygon.
/// </summary>
/// <remarks>
/// <para>
/// There are two write paths into <c>user_geofences</c> — the create endpoint and GeoJSON import — and
/// they enforced different rules. Import checked point arity and coordinate range; create checked only
/// the point count, so <c>[[1],[2],[3]]</c> and <c>[[999,-999],...]</c> were stored verbatim. Those rows
/// then reached <c>GET /api/geofence-feed</c>, the anonymous endpoint that is the single geofence source
/// for PoracleJS, and crashed the owner's GeoJSON export with an <c>IndexOutOfRangeException</c>. See #410.
/// </para>
/// <para>
/// Read paths use <see cref="IsWellFormed"/> to skip bad rows rather than trust them: rows written before
/// this validation existed are still in the database, and one of them must not be able to break a feed
/// that every other user depends on.
/// </para>
/// <para>Points are internal order — <c>[latitude, longitude]</c>, not GeoJSON's <c>[lon, lat]</c>.</para>
/// </remarks>
public static class PolygonValidation
{
    public const int MinPoints = 3;
    public const int MaxPoints = 500;

    /// <summary>
    /// Validates a polygon for storage. <paramref name="error"/> is a message suitable for returning to
    /// the caller, phrased the same way the import path phrases its rejections.
    /// </summary>
    public static bool TryValidate(double[][]? polygon, out string error)
    {
        if (polygon is null || polygon.Length < MinPoints)
        {
            error = $"Polygon must have at least {MinPoints} points.";
            return false;
        }

        if (polygon.Length > MaxPoints)
        {
            error = $"Polygon cannot exceed {MaxPoints} points.";
            return false;
        }

        for (var i = 0; i < polygon.Length; i++)
        {
            var point = polygon[i];

            if (point is null || point.Length != 2)
            {
                error = $"Polygon point {i} must be a [latitude, longitude] pair.";
                return false;
            }

            if (double.IsNaN(point[0]) || double.IsNaN(point[1]) ||
                double.IsInfinity(point[0]) || double.IsInfinity(point[1]))
            {
                error = $"Polygon point {i} is not a finite coordinate.";
                return false;
            }

            if (point[0] is < -90 or > 90 || point[1] is < -180 or > 180)
            {
                error = "Coordinates out of valid range (lat: -90 to 90, lon: -180 to 180).";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether a polygon already in storage is safe to serve or project. Used by read paths, which cannot
    /// assume the row was written after <see cref="TryValidate"/> existed.
    /// </summary>
    public static bool IsWellFormed(double[][]? polygon) => TryValidate(polygon, out _);
}
