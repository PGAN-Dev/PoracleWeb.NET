using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/location")]
[RequireFeatureEnabled(DisableFeatureKeys.Location)]
public class LocationController(
    IHumanService humanService,
    IProfileService profileService,
    IPoracleHumanProxy humanProxy,
    IPoracleApiProxy poracleApiProxy,
    IHttpClientFactory httpClientFactory,
    IPlaceUpdateCapabilityService placeUpdateCapability,
    IScannerService? scannerService = null) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IProfileService _profileService = profileService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IPlaceUpdateCapabilityService _placeUpdateCapability = placeUpdateCapability;
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
        await this._humanProxy.SetLocationAsync(this.UserId, latitude, longitude);

        return this.Ok(new
        {
            latitude,
            longitude
        });
    }

    [RequireFeatureEnabled(DisableFeatureKeys.Geocoding)]
    [HttpGet("geocode")]
    public async Task<IActionResult> Geocode([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return this.BadRequest("Query parameter 'q' is required");
        }

        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            if (config == null || string.IsNullOrEmpty(config.ProviderUrl))
            {
                return this.BadRequest("Geocoding not available - no provider configured");
            }

            var client = this._httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var url = $"{config.ProviderUrl.TrimEnd('/')}/search?addressdetails=1&q={Uri.EscapeDataString(q)}&format=json&limit=5";
            var response = await client.GetStringAsync(url);
            return this.Content(response, "application/json");
        }
        catch (Exception)
        {
            return this.StatusCode(503, "Geocoding service unavailable");
        }
    }

    [RequireFeatureEnabled(DisableFeatureKeys.Geocoding)]
    [HttpGet("reverse")]
    public async Task<IActionResult> ReverseGeocode([FromQuery] double lat, [FromQuery] double lon)
    {
        try
        {
            var config = await this._poracleApiProxy.GetConfigAsync();
            if (config == null || string.IsNullOrEmpty(config.ProviderUrl))
            {
                return this.BadRequest("Geocoding not available - no provider configured");
            }

            var client = this._httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var url = $"{config.ProviderUrl.TrimEnd('/')}/reverse?lat={lat}&lon={lon}&format=json&addressdetails=1";
            var response = await client.GetStringAsync(url);
            return this.Content(response, "application/json");
        }
        catch (Exception)
        {
            return this.StatusCode(503, "Geocoding service unavailable");
        }
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
    /// <c>canEdit</c> rides along rather than sitting on its own endpoint, matching
    /// <c>MuteController</c>: the list is read on every visit to the Areas page, and a second call for
    /// one boolean would double that for no gain.
    /// </remarks>
    [HttpGet("places")]
    public async Task<IActionResult> GetPlaces(CancellationToken cancellationToken) =>
        this.Ok(await this.PlacesWithCapabilityAsync(cancellationToken));

    /// <summary>
    /// The place list in the one shape every caller gets, capability included. The PUT answers it too:
    /// a reply missing canEdit would clear the flag the SPA is holding and hide the control the user
    /// just used.
    /// </summary>
    private async Task<object> PlacesWithCapabilityAsync(CancellationToken cancellationToken)
    {
        var places = await this._humanProxy.GetPlacesAsync(this.UserId);

        return new
        {
            places.Default,
            places.Named,
            canEdit = await this._placeUpdateCapability.IsPlaceUpdateAvailableAsync(cancellationToken),
        };
    }

    /// <summary>
    /// Moves a saved place, keeping its label so every alarm pointing at it follows.
    /// </summary>
    /// <remarks>
    /// New on PoracleNG 5.2.0. Before it, a place an alarm referenced could not be moved at all: the
    /// delete answers 409 while anything still points at it, so the only route was to repoint every
    /// alarm, delete, re-add and repoint back. An older server answers 501 and the SPA hides the control.
    /// </remarks>
    [HttpPut("places/{label}")]
    public async Task<IActionResult> UpdatePlace(
        string label, [FromBody] PlaceMoveRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Latitude is not { } latitude || request.Longitude is not { } longitude)
        {
            return this.BadRequest(new { error = "Latitude and longitude are required." });
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return this.BadRequest(new { error = "Latitude must be -90 to 90 and longitude -180 to 180." });
        }

        var moved = await this._humanProxy.UpdatePlaceAsync(this.UserId, label, latitude, longitude);

        return moved
            ? this.Ok(await this.PlacesWithCapabilityAsync(cancellationToken))
            : this.StatusCode(StatusCodes.Status501NotImplemented, new
            {
                error = "This Poracle server cannot move a saved place. Delete it and add it again.",
            });
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
    /// PoracleNG reports a rejected label inside a 200 because its endpoint answers per row, so the
    /// refusal is unwrapped here and returned as a 400 the SPA can show against the field.
    /// </remarks>
    [HttpPost("places")]
    public async Task<IActionResult> AddPlace(
        [FromBody] SavedPlace place, CancellationToken cancellationToken = default)
    {
        var refusal = await this._humanProxy.AddPlaceAsync(this.UserId, place);

        // Answers the same shape as the GET and the PUT. The SPA replaces its whole places signal
        // from this reply, so a body without canEdit cleared the flag and took the edit control off
        // every card until the next reload -- the hazard already noted on the PUT, one path along.
        return refusal is null
            ? this.Ok(await this.PlacesWithCapabilityAsync(cancellationToken))
            : this.BadRequest(new { error = refusal });
    }

    /// <summary>
    /// Deletes a saved place, unless alarms still point at it.
    /// </summary>
    [HttpDelete("places/{label}")]
    public async Task<IActionResult> DeletePlace(string label)
    {
        try
        {
            await this._humanProxy.DeletePlaceAsync(this.UserId, label);
        }
        catch (PlaceInUseException ex)
        {
            // Naming the alarms is the difference between "could not delete" and a person knowing what
            // to repoint first.
            return this.Conflict(new { error = ex.Message, referencingRules = ex.ReferencingRules });
        }

        return this.NoContent();
    }
}
