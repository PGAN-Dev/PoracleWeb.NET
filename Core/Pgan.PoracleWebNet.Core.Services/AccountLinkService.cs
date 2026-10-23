using System.Globalization;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class AccountLinkService(
    IAccountLinkRepository links,
    IHumanService humans,
    ISiteSettingService settings,
    ILinkedHumanProvisioner provisioner,
    ILinkedAccount linkedAccount,
    ILogger<AccountLinkService> logger) : IAccountLinkService
{
    private readonly IHumanService _humans = humans;
    private readonly ILinkedAccount _linkedAccount = linkedAccount;
    private readonly IAccountLinkRepository _links = links;
    private readonly ILogger<AccountLinkService> _logger = logger;
    private readonly ILinkedHumanProvisioner _provisioner = provisioner;
    private readonly ISiteSettingService _settings = settings;

    public async Task<AccountLink> LinkAsync(string callerHumanId, LinkSide newSide, string newId, string newName)
    {
        var caller = await this._humans.GetByIdAsync(callerHumanId);
        var callerSide = SideOfType(caller?.Type);
        if (callerSide is null || callerSide == newSide)
        {
            throw new LinkRefusalException(LinkRefusalCode.WrongType, "A link pairs one Discord account with one Telegram account.");
        }

        newId = (newId ?? string.Empty).Trim();
        if (!IsValidId(newSide, newId))
        {
            throw new LinkRefusalException(LinkRefusalCode.BadId, "That is not a valid account id.");
        }

        // Before anything is created: an id already in a link must not leave a mirror human behind.
        if (await this._links.GetByHumanIdAsync(callerHumanId) is not null || await this._links.GetByHumanIdAsync(newId) is not null)
        {
            throw Taken();
        }

        var other = await this._humans.GetByIdAsync(newId);
        var created = false;
        if (other is not null)
        {
            if (!string.Equals(other.Type, TypeOf(newSide), StringComparison.Ordinal))
            {
                throw new LinkRefusalException(LinkRefusalCode.TypeMismatch, "That id belongs to something other than a person's account.");
            }
        }
        else if (await this._settings.GetBoolAsync(AccountLinkKeys.RegisterMissing))
        {
            await this._provisioner.CreateMirrorAsync(callerHumanId, newSide, newId, newName);
            created = true;
        }

        var now = DateTime.UtcNow;
        var discordId = newSide == LinkSide.Discord ? newId : callerHumanId;
        var telegramId = newSide == LinkSide.Telegram ? newId : callerHumanId;
        AccountLink link;
        try
        {
            link = await this._links.AddAsync(new AccountLink(0, discordId, telegramId, callerSide.Value, now, now, now));
        }
        catch (AccountLinkConflictException)
        {
            if (created)
            {
                await this.TryRollBackAsync(newId, newSide, newName);
            }

            throw Taken();
        }
        catch when (created)
        {
            await this.TryRollBackAsync(newId, newSide, newName);
            throw;
        }

        // Outside the try: the row is saved by now, so nothing that fails here may delete the mirror.
        this.InvalidateBoth(discordId, telegramId);
        return link;
    }

    public async Task<bool> UnlinkAsync(string humanId)
    {
        var link = await this._links.GetByHumanIdAsync(humanId);
        if (link is null)
        {
            return false;
        }

        var removed = await this._links.RemoveByHumanIdAsync(humanId);
        this.InvalidateBoth(link.DiscordId, link.TelegramId);
        return removed > 0;
    }

    public async Task SetDefaultAsync(string humanId, LinkSide side)
    {
        var link = await this._links.GetByHumanIdAsync(humanId)
            ?? throw new LinkRefusalException(LinkRefusalCode.NotPermitted, "There is no link to change.");

        var sideId = side == LinkSide.Discord ? link.DiscordId : link.TelegramId;
        var human = await this._humans.GetByIdAsync(sideId);
        if (human is null || !string.Equals(human.Type, TypeOf(side), StringComparison.Ordinal))
        {
            throw new LinkRefusalException(LinkRefusalCode.NotPermitted, "That account is not registered yet.");
        }

        await this._links.SetDefaultAsync(link.Id, side);
        this.InvalidateBoth(link.DiscordId, link.TelegramId);
    }

    private static LinkRefusalException Taken() =>
        new(LinkRefusalCode.Taken, "That account is already linked to another one.");

    private static string TypeOf(LinkSide side) =>
        side == LinkSide.Discord ? AccountLinkKeys.DiscordUserType : AccountLinkKeys.TelegramUserType;

    private static LinkSide? SideOfType(string? type) =>
        string.Equals(type, AccountLinkKeys.DiscordUserType, StringComparison.Ordinal) ? LinkSide.Discord
        : string.Equals(type, AccountLinkKeys.TelegramUserType, StringComparison.Ordinal) ? LinkSide.Telegram
        : null;

    private static bool IsValidId(LinkSide side, string id)
    {
        if (id.Length is 0 or > 20 || id[0] == '0' || !id.All(char.IsAsciiDigit))
        {
            return false;
        }

        // No leading zero, so a Telegram id is positive by construction; it only has to fit a long.
        return side == LinkSide.Discord || long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    // Invalidate finds a partner only through a cached link, so both ids are named.
    private void InvalidateBoth(string discordId, string telegramId)
    {
        this._linkedAccount.Invalidate(discordId);
        this._linkedAccount.Invalidate(telegramId);
    }

    private async Task TryRollBackAsync(string newId, LinkSide newSide, string newName)
    {
        try
        {
            await this._provisioner.RollBackAsync(newId, newSide, newName);
        }
        catch (Exception ex)
        {
            LogRollbackFailed(this._logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the human created for a link that was then refused; it may be left behind")]
    private static partial void LogRollbackFailed(ILogger logger, Exception ex);
}
