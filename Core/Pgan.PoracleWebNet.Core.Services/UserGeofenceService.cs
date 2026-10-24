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
    IHumanService humanService,
    IUserAreaWriter userAreaWriter,
    ILinkedAccount linkedAccount,
    IPairedHumanWriter pairWriter,
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
    private readonly IHumanService _humanService = humanService;
    private readonly IUserAreaWriter _userAreaWriter = userAreaWriter;
    private readonly ILinkedAccount _linkedAccount = linkedAccount;
    private readonly IPairedHumanWriter _pairWriter = pairWriter;
    private readonly IDiscordNotificationService _discordNotificationService = discordNotificationService;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<UserGeofenceService> _logger = logger;

    public async Task<List<UserGeofence>> GetByUserAsync(string humanId)
    {
        // A linked pair shares its drawn fences: either id lists them all.
        var geofences = new List<UserGeofence>();
        foreach (var id in await this.PairIdsAsync(humanId))
        {
            geofences.AddRange(await this._repository.GetByHumanIdAsync(id));
        }

        foreach (var g in geofences)
        {
            if (!string.IsNullOrEmpty(g.PolygonJson))
            {
                try
                {
                    g.Polygon = JsonSerializer.Deserialize<double[][]>(g.PolygonJson);
                    // The admin listing set this and the user listing did not, so every geofence on
                    // the user page reported 0 points. See #477.
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

    public async Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model)
    {
        // Gate the "provide a geofence" path (#214). Also covers GeoJSON import, which funnels
        // through here. Throws FeatureDisabledException → 403 via the global exception filter.
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.UserGeofences);

        // Check count limit via local DB
        // The cap counts across both ids of a linked pair, including a side whose human is missing or
        // retyped right now, so the pair can't draw past it while one side is away.
        var count = 0;
        foreach (var id in (await this._linkedAccount.ResolveAsync(humanId)).LinkedHumanIds)
        {
            count += await this._repository.GetCountByHumanIdAsync(id);
        }

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

        // Collisions are checked against BOTH sources, which is what the original comment claimed
        // and what the code did not do: only user_geofences was consulted, so a user could take a
        // name an admin area already held. Both then reach PoracleJS through the same feed under one
        // name, and approving the private one pushes to Koji keyed on __name - an upsert that
        // overwrites the real area's polygon and flags. Matching is case-insensitive because Poracle
        // area matching is, in practice, and lowercasing alone does not separate "Nyack" from
        // "nyack". See #475.
        var takenNames = await this.ReservedGeofenceNamesAsync();

        if (takenNames.Contains(kojiName))
        {
            var baseName = kojiName;
            var found = false;
            for (var i = 2; i <= 10; i++)
            {
                kojiName = $"{baseName} {i}";
                if (!takenNames.Contains(kojiName))
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

        // Reload first: PoracleNG reads the new fence from our feed, and its trusted write rejects a
        // name it has not loaded. The reload does not prove the fence arrived (PoracleNG only logs a
        // failed feed fetch and keeps its cached list), so a rejected add is handled below as well.
        var fenceKnownToPoracle = await this.ReloadGeofencesSafeAsync();

        // Creating a geofence subscribes the current profile to it -- an area write, and one that ran
        // straight past disable_areas while every documented area path was refused. With areas frozen
        // the fence is still created; it just arrives inactive. See #505.
        if (await this._featureGate.IsEnabledAsync(DisableFeatureKeys.Areas))
        {
            // The fence is kept either way. When it could not be switched on the caller is told
            // (ActivationFailed) so the user can retry with the toggle; nothing is written around
            // PoracleNG. The user owns this fence (just created above), which authorises the name.
            var (activated, warning) = fenceKnownToPoracle ? await this.TryAddToActiveProfileAsync(humanId, kojiName) : (false, null);
            geofence.ActivationFailed = !activated;
            geofence.PairWarning = warning;
        }

        // Set polygon on result from input
        geofence.Polygon = model.Polygon;

        LogGeofenceCreated(this._logger, kojiName, LogSafe.Id(humanId));

        return geofence;
    }

    /// <summary>
    /// Every geofence name already in use, from both sources that feed the geofence feed.
    /// </summary>
    /// <remarks>
    /// A Koji outage must not block geofence creation, so an unreachable Koji degrades to checking
    /// PoracleWeb only - the same graceful-degradation rule the feed endpoint follows.
    /// </remarks>
    private async Task<HashSet<string>> ReservedGeofenceNamesAsync()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in await this._repository.GetAllAsync() ?? [])
        {
            names.Add(g.KojiName);

            if (!string.IsNullOrEmpty(g.PromotedName))
            {
                names.Add(g.PromotedName);
            }
        }

        try
        {
            foreach (var admin in await this._kojiService.GetAdminGeofencesAsync() ?? [])
            {
                if (!string.IsNullOrEmpty(admin.Name))
                {
                    names.Add(admin.Name);
                }
            }
        }
        catch (Exception ex)
        {
            LogReservedNameLookupFailed(this._logger, ex);
        }

        return names;
    }

    public async Task<UserGeofence> RenameAsync(
        string humanId, int id, string displayName, string? groupName, int? parentId)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.UserGeofences);

        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        if (!await this.OwnsAsync(geofence, humanId))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // The same rules create enforces. Rename checked only for emptiness, so a name creation refuses --
        // over 50 characters, or carrying characters outside the allowlist -- could be applied by editing
        // an existing geofence instead. The polygon-side validation on this feature is thorough, which
        // made the gap look like an oversight rather than a decision. See #585.
        // Once a fence is approved, Koji owns it under PromotedName and the area lists hold that name,
        // not KojiName. Renaming then rewrote the subscription to a name neither Koji nor the feed
        // serves, silently unsubscribing the owner from a live public area -- and PublicAreaName still
        // returned the promoted name, so a later admin delete cleaned the wrong entry. A submission
        // under review is frozen for the same reason: the admin is looking at the name as submitted.
        // See #646.
        if (!RenameableStatuses.Contains(geofence.Status))
        {
            throw new InvalidOperationException(
                $"A geofence cannot be renamed while its status is '{geofence.Status}'.");
        }

        var trimmedName = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > 50)
        {
            throw new ArgumentException("Display name must be between 1 and 50 characters.", nameof(displayName));
        }

        if (!MyRegex().IsMatch(trimmedName))
        {
            throw new ArgumentException("Display name contains invalid characters.", nameof(displayName));
        }

        var oldKojiName = geofence.KojiName;
        var newKojiName = trimmedName.ToLowerInvariant();
        var (previousDisplayName, previousGroupName, previousParentId) = (geofence.DisplayName, geofence.GroupName, geofence.ParentId);

        if (!string.Equals(oldKojiName, newKojiName, StringComparison.OrdinalIgnoreCase))
        {
            var takenNames = await this.ReservedGeofenceNamesAsync();
            if (takenNames.Contains(newKojiName))
            {
                throw new TrackingConflictException(
                    "geofence",
                    "Another area already uses that name. Choose a different one.");
            }
        }

        geofence.DisplayName = trimmedName;
        geofence.KojiName = newKojiName;
        // group_name is NOT NULL, and the rename dialog does not always send one -- keep what is there rather than clearing it.
        geofence.GroupName = string.IsNullOrWhiteSpace(groupName) ? geofence.GroupName : groupName;
        // Same treatment as group_name above: 0 is the dialog's "nothing selected", not a request to
        // clear the parent. Taken literally it wiped the region of every renamed geofence, and a later
        // approval then sent __parent: null to Koji and landed the area ungrouped. See #648.
        geofence.ParentId = parentId is > 0 ? parentId.Value : geofence.ParentId;
        var updated = await this._repository.UpdateAsync(geofence);

        PairWarning? renameWarning = null;

        // Every profile that was subscribed stays subscribed, under the new name. This is the whole
        // point of renaming in place rather than delete-then-recreate. See #543.
        // Reload first: PoracleNG refuses a rename to a name it has not loaded, and the feed only serves
        // the new name now that the row is updated. A DEFINITE failure (reload failed, or PoracleNG refused
        // the rename) puts the row back: left alone it carries the new name while every profile holds the
        // old one, the save looks successful, and a later fence reusing the old name would inherit its
        // subscribers. An ambiguous failure (the rename call throwing) is not rolled back: the write may
        // have landed, and restoring the row then would strand the subscriptions under the new name.
        if (!string.Equals(CleanName(oldKojiName), CleanName(newKojiName), StringComparison.Ordinal))
        {
            // A linked pair's other human holds the same name, so it is renamed there too.
            var renamed = false;
            if (await this.ReloadGeofencesSafeAsync())
            {
                (renamed, renameWarning) = await this.RenameAcrossPairAsync(humanId, oldKojiName, newKojiName);
            }

            if (!renamed)
            {
                geofence.DisplayName = previousDisplayName;
                geofence.KojiName = oldKojiName;
                geofence.GroupName = previousGroupName;
                geofence.ParentId = previousParentId;
                _ = await this._repository.UpdateAsync(geofence);
                _ = await this.ReloadGeofencesSafeAsync();
                LogRenameRolledBack(this._logger, oldKojiName, newKojiName);
                throw new GeofenceRenameFailedException();
            }
        }
        else
        {
            _ = await this.ReloadGeofencesSafeAsync();
        }

        // The repository round-trip returns the row without its parsed polygon, and the page renders the
        // map straight from this response -- so a renamed geofence vanished from the map and reported 0
        // points until a reload. Carry both across, exactly as the listing does. See #559, #566.
        updated.Polygon = geofence.Polygon ?? ParsePolygonSafe(updated.PolygonJson, updated.KojiName);
        updated.PointCount = updated.Polygon?.Length ?? 0;
        updated.PairWarning = renameWarning;
        return updated;
    }

    /// <summary>
    /// Renames an area on the signed-in human and, for a linked pair, the other existing human. Renamed is the
    /// signed-in side's answer; the warning is set when the other side threw or refused.
    /// </summary>
    private async Task<(bool Renamed, PairWarning? Warning)> RenameAcrossPairAsync(string userId, string from, string to)
    {
        string? refusedOn = null;
        var signedInRenamed = false;
        var written = await this._pairWriter.WriteAsync(userId, async id =>
        {
            var isSignedIn = string.Equals(id, userId, StringComparison.Ordinal);
            if (!isSignedIn && !signedInRenamed)
            {
                // The signed-in side refused, so the caller is about to put the row back. Renaming the other
                // human's areas now would strand them under a name nothing serves.
                return false;
            }

            var renamed = await this._userAreaWriter.RenameEverywhereAsync(id, from, to);
            if (isSignedIn)
            {
                signedInRenamed = renamed;
            }
            else if (!renamed)
            {
                refusedOn = id;
            }

            return renamed;
        });

        var warning = written.Warning;
        if (warning is null && refusedOn is not null)
        {
            warning = new PairWarning(await this._pairWriter.SideOfAsync(userId, refusedOn) ?? LinkSide.Telegram, "PoracleNG refused the rename.", PairWarningCodes.RenameRefused);
        }

        return (written.Primary, warning);
    }

    /// <summary>Removes an area name everywhere on the signed-in human and the other existing human of a linked pair.</summary>
    private async Task<PairWarning?> RemoveAcrossPairAsync(string userId, string name, bool deleteWidenedAlarms) =>
        (await this._pairWriter.WriteAsync(userId, id => this._userAreaWriter.RemoveEverywhereAsync(id, name, deleteWidenedAlarms))).Warning;

    /// <summary>
    /// The alarms that would widen on a delete, summed across a linked pair. The first human's failure
    /// propagates; the other's is logged and left out, since a preview should not fail on it.
    /// </summary>
    private async Task<AreaDeleteImpact> PreviewAcrossPairAsync(string userId, string name)
    {
        var ids = await this.PairIdsAsync(userId);
        var first = await this._userAreaWriter.PreviewRemovalAsync(ids[0], name);
        if (ids.Count == 1)
        {
            return first;
        }

        var byType = new Dictionary<string, int>(first.WidenedByType);
        foreach (var id in ids.Skip(1))
        {
            try
            {
                var other = await this._userAreaWriter.PreviewRemovalAsync(id, name);
                foreach (var (type, count) in other.WidenedByType)
                {
                    byType[type] = byType.GetValueOrDefault(type) + count;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPreviewOtherSideFailed(this._logger, ex, LogSafe.Id(id));
            }
        }

        return new AreaDeleteImpact(byType, byType.Values.Sum());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not preview the area removal for the linked account {HumanId}; its alarms are not in the count")]
    private static partial void LogPreviewOtherSideFailed(ILogger logger, Exception ex, string humanId);

    /// <summary>Parses a stored polygon, returning null rather than throwing on a malformed one.</summary>
    private double[][]? ParsePolygonSafe(string? polygonJson, string kojiName)
    {
        if (string.IsNullOrEmpty(polygonJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<double[][]>(polygonJson);
        }
        catch (JsonException ex)
        {
            LogPolygonDeserializationFailed(this._logger, ex, kojiName);
            return null;
        }
    }

    public async Task<AreaDeleteImpact> GetDeleteImpactAsync(string humanId, int id)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        if (!await this.OwnsAsync(geofence, humanId))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        return await this.PreviewAcrossPairAsync(humanId, PublicAreaName(geofence).ToLowerInvariant());
    }

    public async Task<AreaDeleteImpact> AdminGetDeleteImpactAsync(int id)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        return await this.PreviewAcrossPairAsync(geofence.HumanId, PublicAreaName(geofence).ToLowerInvariant());
    }

    public async Task<PairWarning?> DeleteAsync(string humanId, int profileNo, int id, bool deleteWidenedAlarms)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        if (!await this.OwnsAsync(geofence, humanId))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // Deliberately not gated on disable_areas, unlike the subscribe on create: this is cleanup, not
        // editing. The polygon is about to stop existing, and leaving its name in every profile's area
        // list would subscribe those profiles to something Poracle can no longer resolve. See #505.
        //
        // Removes the name from every profile and every alarm's override areas, through PoracleNG. Uses the
        // promoted name when there is one: that is what the owner is subscribed to after an approval, and
        // what GetDeleteImpactAsync previewed. It goes first: if it throws, Koji, the row and the
        // subscriptions are all left as they were, so the delete can simply be retried.
        // A linked pair holds the name on both humans: the signed-in side's failure keeps the row, the other's is a warning.
        var warning = await this.RemoveAcrossPairAsync(humanId, PublicAreaName(geofence).ToLowerInvariant(), deleteWidenedAlarms);

        // Same orphan risk as the admin path: if this fence was ever promoted it exists in Koji under the
        // public name, and deleting only the local row leaves it there forever. See #409.
        await this.RemoveFromKojiIfPromotedAsync(geofence);

        // Delete from local DB
        await this._repository.DeleteAsync(id);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        LogGeofenceDeleted(this._logger, geofence.KojiName, id, LogSafe.Id(humanId));
        return warning;
    }

    public async Task<List<UserGeofence>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<List<UserGeofence>> GetAllWithDetailsAsync()
    {
        var geofences = await this.GetAllAsync();
        await this.EnrichWithDetailsAsync(geofences);
        return geofences;
    }

    /// <summary>
    /// Fills in the fields the admin list renders but the table does not store: owner and reviewer
    /// names, and the parsed polygon with its point count.
    /// </summary>
    /// <remarks>
    /// Shared with approve and reject so their responses carry the same projection. Returning the bare
    /// row from those left the SPA -- which swaps the row for the response -- showing raw Discord
    /// snowflakes and no map thumbnail until the page was reloaded. See #618.
    /// </remarks>
    private async Task EnrichWithDetailsAsync(List<UserGeofence> geofences)
    {
        if (geofences.Count == 0)
        {
            return;
        }

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
        var humans = await this._humanService.GetByIdsAsync(allIds);
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
    }

    public async Task<PairWarning?> AdminDeleteAsync(string adminId, int id, bool deleteWidenedAlarms)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new GeofenceNotFoundException(id);

        // Remove from the owner's areas across all profiles first, as DeleteAsync does. A failure used to be
        // logged and the row deleted anyway, stranding the name in the owner's lists; now it stops the delete
        // before Koji or the row is touched. An owner PoracleNG no longer has holds no areas, so that one case
        // goes ahead.
        // Each human of the owner's pair is cleaned on its own: one gone from PoracleNG holds no areas, and
        // must not stop the other from being cleaned.
        var areaName = PublicAreaName(geofence).ToLowerInvariant();
        var removed = await this._pairWriter.WriteAsync(geofence.HumanId, async id =>
        {
            try
            {
                await this._userAreaWriter.RemoveEverywhereAsync(id, areaName, deleteWidenedAlarms);
            }
            catch (AccountGoneException)
            {
                LogAreaOwnerGone(this._logger, geofence.KojiName, LogSafe.Id(id));
            }
        });
        var warning = removed.Warning;

        await this.RemoveFromKojiIfPromotedAsync(geofence);

        await this._repository.DeleteAsync(id);
        await this.ReloadGeofencesSafeAsync();

        LogAdminDeletedGeofence(this._logger, LogSafe.Id(adminId), geofence.KojiName, id, geofence.Status);
        return warning;
    }

    public async Task RemoveFencesOfDeletedAccountAsync(string humanId)
    {
        var leftovers = await this._repository.GetByHumanIdAsync(humanId);
        if (leftovers.Count == 0)
        {
            return;
        }

        foreach (var geofence in leftovers)
        {
            await this.RemoveFromKojiIfPromotedAsync(geofence);
            await this._repository.DeleteAsync(geofence.Id);
        }

        await this.ReloadGeofencesSafeAsync();
    }

    /// <summary>
    /// Keyed on "was this ever pushed to Koji?", not on the current status. Gating on status == "approved"
    /// meant a reject-then-delete left a public, userSelectable fence in the shared Koji project that no
    /// PoracleWeb record could manage, recoverable only by hand-editing Koji. PromotedName is set by approval
    /// and never cleared, so it is the durable marker. A Koji failure is logged, never thrown. See #409.
    /// </summary>
    private async Task RemoveFromKojiIfPromotedAsync(UserGeofence geofence)
    {
        if (!WasPromotedToKoji(geofence))
        {
            return;
        }

        try
        {
            await this._kojiService.RemoveGeofenceFromProjectAsync(PublicAreaName(geofence));
        }
        catch (Exception ex)
        {
            LogKojiRemovalFailed(this._logger, ex, geofence.KojiName);
        }
    }

    public async Task<UserGeofence> SubmitForReviewAsync(string humanId, string kojiName)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.UserGeofences);

        var geofence = await this._repository.GetByKojiNameAsync(kojiName)
            ?? throw new GeofenceNotFoundException(kojiName);

        if (!await this.OwnsAsync(geofence, humanId))
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

        LogGeofenceSubmittedForReview(this._logger, LogSafe.Id(humanId), kojiName);

        return updated;
    }

    /// <summary>
    /// Assembles everything the Discord review card shows. Rebuilt on approval/rejection rather than
    /// cached, so the edited post reflects whatever the admin actually published.
    /// </summary>
    private async Task<GeofenceSubmissionPost> BuildSubmissionPostAsync(UserGeofence geofence, GeofenceReviewState state)
    {
        // Read through the service, which asks PoracleNG rather than the database. Its own failure is
        // caught here rather than left to the caller, so the owner's display name degrades on its own
        // like every other piece of this card -- the post still goes out, with the mention carrying the
        // identity instead of a name.
        Human? human = null;
        try
        {
            human = await this._humanService.GetByIdAsync(geofence.HumanId);
        }
        catch (Exception ex)
        {
            LogOwnerNameLookupFailed(this._logger, ex, LogSafe.Id(geofence.HumanId));
        }

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

        // Approve accepted any status, the same gap #409 fixed on reject.
        //
        //   pending_review  the normal path
        //   rejected        allowed: an admin reconsidering a call they already made. Nothing is in Koji
        //                   yet, so this is a plain promotion.
        //   active          refused: never submitted. Approving it skips the submission flow entirely, so
        //                   there is no review thread and the owner never asked for it to be public.
        //   approved        refused: already public in Koji. Re-approving under a different promoted name
        //                   would push a second entry and strand the first — the exact leak #409 closed.
        //                   Renaming a live public area is a different operation from approving one.
        if (!ApprovableStatuses.Contains(geofence.Status))
        {
            throw new InvalidOperationException(
                $"Geofence must be awaiting review to be approved. Current status: '{geofence.Status}'.");
        }

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
            // Koji resolves __parent as a geofence id and rejects one it does not know. That rejection used
            // to surface as an opaque 500, so check it here where the id can be named. Values <= 0 are the
            // documented "no region" case (#314) and are left alone.
            if (parentId.Value > 0)
            {
                // Best-effort: if the region list itself cannot be fetched we let Koji decide rather than
                // blocking an approval that would have worked. Koji's own rejection is now typed and
                // surfaces as a 502 either way, so nothing becomes opaque again.
                List<GeofenceRegion>? regions = null;
                try
                {
                    regions = await this._kojiService.GetRegionsAsync();
                }
                catch (Exception ex)
                {
                    LogRegionLookupFailed(this._logger, ex, parentId.Value);
                }

                if (regions is { Count: > 0 } && !regions.Any(r => r.Id == parentId.Value))
                {
                    throw new InvalidOperationException(
                        $"Region {parentId.Value} does not exist in Koji. Pick a region from the list.");
                }
            }

            geofence.ParentId = parentId.Value;
        }

        if (groupName != null)
        {
            geofence.GroupName = groupName.Trim();
        }

        // Save to Koji as a public geofence (userSelectable + displayInMatches = true). This name is also
        // what Poracle matches area subscriptions against, so -- like every other geofence name in this
        // file -- it must be lowercase; Poracle's matching is case-sensitive. An admin-typed promoted name
        // with any uppercase letter used to reach Koji verbatim while the area rename (below)
        // lowercased the same name for the owner's subscription: the two diverged, Koji served the mixed
        // case and the owner's humans.area/profiles.area held the lowercase, and that user's alerts for
        // the area they just had approved silently stopped firing. geofence.PromotedName keeps the
        // original casing for display, same as every other PromotedName read in this file.
        var targetName = (promotedName ?? geofence.KojiName).ToLowerInvariant();
        await this._kojiService.SaveGeofenceAsync(
            targetName, geofence.DisplayName, geofence.GroupName, geofence.ParentId, polygon, isPublic: true);

        // Move the owner's subscription to the promoted name. This must not go through SetAreasAsync:
        // PoracleNG intersects the submitted list against userSelectable=true fences, which dropped the
        // owner's entire custom-geofence set while approve still returned 200 (#408). The v2 rename moves
        // one name on every profile and in every rule's override areas, but PoracleNG refuses a name it
        // has not loaded, so its list is reloaded first (which also drops the Koji admin cache the save
        // above just made stale). Everything after the Koji save is best effort: the fence is public by
        // now, so the approval stands and the response says whether the owner's areas followed.
        bool? ownerAreasRenamed = null;
        PairWarning? approvalWarning = null;
        if (!string.Equals(CleanName(targetName), CleanName(geofence.KojiName), StringComparison.Ordinal))
        {
            // One retry: the first reload can lose a race with Koji, and a second attempt is cheap next to
            // an admin having to tell an owner to re-pick the area.
            ownerAreasRenamed = false;
            for (var attempt = 0; attempt < 2 && ownerAreasRenamed != true; attempt++)
            {
                try
                {
                    if (await this.ReloadGeofencesSafeAsync())
                    {
                        (var renamedNow, approvalWarning) = await this.RenameAcrossPairAsync(geofence.HumanId, geofence.KojiName, targetName);
                        ownerAreasRenamed = renamedNow;
                    }
                    else
                    {
                        ownerAreasRenamed = false;
                    }
                }
                catch (Exception ex)
                {
                    LogAreaSwapFallbackFailed(this._logger, ex, geofence.KojiName);
                }
            }

            if (ownerAreasRenamed != true)
            {
                LogOwnerAreasNotRenamed(this._logger, geofence.KojiName, targetName);
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

        LogGeofenceApproved(this._logger, LogSafe.Id(adminId), geofence.KojiName, id, promotedName);

        await this.EnrichWithDetailsAsync([updated]);
        updated.OwnerAreasRenamed = ownerAreasRenamed;
        updated.PairWarning = approvalWarning;
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

        LogGeofenceRejected(this._logger, LogSafe.Id(adminId), geofence.KojiName, id);

        await this.EnrichWithDetailsAsync([updated]);
        return updated;
    }

    public async Task<PairWarning?> AddToProfileAsync(string humanId, int profileNo, int geofenceId)
    {
        // Activating or deactivating writes an area subscription, so it must answer to
        // disable_areas as well as disable_user_geofences. Enforced here rather than as a second
        // attribute (the filter disallows two), which also covers service-to-service callers.
        // See #478.
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Areas);
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.UserGeofences);

        var geofence = await this._repository.GetByIdAsync(geofenceId)
            ?? throw new GeofenceNotFoundException(geofenceId);

        if (!await this.OwnsAsync(geofence, humanId))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // The user owns the fence (checked above), which authorises the name for the trusted write.
        // False means "already on the profile" or "PoracleNG rejected the name". Reload and retry once
        // for the second (PoracleNG may not have loaded the fence), then tell the two apart by reading
        // the list: a name that is there is a successful, idempotent activate; one that is not is a
        // failure the caller must see rather than a silent 204.
        //
        // A linked pair switches the fence on for both humans' active profiles. Each human authorises the name
        // through the policy, which counts the pair's drawn fences as the human's own.
        var written = await this._pairWriter.WriteAsync(humanId, async id =>
        {
            if (!await this._userAreaWriter.AddToActiveProfileAsync(id, geofence.KojiName))
            {
                await this.ReloadGeofencesSafeAsync();
                if (!await this._userAreaWriter.AddToActiveProfileAsync(id, geofence.KojiName))
                {
                    var held = await this._humanProxy.GetAreaListAsync(id);
                    if (!held.Contains(geofence.KojiName, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new GeofenceActivationFailedException(geofence.KojiName);
                    }
                }
            }
        });

        await this.ReloadGeofencesSafeAsync();
        return written.Warning;
    }

    public async Task<PairWarning?> RemoveFromProfileAsync(string humanId, int profileNo, int geofenceId)
    {
        // Activating or deactivating writes an area subscription, so it must answer to
        // disable_areas as well as disable_user_geofences. Enforced here rather than as a second
        // attribute (the filter disallows two), which also covers service-to-service callers.
        // See #478.
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Areas);
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.UserGeofences);

        var geofence = await this._repository.GetByIdAsync(geofenceId)
            ?? throw new GeofenceNotFoundException(geofenceId);

        if (!await this.OwnsAsync(geofence, humanId))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // The user owns the fence (checked above), which authorises the name. False means it was
        // not on the profile, which is the state the caller asked for.
        var written = await this._pairWriter.WriteAsync(humanId, id => this._userAreaWriter.RemoveFromActiveProfileAsync(id, geofence.KojiName));
        await this.ReloadGeofencesSafeAsync();
        return written.Warning;
    }

    /// <summary>The caller's human id and, for a linked pair, the other existing human's: the ids whose drawn fences count as the caller's.</summary>
    private async Task<IReadOnlyList<string>> PairIdsAsync(string humanId)
    {
        var pair = await this._linkedAccount.ResolveAsync(humanId);
        return pair.ExistingHumanIds.Contains(humanId, StringComparer.Ordinal)
            ? pair.ExistingHumanIds
            : [humanId, .. pair.ExistingHumanIds];
    }

    private async Task<bool> OwnsAsync(UserGeofence geofence, string humanId) =>
        (await this.PairIdsAsync(humanId)).Contains(geofence.HumanId, StringComparer.OrdinalIgnoreCase);

    public async Task<List<GeofenceRegion>> GetRegionsAsync() => await this._kojiService.GetRegionsAsync();

    /// <summary>
    /// Statuses an admin may approve from. See the comment in <see cref="ApproveSubmissionAsync"/> for why
    /// <c>active</c> and <c>approved</c> are not among them.
    /// </summary>
    /// <summary>Statuses a geofence may still be renamed in. See #646.</summary>
    private static readonly HashSet<string> RenameableStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "active", "rejected" };

    private static readonly HashSet<string> ApprovableStatuses =
        new(StringComparer.Ordinal) { "pending_review", "rejected" };

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

    /// <summary>
    /// Activated is true when the name is now on the signed-in human's active profile; false when PoracleNG
    /// rejected it or it was already there. The warning is set when a linked pair's other human did not take it.
    /// </summary>
    private async Task<(bool Activated, PairWarning? Warning)> TryAddToActiveProfileAsync(string humanId, string name)
    {
        try
        {
            var written = await this._pairWriter.WriteAsync(humanId, id => this._userAreaWriter.AddToActiveProfileAsync(id, name));
            return (written.Primary, written.Warning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogGeofenceActivationFailed(this._logger, ex, name);
            return (false, null);
        }
    }

    /// <returns>True when PoracleNG acknowledged the reload; false when it failed (logged, never thrown).</returns>
    private async Task<bool> ReloadGeofencesSafeAsync()
    {
        try
        {
            this._kojiService.InvalidateAdminGeofenceCache();
            await this._poracleApiProxy.ReloadGeofencesAsync();
            return true;
        }
        catch (Exception ex)
        {
            LogGeofenceReloadFailed(this._logger, ex);
            return false;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-zA-Z0-9 \-'.()&]+$")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();

    private static string CleanName(string name) => name.Trim().ToLowerInvariant();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Renaming geofence '{OldName}' to '{NewName}' failed at PoracleNG; the row was restored and the user asked to retry")]
    private static partial void LogRenameRolledBack(ILogger logger, string oldName, string newName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not switch on new geofence '{KojiName}'; it is saved but inactive")]
    private static partial void LogGeofenceActivationFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize polygon for geofence '{KojiName}'")]
    private static partial void LogPolygonDeserializationFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created custom geofence '{KojiName}' for user {HumanId}")]
    private static partial void LogGeofenceCreated(ILogger logger, string kojiName, string humanId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted custom geofence '{KojiName}' (ID {Id}) for user {HumanId}")]
    private static partial void LogGeofenceDeleted(ILogger logger, string kojiName, int id, string humanId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to remove approved geofence '{KojiName}' from Koji during admin delete")]
    private static partial void LogKojiRemovalFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Owner {HumanId} of geofence '{KojiName}' is gone from PoracleNG; no areas to remove")]
    private static partial void LogAreaOwnerGone(ILogger logger, string kojiName, string humanId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} deleted geofence '{KojiName}' (ID {Id}, status: {Status})")]
    private static partial void LogAdminDeletedGeofence(ILogger logger, string adminId, string kojiName, int id, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the owner of a geofence submission ({HumanId}); the review card will show the mention without a name")]
    private static partial void LogOwnerNameLookupFailed(ILogger logger, Exception ex, string humanId);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not fetch Koji regions to validate parent {ParentId}; letting Koji decide")]
    private static partial void LogRegionLookupFailed(ILogger logger, Exception ex, int parentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read Koji areas while checking a geofence name for collisions; checked PoracleWeb only")]
    private static partial void LogReservedNameLookupFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to reload Poracle geofences after custom geofence change")]
    private static partial void LogGeofenceReloadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approved geofence '{OldName}' as '{NewName}' in Koji, but the owner's subscriptions were not moved to the new name")]
    private static partial void LogOwnerAreasNotRenamed(ILogger logger, string oldName, string newName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not move the owner's subscription to the promoted name for geofence '{KojiName}'; they are no longer subscribed to it")]
    private static partial void LogAreaSwapFallbackFailed(ILogger logger, Exception ex, string kojiName);
}
