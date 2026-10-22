using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// One tracking rule exactly as PoracleNG's <c>/api/v2</c> stores and returns it, with the profile it lives on.
/// </summary>
/// <param name="Type">The v2 type name. <c>incident</c> stays <c>incident</c>; it is not folded into <c>invasion</c>.</param>
/// <param name="ProfileNo">The profile the rule belongs to.</param>
/// <param name="Uid">The rule's uid.</param>
/// <param name="Rule">The v2 rule untranslated, nulls and all.</param>
public sealed record StoredRule(string Type, int ProfileNo, int Uid, JsonElement Rule);

/// <summary>What a uid-addressed replace of a stored rule did, or would do on a dry run.</summary>
public enum StoredRuleReplaceOutcome
{
    /// <summary>PoracleNG stores something different from what it was sent.</summary>
    Updated,

    /// <summary>The rule is already as sent; nothing to change.</summary>
    Unchanged,

    /// <summary>The replacement would duplicate another rule.</summary>
    Conflict,

    /// <summary>PoracleNG holds no such rule for this human.</summary>
    NotFound,
}
