using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/location")]
public class LocationController(
    IHumanService humanService,
    IProfileService profileService,
    IPoracleApiProxy poracleApiProxy,
    IHttpClientFactory httpClientFactory) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IProfileService _profileService = profileService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    [HttpGet]
    public async Task<IActionResult> GetLocation()
    {
        var profile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, this.ProfileNo);
        if (profile == null)
        {
            return this.NotFound();
        }

        return this.Ok(new
        {
            latitude = profile.Latitude,
            longitude = profile.Longitude
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateLocation([FromBody] LocationUpdateRequest request)
    {
        var profile = await this._profileService.GetByUserAndProfileNoAsync(this.UserId, this.ProfileNo);
        if (profile == null)
        {
            return this.NotFound();
        }

        profile.Latitude = request.Latitude;
        profile.Longitude = request.Longitude;
        await this._profileService.UpdateAsync(profile);

        // Also update humans table so PoracleJS can read the active profile's location
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human != null)
        {
            human.Latitude = request.Latitude;
            human.Longitude = request.Longitude;
            await this._humanService.UpdateAsync(human);
        }

        return this.Ok(new
        {
            latitude = profile.Latitude,
            longitude = profile.Longitude
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
