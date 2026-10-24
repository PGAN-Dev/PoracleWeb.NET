using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public partial class AccountLinkRepository(PoracleWebContext context, ILogger<AccountLinkRepository> logger) : IAccountLinkRepository
{
    private const int SqliteConstraint = 19;
    private const int MySqlDuplicateEntry = 1062;

    private readonly PoracleWebContext _context = context;
    private readonly ILogger<AccountLinkRepository> _logger = logger;

    public async Task<AccountLink?> GetByHumanIdAsync(string humanId)
    {
        var entity = await this._context.AccountLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.DiscordId == humanId || l.TelegramId == humanId);

        return entity?.ToModel();
    }

    public async Task<IReadOnlyList<AccountLink>> GetAllAsync()
    {
        // One hand-edited row must not take down the admin list: it is skipped and named in the log.
        var entities = await this._context.AccountLinks.AsNoTracking().OrderBy(l => l.Id).ToListAsync();
        var links = new List<AccountLink>(entities.Count);
        foreach (var entity in entities)
        {
            try
            {
                links.Add(entity.ToModel());
            }
            catch (AccountLinkUnreadableException ex)
            {
                LogUnreadableSkipped(this._logger, ex, ex.LinkId);
            }
        }

        return links;
    }

    public async Task<AccountLink> AddAsync(AccountLink link)
    {
        var entity = link.ToEntity();
        this._context.AccountLinks.Add(entity);
        try
        {
            await this._context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            this._context.Entry(entity).State = EntityState.Detached;
            throw new AccountLinkConflictException("One of these accounts is already linked.", CollidedSide(ex), ex);
        }

        return entity.ToModel();
    }

    public async Task<bool> SetDefaultAsync(int id, LinkSide side)
    {
        var entity = await this._context.AccountLinks.FirstOrDefaultAsync(l => l.Id == id);
        if (entity is null)
        {
            return false;
        }

        entity.DefaultDestination = AccountLinkKeys.Wire(side);
        await this._context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> TouchVerifiedAsync(int id, LinkSide side, DateTime at)
    {
        var entity = await this._context.AccountLinks.FirstOrDefaultAsync(l => l.Id == id);
        if (entity is null)
        {
            return false;
        }

        if (side == LinkSide.Discord)
        {
            entity.DiscordVerifiedAt = at;
        }
        else
        {
            entity.TelegramVerifiedAt = at;
        }

        await this._context.SaveChangesAsync();
        return true;
    }

    public async Task<int> RemoveByHumanIdAsync(string humanId)
    {
        var entities = await this._context.AccountLinks
            .Where(l => l.DiscordId == humanId || l.TelegramId == humanId)
            .ToListAsync();
        if (entities.Count == 0)
        {
            return 0;
        }

        this._context.AccountLinks.RemoveRange(entities);
        await this._context.SaveChangesAsync();
        return entities.Count;
    }

    // The providers' exception types are read by name so this project needs neither package.
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            var name = inner.GetType().Name;
            if (name == "SqliteException" && ReadInt(inner, "SqliteErrorCode") == SqliteConstraint
                && inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (name == "MySqlException" && ReadInt(inner, "Number") == MySqlDuplicateEntry)
            {
                return true;
            }
        }

        return false;
    }

    private static LinkSide? CollidedSide(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? string.Empty;
        var discord = message.Contains("discord", StringComparison.OrdinalIgnoreCase);
        var telegram = message.Contains("telegram", StringComparison.OrdinalIgnoreCase);
        return discord == telegram ? null : discord ? LinkSide.Discord : LinkSide.Telegram;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Account link {LinkId} can't be read and is left out of the list")]
    private static partial void LogUnreadableSkipped(ILogger logger, Exception ex, int linkId);

    private static int? ReadInt(object target, string property) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as int?;
}
