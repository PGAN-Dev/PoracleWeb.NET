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
    IScannerService? scannerService = null) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IProfileService _profileService = profileService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
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
    [HttpGet("places")]
    public async Task<IActionResult> GetPlaces() =>
        this.Ok(await this._humanProxy.GetPlacesAsync(this.UserId));

    /// <summary>
    /// Saves a place an alarm can be anchored to.
    /// </summary>
    /// <remarks>
    /// PoracleNG reports a rejected label inside a 200 because its endpoint answers per row, so the
    /// refusal is unwrapped here and returned as a 400 the SPA can show against the field.
    /// </remarks>
    [HttpPost("places")]
    public async Task<IActionResult> AddPlace([FromBody] SavedPlace place)
    {
        var refusal = await this._humanProxy.AddPlaceAsync(this.UserId, place);

        return refusal is null
            ? this.Ok(await this._humanProxy.GetPlacesAsync(this.UserId))
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
