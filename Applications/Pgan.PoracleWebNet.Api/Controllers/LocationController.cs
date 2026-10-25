using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/location")]
[RequireFeatureEnabled(DisableFeatureKeys.Location)]
// Assumes every outbound HTTP call here is PoracleNG (static map catches its own errors; geocode lets a PoracleNG below 5.4.0 through as PoracleUnsupportedException); a non-PoracleNG call would need its own handling.
[TypeFilter(typeof(PoracleUnreachableExceptionFilter))]
public class LocationController(
    IHumanService humanService,
    IProfileService profileService,
    IPoracleHumanProxy humanProxy,
    IPoracleApiProxy poracleApiProxy,
    IPairedHumanWriter pairWriter,
    IScannerService? scannerService = null) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IProfileService _profileService = profileService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPairedHumanWriter _pairWriter = pairWriter;
    private readonly IScannerService? _scannerService = scannerService;

    [HttpGet]
    public async Task<IActionResult> GetLocation()
    {
        var profile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, this.ProfileNo);
        if (profile != null)
        {
            return this.Ok(new
            {
                latitude = profile.Latitude,
                longitude = profile.Longitude
            });
        }

        // Fall back to humans table when no profile record exists (most PoracleJS users don't have one)
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

        return this.Ok(new
        {
            latitude = human.Latitude,
            longitude = human.Longitude
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateLocation([FromBody] LocationUpdateRequest request)
    {
        // Verify user exists
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

        // [Required] on nullable doubles already rejects an absent coordinate, so by here both have values.
        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;

        // Single atomic call — PoracleNG handles writing to both humans and profiles tables
        // A linked pair gets the same write on both of its humans.
        var written = await this._pairWriter.WriteAsync(this.UserId, id => this._humanProxy.SetLocationAsync(id, latitude, longitude));

        return this.Ok(this.Paired(new
        {
            latitude,
            longitude
        }, written.Warning));
    }

    /// <summary>
    /// Forward geocode, via PoracleNG's own <c>/api/geocode/forward</c> rather than calling a provider
    /// URL directly: PoracleNG already normalises Nominatim, Photon and Google into one shape, so this
    /// app never parses a provider's payload. See #845.
    /// </summary>
    [RequireFeatureEnabled(DisableFeatureKeys.Geocoding)]
    [HttpGet("geocode")]
    public async Task<IActionResult> Geocode([FromQuery] string q, [FromQuery] string? language = null)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return this.BadRequest("Query parameter 'q' is required");
        }

        var result = await this._poracleApiProxy.GetGeocodeForwardAsync(q, language);
        return result == null
            ? this.StatusCode(503, "Geocoding service unavailable")
            : this.Content(result, "application/json");
    }

    /// <summary>See <see cref="Geocode"/>: PoracleNG's <c>/api/geocode/reverse</c>, not a provider URL.</summary>
    [RequireFeatureEnabled(DisableFeatureKeys.Geocoding)]
    [HttpGet("reverse")]
    public async Task<IActionResult> ReverseGeocode([FromQuery] double lat, [FromQuery] double lon, [FromQuery] string? language = null)
    {
        var result = await this._poracleApiProxy.GetGeocodeReverseAsync(lat, lon, language);
        return result == null
            ? this.StatusCode(503, "Geocoding service unavailable")
            : this.Content(result, "application/json");
    }

    [HttpGet("staticmap")]
    public async Task<IActionResult> GetStaticMap([FromQuery] double lat, [FromQuery] double lon)
    {
        try
        {
            var url = await this._poracleApiProxy.GetLocationMapUrlAsync(lat, lon);
            if (url != null)
            {
                return this.Ok(new
                {
                    url
                });
            }
        }
        catch { }
        return this.NotFound();
    }

    [HttpGet("distancemap")]
    public async Task<IActionResult> GetDistanceMap([FromQuery] double lat, [FromQuery] double lon, [FromQuery] int distance)
    {
        try
        {
            var url = await this._poracleApiProxy.GetDistanceMapUrlAsync(lat, lon, distance);
            if (url != null)
            {
                return this.Ok(new
                {
                    url
                });
            }
        }
        catch { }
        return this.NotFound();
    }

    [HttpGet("weather")]
    public async Task<IActionResult> GetWeather()
    {
        if (this._scannerService == null)
        {
            return this.NoContent();
        }

        var profile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, this.ProfileNo);
        double lat, lon;
        if (profile != null)
        {
            lat = profile.Latitude;
            lon = profile.Longitude;
        }
        else
        {
            var human = await this._humanService.GetByIdAsync(this.UserId);
            if (human == null)
            {
                return this.NoContent();
            }
            lat = human.Latitude;
            lon = human.Longitude;
        }

        if (lat == 0 && lon == 0)
        {
            return this.NoContent();
        }

        var weather = await this._scannerService.GetWeatherAtLocationAsync(lat, lon);
        if (weather == null)
        {
            return this.NoContent();
        }

        return this.Ok(weather);
    }

    [HttpPost("weather/areas")]
    public async Task<IActionResult> GetWeatherForAreas([FromBody] AreaWeatherRequest request)
    {
        if (this._scannerService == null || request.Locations == null || request.Locations.Length == 0)
        {
            return this.Ok(Array.Empty<object>());
        }

        // Compute S2 cell IDs for each location, deduplicating cells
        var locationCells = request.Locations
            .Where(l => l.Lat != 0 || l.Lon != 0)
            .Select(l => new { l.Name, CellId = Core.Services.S2CellHelper.LatLonToWeatherCellId(l.Lat, l.Lon) })
            .ToList();

        var uniqueCellIds = locationCells.Select(l => l.CellId).Distinct();
        var weatherByCell = await this._scannerService.GetWeatherForCellsAsync(uniqueCellIds);

        // Map back to area names
        var results = locationCells
            .Where(l => weatherByCell.ContainsKey(l.CellId))
            .Select(l => new { name = l.Name, weather = weatherByCell[l.CellId] })
            .ToList();

        return this.Ok(results);
    }

    public class AreaWeatherRequest
    {
        public AreaLocation[] Locations { get; set; } = [];
    }

    public class AreaLocation
    {
        public string Name { get; set; } = string.Empty;
        public double Lat
        {
            get; set;
        }
        public double Lon
        {
            get; set;
        }
    }

    public class LocationUpdateRequest
    {
        /// <remarks>
        /// Unbounded doubles were written straight to humans.latitude/longitude and the active profile, so
        /// a location off the globe persisted and then failed silently downstream: weather returned 204 and
        /// the static map 404 with no explanation, distance matching ran against a point that does not
        /// exist, and the active-hours scheduler's timezone lookup was meaningless. 1e308 additionally
        /// produced a 500 rather than a 400. See #423.
        /// </remarks>
        /// <remarks>
        /// Nullable so that "absent" is distinguishable from "zero". As non-nullable doubles both members
        /// bound to 0.0 when the request omitted them, [Range] passed, and 0,0 was written over the real
        /// location -- exactly the outcome the remarks above describe as the harm this validation exists to
        /// prevent, reached by the one path the validation could not see. See #480.
        /// </remarks>
        [Required(ErrorMessage = "Latitude is required.")]
        [Range(-90.0, 90.0, ErrorMessage = "Latitude must be between -90 and 90.")]
        public double? Latitude
        {
            get; set;
        }

        [Required(ErrorMessage = "Longitude is required.")]
        [Range(-180.0, 180.0, ErrorMessage = "Longitude must be between -180 and 180.")]
        public double? Longitude
        {
            get; set;
        }
    }



    /// <summary>
    /// The user's saved places, plus the profile pin every alarm falls back to.
    /// </summary>
    /// <remarks>
    /// There is no <c>canEdit</c> flag: moving a place works on every supported PoracleNG (5.4.0 and up).
    /// </remarks>
    [HttpGet("places")]
    public async Task<IActionResult> GetPlaces() =>
        this.Ok(await this.PlacesAsync());

    /// <summary>
    /// The place list in the one shape every caller gets; the PUT and POST answer it too.
    /// </summary>
    private async Task<object> PlacesAsync()
    {
        var places = await this._humanProxy.GetPlacesAsync(this.UserId);

        return new
        {
            places.Default,
            places.Named,
        };
    }

    /// <summary>
    /// Moves a saved place, keeping its label so every alarm pointing at it follows.
    /// </summary>
    /// <remarks>
    /// New on PoracleNG 5.2.0. Before it, a place an alarm referenced could not be moved at all: the
    /// delete answers 409 while anything still points at it, so the only route was to repoint every
    /// alarm, delete, re-add and repoint back. A server older than 5.4.0 raises
    /// <see cref="PoracleUnsupportedException"/>, which the exception filter reports.
    /// </remarks>
    [HttpPut("places/{label}")]
    public async Task<IActionResult> UpdatePlace(
        string label, [FromBody] PlaceMoveRequest request)
    {
        if (request.Latitude is not { } latitude || request.Longitude is not { } longitude)
        {
            return this.BadRequest(new { error = "Latitude and longitude are required." });
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return this.BadRequest(new { error = "Latitude must be -90 to 90 and longitude -180 to 180." });
        }

        var written = await this._pairWriter.WriteAsync(this.UserId, id => this._humanProxy.UpdatePlaceAsync(id, label, latitude, longitude));

        return this.Ok(this.Paired(await this.PlacesAsync(), written.Warning));
    }

    /// <summary>New coordinates for a saved place. The label is the path segment and does not change.</summary>
    public class PlaceMoveRequest
    {
        [Range(-90, 90)]
        public double? Latitude
        {
            get; set;
        }

        [Range(-180, 180)]
        public double? Longitude
        {
            get; set;
        }
    }

    /// <summary>
    /// Saves a place an alarm can be anchored to.
    /// </summary>
    /// <remarks>
    /// A refusal, ours or PoracleNG's, is returned as a 400 <c>{ error }</c> the SPA can show as it is.
    /// The label rule (<see cref="PlaceLabelRules"/>) runs here rather than as model attributes, because an
    /// attribute's refusal arrives as a validation-problem body the SPA could not read, and it judges this new
    /// label only: moving and deleting address the stored label, so a place saved before the rule stays usable.
    /// </remarks>
    [HttpPost("places")]
    public async Task<IActionResult> AddPlace(
        [FromBody] SavedPlace place)
    {
        if (PlaceLabelRules.Validate(place.Label) is { } invalid)
        {
            return this.BadRequest(new { error = invalid });
        }

        place.Label = place.Label.Trim();

        // A refusal on the signed-in side is today's 400 and stops there; on the other side it is a warning.
        PairWriteResult<bool> written;
        try
        {
            written = await this._pairWriter.WriteAsync(this.UserId, async id =>
            {
                var refusal = await this._humanProxy.AddPlaceAsync(id, place);
                if (refusal is not null)
                {
                    throw new PlaceRefusedException(refusal);
                }
            });
        }
        catch (PlaceRefusedException ex)
        {
            return this.BadRequest(new { error = ex.Message });
        }

        // Answers the same shape as the GET and the PUT; the SPA replaces its places signal from it.
        return this.Ok(this.Paired(await this.PlacesAsync(), written.Warning));
    }

    private sealed class PlaceRefusedException(string reason) : Exception(reason);

    /// <summary>
    /// How many of the active profile's alarms measure their radius from the profile pin: a radius above
    /// zero and no saved place. Clearing the pin stops every one of them matching, so the SPA names the
    /// count before it asks to clear. A rule aimed at a saved place keeps its anchor and is not counted.
    /// </summary>
    [EnableRateLimiting("tracking-usage")]
    [HttpGet("pin-usage")]
    public async Task<IActionResult> GetPinUsage(
        [FromServices] IPoracleTrackingProxy trackingProxy, [FromServices] ILinkedAccount linked)
    {
        // Both humans of a linked pair: clearing the pin is person-level, so the count has to cover them all.
        var pair = await linked.ResolveAsync(this.UserId);
        var alarms = 0;
        foreach (var humanId in pair.ExistingHumanIds)
        {
            alarms += PlaceUsage.CountMeasuredFromPin(await trackingProxy.GetAllTrackingAsync(humanId));
        }

        return this.Ok(new { alarms });
    }

    /// <summary>
    /// The active profile's alarms aimed at a saved place, every type included (Pokéstop events too).
    /// PoracleNG refuses to delete a place while any exist, so the SPA reads this before it offers Delete
    /// and lists them instead. The delete's own 409 still covers a rule added in between.
    /// </summary>
    [EnableRateLimiting("tracking-usage")]
    [HttpGet("place-usage")]
    public async Task<IActionResult> GetPlaceUsage(
        [FromQuery] string? label, [FromServices] IPoracleTrackingProxy trackingProxy, [FromServices] ILinkedAccount linked)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return this.BadRequest(new { error = "Name the place to check." });
        }

        var pair = await linked.ResolveAsync(this.UserId);
        var rules = new List<PlaceUsageRule>();
        foreach (var humanId in pair.ExistingHumanIds)
        {
            var snapshot = await trackingProxy.GetAllTrackingWithDescriptionsAsync(humanId);
            var side = pair.SideOf(humanId);
            rules.AddRange(PlaceUsage.RulesAimedAt(snapshot, label)
                .Select(r => side is { } s ? r with { Destination = AccountLinkKeys.Wire(s) } : r));
        }

        return this.Ok(new { alarms = rules.Count, rules });
    }

    /// <summary>
    /// Deletes a saved place, unless alarms still point at it.
    /// </summary>
    [HttpDelete("places/{label}")]
    public async Task<IActionResult> DeletePlace(string label)
    {
        PairWriteResult<bool> written;
        try
        {
            written = await this._pairWriter.WriteAsync(this.UserId, id => this._humanProxy.DeletePlaceAsync(id, label));
        }
        catch (PlaceInUseException ex)
        {
            // Naming the alarms is the difference between "could not delete" and a person knowing what
            // to repoint first.
            return this.Conflict(new { error = ex.Message, referencingRules = ex.ReferencingRules });
        }

        return this.NoContentPaired(written.Warning);
    }
}
