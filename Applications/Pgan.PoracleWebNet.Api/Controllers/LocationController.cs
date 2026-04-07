using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/location")]
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

        // Single atomic call — PoracleNG handles writing to both humans and profiles tables
        await this._humanProxy.SetLocationAsync(this.UserId, request.Latitude, request.Longitude);

        return this.Ok(new
        {
            latitude = request.Latitude,
            longitude = request.Longitude
        });
    }

    [HttpPut("language")]
    public async Task<IActionResult> UpdateLanguage([FromBody] LanguageUpdateRequest request)
    {
        var human = await this._humanService.GetByIdAndProfileAsync(this.UserId, this.ProfileNo);
        if (human == null)
        {
            return this.NotFound();
        }

        human.Language = request.Language;
        await this._humanService.UpdateAsync(human);

        return this.Ok(new
        {
            language = human.Language
        });
    }

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
        public double Latitude
        {
            get; set;
        }
        public double Longitude
        {
            get; set;
        }
    }

    public class LanguageUpdateRequest
    {
        public string Language { get; set; } = string.Empty;
    }
}
