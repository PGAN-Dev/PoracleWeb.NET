namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// One alarm aimed at a saved place: what blocks deleting it. <c>Type</c> is PoracleNG's tracking type, with a
/// Pokéstop event reported as <c>incident</c> although the snapshot files it under <c>invasion</c>.
/// <c>Description</c> is PoracleNG's own one-line summary of the rule, when it sent one.
/// </summary>
public sealed record PlaceUsageRule(string Type, int Uid, string? Description);
