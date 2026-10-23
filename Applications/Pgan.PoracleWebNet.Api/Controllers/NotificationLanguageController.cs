using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Api.Controllers;

/// <summary>
/// The user's notification language.
/// </summary>
/// <remarks>
/// Deliberately its own controller rather than an action on <c>LocationController</c>. That controller
/// carries a class-level <c>disable_location</c> gate, which also blocked these two endpoints even
/// though they only touch <c>humans.language</c> and have nothing to do with a location. The Areas page
/// hosts the language selector and calls this on init, so with locations disabled the 403 carried a
/// disableKey, the error interceptor read it as a dead page and redirected to the dashboard - making an
/// enabled feature unreachable. Authentication is unchanged: BaseApiController is [Authorize]. See #479.
/// </remarks>
[Route("api/location/language")]
public class NotificationLanguageController(IHumanService humanService, IPairedHumanWriter pairWriter) : BaseApiController
{
    private readonly IHumanService _humanService = humanService;
    private readonly IPairedHumanWriter _pairWriter = pairWriter;

    [HttpGet]
    public async Task<IActionResult> GetLanguage()
    {
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

        return this.Ok(new
        {
            language = human.Language
        });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateLanguage([FromBody] LanguageUpdateRequest request)
    {
        var human = await this._humanService.GetByIdAsync(this.UserId);
        if (human == null)
        {
            return this.NotFound();
        }

        // humans.language is varchar(255); a longer value used to overflow on write.
        if (request.Language is { Length: > 255 })
        {
            return this.BadRequest(new { error = "Language must be 255 characters or fewer." });
        }

        var written = await this._pairWriter.WriteAsync(this.UserId, id => this._humanService.SetLanguageAsync(id, request.Language));

        // Read back rather than echo. PoracleNG lowercases and trims what it stores -- "pt-BR" becomes
        // "pt-br" -- and an endpoint that reported the request instead of the row would leave the SPA
        // holding a value the server does not have. Verified on 5.2.1 against both API versions.
        // The write has landed by now. A failed read-back throws since #994; the request stays the fallback
        // a null always was, so a change that took effect is not reported as a failure.
        string? storedLanguage;
        try
        {
            storedLanguage = (await this._humanService.GetByIdAsync(this.UserId))?.Language;
        }
        catch (HttpRequestException)
        {
            storedLanguage = null;
        }

        return this.Ok(PairResponse.With(new
        {
            language = storedLanguage ?? request.Language
        }, written.Warning));
    }

    public class LanguageUpdateRequest
    {
        public string Language { get; set; } = string.Empty;
    }
}
