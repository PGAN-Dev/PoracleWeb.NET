namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// PoracleNG refused a mute with a 422 and said why -- an unknown area name is the case that reaches a
/// user. Carries upstream's own sentence so the SPA can show it instead of a generic failure.
/// </summary>
/// <remarks>
/// The v2 surface is huma-generated and its error envelope is <c>{title,status,detail,errors[]}</c>.
/// The readable sentence is <c>detail</c>; every other proxy in this codebase reads <c>error</c>, which
/// is v1's shape and is absent here.
/// </remarks>
public class MuteRejectedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}
