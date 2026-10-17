namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The two PoracleNG settings personal shiny tracking depends on (PoracleNG PR #255).
/// </summary>
/// <param name="RatesFeedConfigured">
/// <c>[shiny_rates] url</c> is set. Without a feed every personal rule matches nothing, so an
/// administrator is warned.
/// </param>
/// <param name="SelfService">
/// <c>[player_accounts] self_service</c>: whether trainers may link their own game accounts.
/// PoracleNG's API trusts its caller on this, so PoracleWeb.NET enforces it.
/// </param>
public sealed record PersonalShinyConfig(bool RatesFeedConfigured, bool SelfService);
