namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The alarms of one batch create, index for index with the request: uid 0 where nothing was written because
/// the alarm was already stored exactly or would have taken another rule over.
/// </summary>
/// <param name="Alarms">The requested alarms, each carrying the uid it was created under.</param>
/// <param name="TakeoversSkipped">How many of the uid-0 alarms were left out as takeovers rather than repeats.</param>
/// <param name="TakeoverReason">What a single Add says about a takeover, for the caller to show; null when there was none.</param>
public sealed record BatchCreateResult<TModel>(IReadOnlyList<TModel> Alarms, int TakeoversSkipped, string? TakeoverReason);
