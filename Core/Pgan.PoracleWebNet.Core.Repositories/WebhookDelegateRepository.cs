using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class WebhookDelegateRepository(PoracleWebContext context, IMapper mapper) : IWebhookDelegateRepository
{
    private readonly PoracleWebContext _context = context;
    private readonly IMapper _mapper = mapper;

    public async Task<IEnumerable<WebhookDelegate>> GetAllAsync()
    {
        var entities = await this._context.WebhookDelegates
            .AsNoTracking()
            .ToListAsync();

        return this._mapper.Map<IEnumerable<WebhookDelegate>>(entities);
    }

    public async Task<IEnumerable<WebhookDelegate>> GetByWebhookIdAsync(string webhookId)
    {
        var entities = await this._context.WebhookDelegates
            .AsNoTracking()
            .Where(d => d.WebhookId == webhookId)
            .ToListAsync();

        return this._mapper.Map<IEnumerable<WebhookDelegate>>(entities);
    }

    public async Task<IEnumerable<string>> GetWebhookIdsByUserIdAsync(string userId) => await this._context.WebhookDelegates
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .Select(d => d.WebhookId)
            .ToListAsync();

    public async Task<WebhookDelegate> AddAsync(string webhookId, string userId)
    {
        // Check for existing delegate to avoid unique constraint violation
        var existing = await this._context.WebhookDelegates
            .FirstOrDefaultAsync(d => d.WebhookId == webhookId && d.UserId == userId);

        if (existing is not null)
        {
            return this._mapper.Map<WebhookDelegate>(existing);
        }

        var entity = new WebhookDelegateEntity
        {
            WebhookId = webhookId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        };

        this._context.WebhookDelegates.Add(entity);
        await this._context.SaveChangesAsync();

        return this._mapper.Map<WebhookDelegate>(entity);
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
}
