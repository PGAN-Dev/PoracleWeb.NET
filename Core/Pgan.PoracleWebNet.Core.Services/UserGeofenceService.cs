using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class UserGeofenceService(
    IUserGeofenceRepository repository,
    IKojiService kojiService,
    IPoracleApiProxy poracleApiProxy,
    IPoracleHumanProxy humanProxy,
    IHumanRepository humanRepository,
    IUserAreaDualWriter areaWriter,
    IDiscordNotificationService discordNotificationService,
    IFeatureGate featureGate,
    IConfiguration configuration,
    ILogger<UserGeofenceService> logger) : IUserGeofenceService
{
    private const int MaxGeofencesPerUser = 10;

    private readonly IUserGeofenceRepository _repository = repository;
    private readonly IKojiService _kojiService = kojiService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IHumanRepository _humanRepository = humanRepository;
    private readonly IUserAreaDualWriter _areaWriter = areaWriter;
    private readonly IDiscordNotificationService _discordNotificationService = discordNotificationService;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<UserGeofenceService> _logger = logger;

    public async Task<List<UserGeofence>> GetByUserAsync(string humanId)
    {
        var geofences = await this._repository.GetByHumanIdAsync(humanId);

        foreach (var g in geofences)
        {
            if (!string.IsNullOrEmpty(g.PolygonJson))
            {
                try
                {
                    g.Polygon = JsonSerializer.Deserialize<double[][]>(g.PolygonJson);
                }
                catch (JsonException ex)
                {
                    LogPolygonDeserializationFailed(this._logger, ex, g.KojiName);
                }
            }
        }

        return geofences;
    }

    public async Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model)
    {
        // Gate the "provide a geofence" path (#214). Also covers GeoJSON import, which funnels
        // through here. Throws FeatureDisabledException → 403 via the global exception filter.
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.UserGeofences);

        // Check count limit via local DB
        var count = await this._repository.GetCountByHumanIdAsync(humanId);
        if (count >= MaxGeofencesPerUser)
        {
            throw new InvalidOperationException($"Maximum of {MaxGeofencesPerUser} custom geofences per user reached.");
        }

        // Validate display name (server-side, matching frontend regex)
        var trimmedName = model.DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > 50)
        {
            throw new InvalidOperationException("Display name must be between 1 and 50 characters.");
        }

        if (!MyRegex().IsMatch(trimmedName))
        {
            throw new InvalidOperationException("Display name contains invalid characters.");
        }

        // Point count was the only thing checked here, so a polygon of [[1],[2],[3]] or one with
        // coordinates outside the globe was stored verbatim and then served by the anonymous geofence
        // feed. Import already validated arity and range; this is the same rule, applied once. See #410.
        if (!PolygonValidation.TryValidate(model.Polygon, out var polygonError))
        {
            throw new InvalidOperationException(polygonError);
        }

        // Use lowercase display name as the Koji geofence name
        // Must be lowercase because Poracle does case-sensitive area matching
        // and humans.area stores names in lowercase
        var kojiName = model.DisplayName.Trim().ToLowerInvariant();

        // Check for collision with existing geofences (our DB + Koji)
        var existing = await this._repository.GetByKojiNameAsync(kojiName);
        if (existing != null)
        {
            var baseName = kojiName;
            var found = false;
            for (var i = 2; i <= 10; i++)
            {
                kojiName = $"{baseName} {i}";
                existing = await this._repository.GetByKojiNameAsync(kojiName);
                if (existing == null)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException($"Unable to generate a unique geofence name for '{model.DisplayName}'. Please choose a different name.");
            }
        }

        // Serialize polygon to JSON and store locally (not in Koji)
        var polygonJson = JsonSerializer.Serialize(model.Polygon);

        // Create local DB record with polygon data
        var geofence = await this._repository.CreateAsync(new UserGeofence
        {
            HumanId = humanId,
            KojiName = kojiName,
            DisplayName = model.DisplayName,
            GroupName = model.GroupName,
            ParentId = model.ParentId,
            PolygonJson = polygonJson,
            Status = "active",
        });

        // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
        // Atomic direct-DB dual-write of humans.area + current profiles.area. Revert to
        // IPoracleHumanProxy.SetAreasAsync once PoracleNG ships a trusted setAreas variant.
        await this._areaWriter.AddAreaToActiveProfileAsync(humanId, kojiName);

        // Reload Poracle geofences (Poracle reads from our feed + Koji)
        await this.ReloadGeofencesSafeAsync();

        // Set polygon on result from input
        geofence.Polygon = model.Polygon;

        LogGeofenceCreated(this._logger, kojiName, humanId);

        return geofence;
    }

    public async Task DeleteAsync(string humanId, int profileNo, int id)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // Same orphan risk as the admin path: if this fence was ever promoted it exists in Koji under the
        // public name, and deleting only the local row leaves it there forever. See #409.
        if (WasPromotedToKoji(geofence))
        {
            try
            {
                await this._kojiService.RemoveGeofenceFromProjectAsync(PublicAreaName(geofence));
            }
            catch (Exception ex)
            {
                LogKojiRemovalFailed(this._logger, ex, geofence.KojiName);
            }
        }

        // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
        // Atomic direct-DB removal from humans.area + every profiles.area row. Uses the promoted name
        // when there is one — that is what the owner is actually subscribed to after an approval.
        await this._areaWriter.RemoveAreaFromAllProfilesAsync(humanId, PublicAreaName(geofence).ToLowerInvariant());

        // Delete from local DB
        await this._repository.DeleteAsync(id);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        LogGeofenceDeleted(this._logger, geofence.KojiName, id, humanId);
    }

    public async Task<List<UserGeofence>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<List<UserGeofence>> GetAllWithDetailsAsync()
    {
        var geofences = await this.GetAllAsync();

        // Batch-fetch owner humans by distinct HumanId
        var humanIds = geofences.Select(g => g.HumanId).Distinct().ToList();

        // Also fetch reviewer humans (reviewedBy is a humanId)
        var reviewerIds = geofences
            .Where(g => !string.IsNullOrEmpty(g.ReviewedBy))
            .Select(g => g.ReviewedBy!)
            .Distinct()
            .ToList();

        // Merge all IDs for a single batch lookup
        var allIds = humanIds.Union(reviewerIds).Distinct().ToList();
        var humans = await this._humanRepository.GetByIdsAsync(allIds);
        var humanLookup = humans.ToDictionary(h => h.Id, h => h);

        foreach (var g in geofences)
        {
            // Enrich owner name
            g.OwnerName = humanLookup.TryGetValue(g.HumanId, out var human)
                ? human.Name ?? g.HumanId
                : g.HumanId;

            // Enrich reviewer name
            if (!string.IsNullOrEmpty(g.ReviewedBy))
            {
                g.ReviewedByName = humanLookup.TryGetValue(g.ReviewedBy, out var reviewer)
                    ? reviewer.Name ?? g.ReviewedBy
                    : g.ReviewedBy;
            }

            // Parse polygon and set point count
            if (!string.IsNullOrEmpty(g.PolygonJson))
            {
                try
                {
                    g.Polygon = JsonSerializer.Deserialize<double[][]>(g.PolygonJson);
                    g.PointCount = g.Polygon?.Length ?? 0;
                }
                catch (JsonException ex)
                {
                    LogPolygonDeserializationFailed(this._logger, ex, g.KojiName);
                }
            }
        }

        return geofences;
    }

    public async Task AdminDeleteAsync(string adminId, int id)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        // Keyed on "was this ever pushed to Koji?", not on the current status. Gating on
        // status == "approved" meant a reject-then-delete left a public, userSelectable fence in the
        // shared Koji project that no PoracleWeb record could manage — recoverable only by hand-editing
        // Koji. PromotedName is set by approval and never cleared, so it is the durable marker. See #409.
        if (WasPromotedToKoji(geofence))
        {
            try
            {
                await this._kojiService.RemoveGeofenceFromProjectAsync(PublicAreaName(geofence));
            }
            catch (Exception ex)
            {
                LogKojiRemovalFailed(this._logger, ex, geofence.KojiName);
            }
        }

        // Remove from user's area across all profiles
        try
        {
            var areaName = PublicAreaName(geofence).ToLowerInvariant();
            // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
            await this._areaWriter.RemoveAreaFromAllProfilesAsync(geofence.HumanId, areaName);
        }
        catch (Exception ex)
        {
            LogAreaRemovalFailed(this._logger, ex, geofence.KojiName);
        }

        await this._repository.DeleteAsync(id);
        await this.ReloadGeofencesSafeAsync();

        LogAdminDeletedGeofence(this._logger, adminId, geofence.KojiName, id, geofence.Status);
    }

    public async Task<UserGeofence> SubmitForReviewAsync(string humanId, string kojiName)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.UserGeofences);

        var geofence = await this._repository.GetByKojiNameAsync(kojiName)
            ?? throw new GeofenceNotFoundException(kojiName);

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        if (geofence.Status != "active")
        {
            throw new InvalidOperationException($"Geofence must be in 'active' status to submit for review. Current status: '{geofence.Status}'.");
        }

        geofence.Status = "pending_review";
        geofence.SubmittedAt = DateTime.UtcNow;

        var updated = await this._repository.UpdateAsync(geofence);

        // Create Discord forum post for the submission
        try
        {
            var post = await this.BuildSubmissionPostAsync(updated, GeofenceReviewState.Pending);
            var threadId = await this._discordNotificationService.CreateGeofenceSubmissionPostAsync(post);

            if (threadId != null)
            {
                updated.DiscordThreadId = threadId;
                updated = await this._repository.UpdateAsync(updated);
            }
        }
        catch (Exception ex)
        {
            LogDiscordForumPostCreationFailed(this._logger, ex, kojiName);
        }

        LogGeofenceSubmittedForReview(this._logger, humanId, kojiName);

        return updated;
    }

    /// <summary>
    /// Assembles everything the Discord review card shows. Rebuilt on approval/rejection rather than
    /// cached, so the edited post reflects whatever the admin actually published.
    /// </summary>
    private async Task<GeofenceSubmissionPost> BuildSubmissionPostAsync(UserGeofence geofence, GeofenceReviewState state)
    {
        // Profile-agnostic on purpose. GetByIdAndProfileAsync(id, 1) also filters on current_profile_no, so
        // it finds nobody whose active profile isn't #1 -- which is most people, since the default is 0.
        var human = await this._humanRepository.GetByIdAsync(geofence.HumanId);

        double[][]? polygon = null;
        if (!string.IsNullOrEmpty(geofence.PolygonJson))
        {
            try
            {
                polygon = JsonSerializer.Deserialize<double[][]>(geofence.PolygonJson);
            }
            catch (JsonException ex)
            {
                LogPolygonDeserializationFailed(this._logger, ex, geofence.KojiName);
            }
        }

        var area = polygon != null ? GeoMath.AreaSqKm(polygon) : 0;
        var (lat, lon) = polygon != null ? GeoMath.Centroid(polygon) : (0, 0);

        string? mapImageUrl = null;
        try
        {
            // Approved geofences live in Koji under their published name; everything else under the original.
            var mapName = state == GeofenceReviewState.Approved && geofence.PromotedName != null
                ? geofence.PromotedName.ToLowerInvariant()
                : geofence.KojiName;

            mapImageUrl = await this._poracleApiProxy.GetAreaMapUrlAsync(mapName);
            if (mapImageUrl == null)
            {
                LogStaticMapUnavailable(this._logger, geofence.KojiName);
            }
        }
        catch (Exception ex)
        {
            LogStaticMapFetchFailed(this._logger, ex, geofence.KojiName);
        }

        var publicName = state == GeofenceReviewState.Approved && geofence.PromotedName != null
            ? geofence.PromotedName.ToLowerInvariant()
            : geofence.KojiName;

        return new GeofenceSubmissionPost
        {
            UserId = geofence.HumanId,
            // Never a raw snowflake: with no name the author block is dropped and the mention in the
            // message body carries the identity instead.
            UserName = string.IsNullOrWhiteSpace(human?.Name) ? null : human.Name,
            DisplayName = geofence.DisplayName,
            PublicName = publicName,
            GroupName = geofence.GroupName,
            AreaSqKm = area,
            CentroidLat = lat,
            CentroidLon = lon,
            OverlapsArea = polygon != null && state == GeofenceReviewState.Pending
                ? await this.FindContainingPublicAreaAsync(lat, lon)
                : null,
            MapImageUrl = mapImageUrl,
            State = state,
            ReviewNotes = geofence.ReviewNotes,
            ReviewUrl = this.BuildReviewUrl(),
        };
    }

    /// <summary>
    /// Names an existing public area whose polygon contains this submission's centre, so a reviewer can spot
    /// a duplicate without opening a map. A centroid test, not a true overlap measurement.
    /// </summary>
    private async Task<string?> FindContainingPublicAreaAsync(double lat, double lon)
    {
        try
        {
            var adminGeofences = await this._kojiService.GetAdminGeofencesAsync();

            foreach (var fence in adminGeofences)
            {
                // Bounding box first -- the Koji cache precomputes it precisely to avoid ray-casting 800 fences.
                if (lat < fence.MinLat || lat > fence.MaxLat || lon < fence.MinLon || lon > fence.MaxLon)
                {
                    continue;
                }

                if (GeoMath.Contains(fence.Path, lat, lon))
                {
                    return fence.Name;
                }
            }
        }
        catch (Exception ex)
        {
            LogOverlapCheckFailed(this._logger, ex);
        }

        return null;
    }

    /// <summary>Deep link to the admin review queue, or null when no public site URL is configured.</summary>
    private string? BuildReviewUrl()
    {
        var siteUrl = this._configuration["Site:PublicUrl"];
        return string.IsNullOrWhiteSpace(siteUrl)
            ? null
            : $"{siteUrl.TrimEnd('/')}/admin/geofence-submissions";
    }

    public async Task<List<UserGeofence>> GetPendingSubmissionsAsync() => await this._repository.GetByStatusAsync("pending_review");

    public async Task<UserGeofence> ApproveSubmissionAsync(string adminId, int id, string? promotedName, int? parentId = null, string? groupName = null)
    {
        // Validate promotedName with the same rules as display names
        if (promotedName != null)
        {
            var trimmedPromoted = promotedName.Trim();
            if (string.IsNullOrEmpty(trimmedPromoted) || trimmedPromoted.Length > 50)
            {
                throw new InvalidOperationException("Promoted name must be between 1 and 50 characters.");
            }

            if (!MyRegex().IsMatch(trimmedPromoted))
            {
                throw new InvalidOperationException("Promoted name contains invalid characters.");
            }

            promotedName = trimmedPromoted;
        }

        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        // Parse polygon from local DB
        if (string.IsNullOrEmpty(geofence.PolygonJson))
        {
            throw new InvalidOperationException($"Geofence '{geofence.KojiName}' has no polygon data stored locally.");
        }

        var polygon = JsonSerializer.Deserialize<double[][]>(geofence.PolygonJson)
            ?? throw new InvalidOperationException($"Failed to deserialize polygon for geofence '{geofence.KojiName}'.");

        // The region (parent/group) is what makes a promoted geofence appear under a region in Koji and
        // PoracleNG's area picker. End users may create a geofence without one (issue #314), so let the
        // approving admin set/override it here. Null args mean "keep whatever the submission already had".
        if (parentId.HasValue)
        {
            geofence.ParentId = parentId.Value;
        }

        if (groupName != null)
        {
            geofence.GroupName = groupName.Trim();
        }

        // Save to Koji as a public geofence (userSelectable + displayInMatches = true)
        var targetName = promotedName ?? geofence.KojiName;
        await this._kojiService.SaveGeofenceAsync(
            targetName, geofence.DisplayName, geofence.GroupName, geofence.ParentId, polygon, isPublic: true);

        // Move the owner's subscription to the promoted name.
        //
        // HACK: trusted-set-areas — this must not go through SetAreasAsync. PoracleNG intersects the
        // submitted list against userSelectable=true fences for non-admins, and at this point BOTH names
        // fail that test: the old one because user geofences are served userSelectable=false, and the
        // promoted one because PoracleNG has not reloaded its fence list yet (that happens below). The
        // result was a silent wipe of the owner's entire custom-geofence subscription set — not just this
        // fence — while approve still returned 200. See #408.
        if (promotedName != null && !string.Equals(promotedName, geofence.KojiName, StringComparison.Ordinal))
        {
            try
            {
                await this._areaWriter.RenameAreaInAllProfilesAsync(
                    geofence.HumanId, geofence.KojiName, promotedName);
            }
            catch (Exception ex)
            {
                // The geofence is public in Koji by now, so the approval itself stands. Surface the
                // subscription loss instead of failing an approval that already took effect upstream.
                LogAreaSwapFallbackFailed(this._logger, ex, geofence.KojiName);
            }
        }

        // Update local record
        geofence.Status = "approved";
        geofence.ReviewedBy = adminId;
        geofence.ReviewedAt = DateTime.UtcNow;
        geofence.PromotedName = promotedName;

        var updated = await this._repository.UpdateAsync(geofence);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        // Notify Discord forum thread
        if (!string.IsNullOrEmpty(geofence.DiscordThreadId))
        {
            try
            {
                var post = await this.BuildSubmissionPostAsync(updated, GeofenceReviewState.Approved);
                await this._discordNotificationService.PostReviewOutcomeAsync(geofence.DiscordThreadId, post);
            }
            catch (Exception ex)
            {
                LogApprovalDiscordPostFailed(this._logger, ex, geofence.DiscordThreadId);
            }
        }

        LogGeofenceApproved(this._logger, adminId, geofence.KojiName, id, promotedName);

        return updated;
    }

    public async Task<UserGeofence> RejectSubmissionAsync(string adminId, int id, string reviewNotes)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        // Without this, reject accepted any status. Rejecting an already-approved fence flipped the row to
        // "rejected" while leaving it public in Koji, and admin delete then skipped the Koji cleanup
        // because that was gated on status == "approved" — orphaning a public, userSelectable fence in the
        // shared project with no local record able to manage it. It would also silently "reject" a
        // geofence the owner had never submitted. SubmitForReviewAsync has always enforced the state
        // machine; the review endpoints did not. See #409.
        if (!string.Equals(geofence.Status, "pending_review", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Geofence must be awaiting review to be rejected. Current status: '{geofence.Status}'.");
        }

        geofence.Status = "rejected";
        geofence.ReviewedBy = adminId;
        geofence.ReviewedAt = DateTime.UtcNow;
        geofence.ReviewNotes = reviewNotes;

        var updated = await this._repository.UpdateAsync(geofence);

        // Notify Discord forum thread
        if (!string.IsNullOrEmpty(geofence.DiscordThreadId))
        {
            try
            {
                var post = await this.BuildSubmissionPostAsync(updated, GeofenceReviewState.Rejected);
                await this._discordNotificationService.PostReviewOutcomeAsync(geofence.DiscordThreadId, post);
            }
            catch (Exception ex)
            {
                LogRejectionDiscordPostFailed(this._logger, ex, geofence.DiscordThreadId);
            }
        }

        LogGeofenceRejected(this._logger, adminId, geofence.KojiName, id);

        return updated;
    }

    public async Task AddToProfileAsync(string humanId, int profileNo, int geofenceId)
    {
        var geofence = await this._repository.GetByIdAsync(geofenceId)
            ?? throw new GeofenceNotFoundException(geofenceId);

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
        // Atomic direct-DB dual-write. Revert to IPoracleHumanProxy.SetAreasAsync once
        // PoracleNG ships a trusted setAreas variant.
        await this._areaWriter.AddAreaToActiveProfileAsync(humanId, geofence.KojiName);

        // HACK: trusted-set-areas — PoracleNG's HandleSetAreas ends with reloadState(deps) so
        // the proxy path used to trigger the in-memory state refresh automatically. Direct-DB
        // writes skip that, so we ask PoracleNG to reload manually — otherwise the toggle only
        // takes effect on the next organic reload (which can be minutes). Drop this line when
        // the direct-DB write is reverted to a trusted setAreas proxy call.
        await this.ReloadGeofencesSafeAsync();
    }

    public async Task RemoveFromProfileAsync(string humanId, int profileNo, int geofenceId)
    {
        var geofence = await this._repository.GetByIdAsync(geofenceId)
            ?? throw new GeofenceNotFoundException(geofenceId);

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // HACK: trusted-set-areas — atomic direct-DB removal, mirror of AddToProfileAsync.
        await this._areaWriter.RemoveAreaFromActiveProfileAsync(humanId, geofence.KojiName);
        // HACK: trusted-set-areas — manual reload, same rationale as AddToProfileAsync.
        await this.ReloadGeofencesSafeAsync();
    }

    public async Task<List<GeofenceRegion>> GetRegionsAsync() => await this._kojiService.GetRegionsAsync();

    // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
    // Re-adds user-owned geofence names via direct DB after PoracleNG's setAreas stripped them.
    // Remove this method and its callsite in AreaController.UpdateAreas once PoracleNG ships a
    // trusted setAreas variant (query flag or dedicated endpoint) that skips the userSelectable
    // intersection filter. Trust is already established via X-Poracle-Secret.
    public async Task<IReadOnlyList<string>> PreserveOwnedAreasInHumanAsync(string humanId, IReadOnlyCollection<string> candidateAreaNames)
    {
        if (candidateAreaNames.Count == 0)
        {
            return [];
        }

        // Only write back names that the user actually owns as custom geofences.
        var owned = await this._repository.GetByHumanIdAsync(humanId);
        if (owned.Count == 0)
        {
            return [];
        }

        var ownedNames = new HashSet<string>(owned.Select(g => g.KojiName.ToLowerInvariant()));
        var toRestore = candidateAreaNames
            .Select(a => a.ToLowerInvariant())
            .Where(ownedNames.Contains)
            .Distinct()
            .ToList();

        if (toRestore.Count == 0)
        {
            return [];
        }

        // Single bulk write — one load of humans + profiles, one SaveChangesAsync, atomic.
        await this._areaWriter.AddAreasToActiveProfileAsync(humanId, toRestore);

        // HACK: trusted-set-areas — manual reload because the direct-DB merge above skipped
        // PoracleNG's internal reloadState hook. The caller (AreaController.UpdateAreas)
        // already triggered a reload via setAreas, but we need a fresh one so PoracleNG picks
        // up the restored names too. PoracleNG's reload is debounced 500ms server-side so the
        // back-to-back calls coalesce. Drop this line when the method is removed.
        await this.ReloadGeofencesSafeAsync();

        return toRestore;
    }

    /// <summary>
    /// Whether this geofence was ever pushed into the shared Koji project. <c>PromotedName</c> is written
    /// by approval and never cleared, so it stays true through a later rejection — which is the point.
    /// </summary>
    private static bool WasPromotedToKoji(UserGeofence geofence) =>
        !string.IsNullOrEmpty(geofence.PromotedName) || string.Equals(geofence.Status, "approved", StringComparison.Ordinal);

    /// <summary>
    /// The name this geofence is known by outside PoracleWeb: the promoted name once approved under one,
    /// otherwise the original. This is the name in Koji and in the owner's area list.
    /// </summary>
    private static string PublicAreaName(UserGeofence geofence) =>
        string.IsNullOrEmpty(geofence.PromotedName) ? geofence.KojiName : geofence.PromotedName;

    private async Task ReloadGeofencesSafeAsync()
    {
        try
        {
            this._kojiService.InvalidateAdminGeofenceCache();
            await this._poracleApiProxy.ReloadGeofencesAsync();
        }
        catch (Exception ex)
        {
            LogGeofenceReloadFailed(this._logger, ex);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-zA-Z0-9 \-'.()&]+$")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize polygon for geofence '{KojiName}'")]
    private static partial void LogPolygonDeserializationFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created custom geofence '{KojiName}' for user {HumanId}")]
    private static partial void LogGeofenceCreated(ILogger logger, string kojiName, string humanId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted custom geofence '{KojiName}' (ID {Id}) for user {HumanId}")]
    private static partial void LogGeofenceDeleted(ILogger logger, string kojiName, int id, string humanId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to remove approved geofence '{KojiName}' from Koji during admin delete")]
    private static partial void LogKojiRemovalFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to remove area for geofence '{KojiName}' during admin delete")]
    private static partial void LogAreaRemovalFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} deleted geofence '{KojiName}' (ID {Id}, status: {Status})")]
    private static partial void LogAdminDeletedGeofence(ILogger logger, string adminId, string kojiName, int id, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch static map for geofence '{KojiName}'")]
    private static partial void LogStaticMapFetchFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Poracle returned no static map URL for geofence '{KojiName}'; the submission embed will have no map")]
    private static partial void LogStaticMapUnavailable(ILogger logger, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not check the submission against existing public areas; the overlap hint will be omitted")]
    private static partial void LogOverlapCheckFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create Discord forum post for geofence submission '{KojiName}'")]
    private static partial void LogDiscordForumPostCreationFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {HumanId} submitted geofence '{KojiName}' for review")]
    private static partial void LogGeofenceSubmittedForReview(ILogger logger, string humanId, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post approval to Discord thread {ThreadId}")]
    private static partial void LogApprovalDiscordPostFailed(ILogger logger, Exception ex, string threadId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} approved geofence '{KojiName}' (ID {Id}), promotedName: {PromotedName}")]
    private static partial void LogGeofenceApproved(ILogger logger, string adminId, string kojiName, int id, string? promotedName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post rejection to Discord thread {ThreadId}")]
    private static partial void LogRejectionDiscordPostFailed(ILogger logger, Exception ex, string threadId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} rejected geofence '{KojiName}' (ID {Id})")]
    private static partial void LogGeofenceRejected(ILogger logger, string adminId, string kojiName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to reload Poracle geofences after custom geofence change")]
    private static partial void LogGeofenceReloadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the owner's subscription to the promoted name for geofence '{KojiName}'; they are no longer subscribed to it")]
    private static partial void LogAreaSwapFallbackFailed(ILogger logger, Exception ex, string kojiName);
}
