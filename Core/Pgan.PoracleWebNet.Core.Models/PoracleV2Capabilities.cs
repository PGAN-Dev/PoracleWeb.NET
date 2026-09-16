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
