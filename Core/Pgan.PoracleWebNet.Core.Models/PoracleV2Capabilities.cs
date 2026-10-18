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
    /// <c>V2SetAreasBody</c> carries <c>trusted</c>, so a client holding the API secret can select a
    /// fence that is not <c>userSelectable</c>.
    /// </summary>
    /// <remarks>
    /// The keystone. Without it every user-drawn geofence name sent to <c>setAreas</c> is silently
    /// dropped, which is the entire reason <c>IUserAreaDualWriter</c> and the
    /// <c>HACK: trusted-set-areas</c> sites exist. See #838.
    /// </remarks>
    public bool TrustedSetAreas
    {
        get; init;
    }

    /// <summary>
    /// <c>V2UpdateProfileBody</c> carries <c>name</c>, so a profile can be renamed through the API
    /// instead of by writing <c>profiles.name</c> directly. See #837.
    /// </summary>
    public bool ProfileRename
    {
        get; init;
    }

    /// <summary>
    /// Creating a profile answers with the number it was assigned, rather than a bare
    /// <c>{"status":"ok"}</c>. See #836.
    /// </summary>
    /// <remarks>
    /// PoracleNG assigns the lowest free number, so the caller cannot predict it. Without this the only
    /// way to learn it is to snapshot the profile list, create, re-read and diff.
    /// </remarks>
    public bool ProfileCreateReturnsNumber
    {
        get; init;
    }

    /// <summary>
    /// <c>GET /v2/humans</c> lists humans and <c>DELETE /v2/humans/{id}</c> purges one, which together
    /// are the last three things keeping <c>IHumanRepository</c> alive. See #839.
    /// </summary>
    /// <remarks>
    /// Both, deliberately. The list alone does not let the repository go, and reporting them separately
    /// would invite a half-migration that keeps the Poracle-DB dependency for the remaining method.
    /// </remarks>
    public bool AdminHumanRoutes
    {
        get; init;
    }

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
    /// <c>V2PokemonRule</c> carries <c>ping</c>, and a v2 write of it is genuinely stored rather than
    /// blanked. See jfberry/PoracleNG#249.
    /// </summary>
    /// <remarks>
    /// Before #249, every <c>V2*Rule</c> had no field for <c>ping</c> at all and the handlers stored
    /// <c>Ping: ""</c> unconditionally on a v2 write -- silently erasing a role or user mention that was
    /// the entire point of the alert. Checked against <c>V2PokemonRule</c> only, as the one type verified
    /// live, standing in for all eleven the way <see cref="InvasionGruntType"/> stands in for one PR
    /// landing as a whole -- #249 touches the same shared <c>translateV2*</c> functions for every type,
    /// not pokemon's alone.
    /// </remarks>
    public bool TrackingPing
    {
        get; init;
    }

    /// <summary>
    /// <c>V2PokemonRule</c> carries <c>personal_shiny</c> and <c>personal_background</c>: a rule can be
    /// limited to Pokemon that are shiny, or have the special background, for one of the human's linked
    /// game accounts. jfberry/PoracleNG#255.
    /// </summary>
    /// <remarks>
    /// Both fields, not either: the PR adds them together and a server with one is not a server this
    /// application knows how to talk to. Read from the schema rather than a version for the usual
    /// reason: no release carries it, and a fork can report whatever version it likes.
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

    /// <summary>
    /// The three location request bodies (<c>V2LocationBody</c>, <c>V2AddLocationBody</c>,
    /// <c>V2UpdateLocationBody</c>) name the coordinates <c>latitude</c> and <c>longitude</c>
    /// (jfberry/PoracleNG#303, unreleased). Every released PoracleNG names them <c>lat</c> and <c>lon</c>.
    /// </summary>
    /// <remarks>
    /// The bodies are <c>additionalProperties:false</c>, so one body cannot carry both spellings, and a
    /// 422 from the wrong one is not a missing route: <c>TryV2Async</c> does not fall back to v1 on it.
    /// Read from <c>V2LocationBody</c> alone, which stands in for all three the way #303 changed them
    /// together; false when the document cannot be read, which is the released names.
    /// </remarks>
    public bool V2LocationLatitudeLongitude
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
