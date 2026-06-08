using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Repositories;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Tests.Repositories;

/// <summary>
/// Repository tests over a real relational provider (SQLite in-memory) because the rotation guard
/// and cleanup use <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c>, which the EF InMemory
/// provider cannot translate. Covers the retention semantics (decoupled revoked-row retention) and
/// the atomic rotation guard.
/// </summary>
public sealed class OidcSessionRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PoracleWebContext _context;
    private readonly OidcSessionRepository _repo;

    public OidcSessionRepositoryTests()
    {
        this._connection = new SqliteConnection("DataSource=:memory:");
        this._connection.Open();
        var options = new DbContextOptionsBuilder<PoracleWebContext>()
            .UseSqlite(this._connection)
            .Options;
        this._context = new PoracleWebContext(options);
        this._context.Database.EnsureCreated();
        this._repo = new OidcSessionRepository(this._context);
    }

    public void Dispose()
    {
        this._context.Dispose();
        this._connection.Dispose();
    }

    private async Task<OidcSession> SeedAsync(string hash, string family, DateTime expiresAt, DateTime? revokedAt = null, string userId = "user-1")
    {
        var session = new OidcSession
        {
            SessionTokenHash = hash,
            FamilyId = family,
            FamilyIssuedAt = DateTime.UtcNow.AddMinutes(-5),
            UserId = userId,
            EncryptedRefreshToken = "cipher",
            ExpiresAt = expiresAt,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-5),
            RevokedAt = revokedAt,
            RevokedReason = revokedAt is null ? null : "rotation",
        };
        await this._repo.AddAsync(session);
        return session;
    }

    [Fact]
    public async Task DeleteExpiredAndStale_DeletesExpired_AndOldRevoked_ButKeepsActiveAndRecentRevoked()
    {
        var now = DateTime.UtcNow;
        await this.SeedAsync("active", "f1", expiresAt: now.AddDays(20));                                   // active → keep
        await this.SeedAsync("expired", "f2", expiresAt: now.AddMinutes(-1));                               // expired → delete
        await this.SeedAsync("revoked-recent", "f3", expiresAt: now.AddDays(20), revokedAt: now.AddDays(-1)); // revoked 1d ago → keep (retention 2d)
        await this.SeedAsync("revoked-old", "f4", expiresAt: now.AddDays(20), revokedAt: now.AddDays(-5));    // revoked 5d ago → delete

        var deleted = await this._repo.DeleteExpiredAndStaleAsync(TimeSpan.FromDays(2));

        Assert.Equal(2, deleted);
        var remaining = await this._context.OidcSessions.Select(s => s.SessionTokenHash).ToListAsync();
        Assert.Contains("active", remaining);
        Assert.Contains("revoked-recent", remaining);
        Assert.DoesNotContain("expired", remaining);
        Assert.DoesNotContain("revoked-old", remaining);
    }

    [Fact]
    public async Task TryRevokeForRotation_RevokesActiveRow_ReturnsOne_AndChainsSuccessor()
    {
        await this.SeedAsync("present", "f1", expiresAt: DateTime.UtcNow.AddDays(20));

        var affected = await this._repo.TryRevokeForRotationAsync("present", "successor");

        Assert.Equal(1, affected);
        var row = await this._context.OidcSessions.AsNoTracking().FirstAsync(s => s.SessionTokenHash == "present");
        Assert.NotNull(row.RevokedAt);
        Assert.Equal("rotation", row.RevokedReason);
        Assert.Equal("successor", row.ReplacedByHash);
    }

    [Fact]
    public async Task TryRevokeForRotation_AlreadyRevoked_ReturnsZero()
    {
        await this.SeedAsync("present", "f1", expiresAt: DateTime.UtcNow.AddDays(20), revokedAt: DateTime.UtcNow.AddMinutes(-1));

        var affected = await this._repo.TryRevokeForRotationAsync("present", "successor");

        Assert.Equal(0, affected);
    }

    [Fact]
    public async Task RevokeFamily_RevokesAllActiveInFamily_Only()
    {
        await this.SeedAsync("a", "fam", expiresAt: DateTime.UtcNow.AddDays(20));
        await this.SeedAsync("b", "fam", expiresAt: DateTime.UtcNow.AddDays(20));
        await this.SeedAsync("other", "other-fam", expiresAt: DateTime.UtcNow.AddDays(20));

        var revoked = await this._repo.RevokeFamilyAsync("fam", "replay_detected");

        Assert.Equal(2, revoked);
        var other = await this._context.OidcSessions.AsNoTracking().FirstAsync(s => s.SessionTokenHash == "other");
        Assert.Null(other.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllForUser_RevokesActiveSessionsForThatUser_Only()
    {
        await this.SeedAsync("u1a", "f1", expiresAt: DateTime.UtcNow.AddDays(20), userId: "user-1");
        await this.SeedAsync("u1b", "f2", expiresAt: DateTime.UtcNow.AddDays(20), userId: "user-1");
        await this.SeedAsync("u2", "f3", expiresAt: DateTime.UtcNow.AddDays(20), userId: "user-2");

        var revoked = await this._repo.RevokeAllForUserAsync("user-1", "admin_disable");

        Assert.Equal(2, revoked);
        var u2 = await this._context.OidcSessions.AsNoTracking().FirstAsync(s => s.SessionTokenHash == "u2");
        Assert.Null(u2.RevokedAt);
    }

    [Fact]
    public async Task GetByHash_ReturnsMatchingSession_OrNull()
    {
        await this.SeedAsync("known", "f1", expiresAt: DateTime.UtcNow.AddDays(20));

        Assert.NotNull(await this._repo.GetByHashAsync("known"));
        Assert.Null(await this._repo.GetByHashAsync("missing"));
    }
}
