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
    /// <c>POST /v2/humans/{id}/areas</c> answers with what it stored and what it refused
    /// (<c>V2SetAreasOutputBody</c>: <c>areas</c> and <c>rejected</c>), so the Areas page save can go
    /// through v2 instead of v1's <c>setAreas</c>. See #995.
    /// </summary>
    /// <remarks>
    /// Read off the response rather than the route. 5.2.1 serves the same route answering a bare
    /// <c>{"status":"ok"}</c>, and only the shape that reports <c>rejected</c> was verified against a running
    /// build; on 5.2.1 the save stays on v1, which behaves the same way it always has.
    /// </remarks>
    public bool SetAreasReportsRejected
    {
        get; init;
    }

    /// <summary>
    /// <c>GET /v2/humans/{id}/areas</c> lists the areas this human may see as <c>V2AreaInfo</c> items
    /// (<c>name</c>, <c>group</c>, <c>description</c>, <c>userSelectable</c>), the same four fields v1's
    /// <c>GET /api/humans/{id}</c> carries under <c>areas</c>. See #995.
    /// </summary>
    public bool AvailableAreas
    {
        get; init;
    }

    /// <summary>
    /// <c>GET /v2/humans/{id}/profiles</c> lists profiles as <c>V2ProfileResponse</c>, carrying <c>area</c>
    /// and <c>active_hours</c>. See #995.
    /// </summary>
    /// <remarks>
    /// The two fields change shape across versions: 5.2.1 sends <c>area</c> as the JSON-encoded string v1
    /// sends, jfberry/PoracleNG#303 sends a real array, and both send <c>active_hours</c> as the typed
    /// entry array where v1 sends a string. The proxy translates all of them back to v1's shape.
    /// </remarks>
    public bool ProfileList
    {
        get; init;
    }

    /// <summary><c>DELETE /v2/humans/{id}/profiles/{profile_no}</c> exists. See #995.</summary>
    public bool ProfileDelete
    {
        get; init;
    }

    /// <summary>
    /// <c>POST /v2/humans/{id}/profiles/{profile_no}/copy</c> exists and its body names the source as
    /// <c>from_profile</c>. See #995.
    /// </summary>
    public bool ProfileCopy
    {
        get; init;
    }

    /// <summary>
    /// <c>POST /v2/humans/{id}/areas</c> and <c>POST /v2/humans/{id}/location</c> both take a
    /// <c>?profile=</c> naming the profile to write, and the areas answer reports what it refused, so a new
    /// profile's areas and location can be set through the API rather than by writing the
    /// <c>profiles</c> row directly. See #993.
    /// </summary>
    /// <remarks>
    /// All three, deliberately. 5.2.1 declares <c>?profile=</c> on the location write alone, and
    /// <c>docs/architecture/poracleng-proxy.md</c> records that write moving <c>humans.latitude/longitude</c>
    /// as well as the named profile's row there. Requiring the areas parameter too limits this to builds of
    /// the kind it was verified on (5.3.0, the build production runs), where writing a non-active profile
    /// leaves the active one alone.
    /// </remarks>
    public bool ProfileScopedGeography
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
