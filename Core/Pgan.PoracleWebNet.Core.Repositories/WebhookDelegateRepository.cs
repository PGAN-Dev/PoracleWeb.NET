using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class WebhookDelegateRepository(PoracleWebContext context) : IWebhookDelegateRepository
{
    private readonly PoracleWebContext _context = context;

    public async Task<IEnumerable<WebhookDelegate>> GetAllAsync()
    {
        var entities = await this._context.WebhookDelegates
            .AsNoTracking()
            .ToListAsync();

        return entities.Select(e => e.ToModel());
    }

    public async Task<IEnumerable<WebhookDelegate>> GetByWebhookIdAsync(string webhookId)
    {
        var entities = await this._context.WebhookDelegates
            .AsNoTracking()
            .Where(d => d.WebhookId == webhookId)
            .ToListAsync();

        return entities.Select(e => e.ToModel());
    }

    public async Task<IEnumerable<string>> GetWebhookIdsByUserIdAsync(string userId) => await this._context.WebhookDelegates
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .Select(d => d.WebhookId)
            .ToListAsync();

    public async Task<WebhookDelegate> AddAsync(string webhookId, string userId)
    {
        // One atomic statement instead of check-then-insert. Two grants of the same delegate racing each
        // other would otherwise both pass the check and the loser would fail the unique index with a
        // "Duplicate entry '<webhook_id>-<user_id>'" error -- and a webhook's id is its URL, token and all,
        // which EF Core's own Update error line and the global exception handler would then both print.
        // A duplicate is a no-op here, so the provider never raises it. See #1101.
        var now = DateTime.UtcNow;
        if (this._context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            // The tests run on SQLite, which spells the same thing differently.
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO webhook_delegates (webhook_id, user_id, created_at) VALUES ({webhookId}, {userId}, {now}) ON CONFLICT (webhook_id, user_id) DO NOTHING");
        }
        else
        {
            // ON DUPLICATE KEY UPDATE with a self-assignment rather than INSERT IGNORE: IGNORE would also
            // downgrade other errors (an over-long value) to warnings and store a truncated row.
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO webhook_delegates (webhook_id, user_id, created_at) VALUES ({webhookId}, {userId}, {now}) ON DUPLICATE KEY UPDATE id = id");
        }

        var entity = await this._context.WebhookDelegates
            .AsNoTracking()
            .FirstAsync(d => d.WebhookId == webhookId && d.UserId == userId);

        return entity.ToModel();
    }

    public async Task<bool> RemoveAsync(string webhookId, string userId)
    {
        var entity = await this._context.WebhookDelegates
            .FirstOrDefaultAsync(d => d.WebhookId == webhookId && d.UserId == userId);

        if (entity is null)
        {
            return false;
        }

        this._context.WebhookDelegates.Remove(entity);
        await this._context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveAllForWebhookAsync(string webhookId)
    {
        var entities = await this._context.WebhookDelegates
            .Where(d => d.WebhookId == webhookId)
            .ToListAsync();

        if (entities.Count == 0)
        {
            return false;
        }

        this._context.WebhookDelegates.RemoveRange(entities);
        await this._context.SaveChangesAsync();
        return true;
    }

    public async Task<int> RemoveAllForIdAsync(string id)
    {
        var entities = await this._context.WebhookDelegates
            .Where(d => d.WebhookId == id || d.UserId == id)
            .ToListAsync();

        if (entities.Count == 0)
        {
            return 0;
        }

        this._context.WebhookDelegates.RemoveRange(entities);
        await this._context.SaveChangesAsync();
        return entities.Count;
    }
}
