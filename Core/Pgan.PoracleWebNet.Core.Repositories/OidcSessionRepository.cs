using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class OidcSessionRepository(PoracleWebContext context) : IOidcSessionRepository
{
    private readonly PoracleWebContext _context = context;

    public async Task<OidcSession?> GetByHashAsync(string sessionTokenHash)
    {
        var entity = await this._context.OidcSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionTokenHash == sessionTokenHash);

        return entity is null ? null : ToModel(entity);
    }

    public async Task AddAsync(OidcSession session)
    {
        this._context.OidcSessions.Add(ToEntity(session));
        await this._context.SaveChangesAsync();
    }

    public async Task<int> TryRevokeForRotationAsync(string sessionTokenHash, string newHash)
    {
        // EF query lambdas require == null / != null (translates to IS NULL); `is null` throws.
        DateTime now = DateTime.UtcNow;
        return await this._context.OidcSessions
            .Where(s => s.SessionTokenHash == sessionTokenHash && s.RevokedAt == null && s.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, "rotation")
                .SetProperty(s => s.ReplacedByHash, newHash));
    }

    public async Task<int> RevokeFamilyAsync(string familyId, string reason)
    {
        DateTime now = DateTime.UtcNow;
        return await this._context.OidcSessions
            .Where(s => s.FamilyId == familyId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, reason));
    }

    public async Task<int> RevokeAllForUserAsync(string userId, string reason)
    {
        DateTime now = DateTime.UtcNow;
        return await this._context.OidcSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevokedReason, reason));
    }

    public async Task<int> DeleteExpiredAndStaleAsync(TimeSpan revokedRetention)
    {
        DateTime now = DateTime.UtcNow;
        DateTime revokedCutoff = now - revokedRetention;
        return await this._context.OidcSessions
            .Where(s => s.ExpiresAt < now || (s.RevokedAt != null && s.RevokedAt < revokedCutoff))
            .ExecuteDeleteAsync();
    }

    private static OidcSession ToModel(OidcSessionEntity e) => new()
    {
        Id = e.Id,
        SessionTokenHash = e.SessionTokenHash,
        FamilyId = e.FamilyId,
        FamilyIssuedAt = e.FamilyIssuedAt,
        UserId = e.UserId,
        EncryptedRefreshToken = e.EncryptedRefreshToken,
        ExpiresAt = e.ExpiresAt,
        CreatedUtc = e.CreatedUtc,
        RevokedAt = e.RevokedAt,
        RevokedReason = e.RevokedReason,
        ReplacedByHash = e.ReplacedByHash,
        IpAddress = e.IpAddress,
        UserAgent = e.UserAgent,
    };

    private static OidcSessionEntity ToEntity(OidcSession m) => new()
    {
        SessionTokenHash = m.SessionTokenHash,
        FamilyId = m.FamilyId,
        FamilyIssuedAt = m.FamilyIssuedAt,
        UserId = m.UserId,
        EncryptedRefreshToken = m.EncryptedRefreshToken,
        ExpiresAt = m.ExpiresAt,
        CreatedUtc = m.CreatedUtc,
        RevokedAt = m.RevokedAt,
        RevokedReason = m.RevokedReason,
        ReplacedByHash = m.ReplacedByHash,
        IpAddress = m.IpAddress,
        UserAgent = m.UserAgent,
    };
}
