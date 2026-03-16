using Microsoft.AspNetCore.Mvc;
using PGAN.Poracle.Web.Core.Abstractions.Services;

namespace PGAN.Poracle.Web.Api.Controllers;

[Route("api/location")]
public class LocationController : BaseApiController
{
    private readonly IHumanService _humanService;
    private readonly IPoracleApiProxy _poracleApiProxy;
    private readonly IHttpClientFactory _httpClientFactory;

    public LocationController(
        IHumanService humanService,
        IPoracleApiProxy poracleApiProxy,
        IHttpClientFactory httpClientFactory)
    {
        _humanService = humanService;
        _poracleApiProxy = poracleApiProxy;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetLocation()
    {
        var human = await _humanService.GetByIdAndProfileAsync(UserId, ProfileNo);
        if (human == null)
            return NotFound();

        return Ok(new { latitude = human.Latitude, longitude = human.Longitude });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateLocation([FromBody] LocationUpdateRequest request)
    {
        var human = await _humanService.GetByIdAndProfileAsync(UserId, ProfileNo);
        if (human == null)
            return NotFound();

        human.Latitude = request.Latitude;
        human.Longitude = request.Longitude;
        await _humanService.UpdateAsync(human);

        return Ok(new { latitude = human.Latitude, longitude = human.Longitude });
    }

    [HttpPut("language")]
    public async Task<IActionResult> UpdateLanguage([FromBody] LanguageUpdateRequest request)
    {
        var human = await _humanService.GetByIdAndProfileAsync(UserId, ProfileNo);
        if (human == null)
            return NotFound();

        human.Language = request.Language;
        await _humanService.UpdateAsync(human);

        return Ok(new { language = human.Language });
    }

    [HttpGet("geocode")]
    public async Task<IActionResult> Geocode([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("Query parameter 'q' is required");

        try
        {
            var config = await _poracleApiProxy.GetConfigAsync();
            if (config == null || string.IsNullOrEmpty(config.ProviderUrl))
                return BadRequest("Geocoding not available - no provider configured");

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var url = $"{config.ProviderUrl.TrimEnd('/')}/search?addressdetails=1&q={Uri.EscapeDataString(q)}&format=json&limit=5";
            var response = await client.GetStringAsync(url);
            return Content(response, "application/json");
        }
        catch (Exception)
        {
            return StatusCode(503, "Geocoding service unavailable");
        }
    }

    [HttpGet("reverse")]
    public async Task<IActionResult> ReverseGeocode([FromQuery] double lat, [FromQuery] double lon)
    {
        try
        {
            var config = await _poracleApiProxy.GetConfigAsync();
            if (config == null || string.IsNullOrEmpty(config.ProviderUrl))
                return BadRequest("Geocoding not available - no provider configured");

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var url = $"{config.ProviderUrl.TrimEnd('/')}/reverse?lat={lat}&lon={lon}&format=json&addressdetails=1";
            var response = await client.GetStringAsync(url);
            return Content(response, "application/json");
        }
        catch (Exception)
        {
            return StatusCode(503, "Geocoding service unavailable");
        }
    }

    [HttpGet("staticmap")]
    public async Task<IActionResult> GetStaticMap([FromQuery] double lat, [FromQuery] double lon)
    {
        try
        {
            var url = await _poracleApiProxy.GetLocationMapUrlAsync(lat, lon);
            if (url != null)
                return Ok(new { url });
        }
        catch { }
        return NotFound();
    }

    [HttpGet("distancemap")]
    public async Task<IActionResult> GetDistanceMap([FromQuery] double lat, [FromQuery] double lon, [FromQuery] int distance)
    {
        try
        {
            var url = await _poracleApiProxy.GetDistanceMapUrlAsync(lat, lon, distance);
            if (url != null)
                return Ok(new { url });
        }
        catch { }
        return NotFound();
    }

    public class LocationUpdateRequest
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class LanguageUpdateRequest
    {
        public string Language { get; set; } = string.Empty;
    }
}
