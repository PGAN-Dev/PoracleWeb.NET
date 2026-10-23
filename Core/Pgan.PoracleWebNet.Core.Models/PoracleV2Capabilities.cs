namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// What a PoracleNG instance's own <c>/openapi.json</c> says its <c>/api/v2</c> surface can do.
/// </summary>
/// <remarks>
/// <para>
/// The schema-document sibling of <see cref="PoracleServerProfile"/>, and for the same reason: one
/// probe answering many typed questions, rather than each feature running its own. The two read
/// different documents because they answer different questions. <c>/health</c> gives the release number
/// and a capability map covering bot and template-editor features; <c>/openapi.json</c> is the only
/// place that says which fields and routes the v2 surface actually carries.
/// </para>
/// <para>
/// Asked of the server rather than checked in as a version compare, which is what
/// <c>V2SchemaBounds</c> already established for the numeric limits. Every capability below arrived in
/// PoracleNG PR #217, which as of this writing no release carries: the branch reports <c>5.3.0</c>, but
/// betting on that number means guessing which release it lands in, and a fork that cherry-picks one
/// fix reports whatever it likes. The document says what is there.
/// </para>
/// <para>
/// Everything fails closed. A server that cannot be reached, a document that will not parse, and a
/// 5.2.1 that genuinely lacks these all produce <see cref="None"/> — every answer false, every caller
/// keeping the workaround it has today. Failing open would point a write at a route that does not
/// exist.
/// </para>
/// </remarks>
public sealed class PoracleV2Capabilities
{
    /// <summary>Nothing known, so nothing offered.</summary>
    public static readonly PoracleV2Capabilities None = new();

    /// <summary>
    /// <c>V2InvasionRule</c> carries <c>grunt_type</c>, so an invasion rule names the grunt it targets
    /// on the way in and on the way out. See #841.
    /// </summary>
    /// <remarks>
    /// Without it the only targeting v2 offers is <c>type_id</c>, <c>grunt_id</c>, <c>boss</c> and
    /// <c>everything</c> — none of which can express the grunt names production holds.
    /// </remarks>
    public bool InvasionGruntType
    {
        get; init;
    }

    /// <summary>
    /// <c>V2PokemonRule</c> carries <c>personal_shiny</c> and <c>personal_background</c>: a rule can be
    /// limited to Pokemon that are shiny, or have the special background, for one of the human's linked
    /// game accounts. jfberry/PoracleNG#255 (feat/personal-shiny).
    /// </summary>
    /// <remarks>
    /// Both fields, not either: the PR adds them together and a server with one is not a server this
    /// application knows how to talk to. Read from the schema rather than a version for the usual
    /// reason: no release carries it, and a fork can report whatever version it likes. The write itself is
    /// gated on the same two names inside <c>TrackingV2Translator</c>; this flag is what the SPA is told.
    /// </remarks>
    public bool PersonalTracking
    {
        get; init;
    }

    /// <summary>
    /// The server serves <c>/api/v2/humans/{id}/accounts</c>, where a human's game player IDs are linked.
    /// Personal tracking matches nothing for a human with no linked account. jfberry/PoracleNG#255.
    /// </summary>
    public bool PlayerAccounts
    {
        get; init;
    }

    /// <summary>True when the document was read and parsed, whatever it turned out to say.</summary>
    /// <remarks>
    /// Distinct from every capability being false, which is also what an unreachable server looks like.
    /// Callers that want to explain themselves — an admin page saying "your Poracle does not have this"
    /// rather than "could not ask" — need to tell those apart.
    /// </remarks>
    public bool Read
    {
        get; init;
    }
}
