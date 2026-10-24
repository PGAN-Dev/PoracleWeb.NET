using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Repositories;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Tests.Repositories;

public sealed class AccountLinkRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PoracleWebContext _context;
    private readonly AccountLinkRepository _repo;

    public AccountLinkRepositoryTests()
    {
        this._connection = new SqliteConnection("DataSource=:memory:");
        this._connection.Open();
        var options = new DbContextOptionsBuilder<PoracleWebContext>().UseSqlite(this._connection).Options;
        this._context = new PoracleWebContext(options);
        this._context.Database.EnsureCreated();
        this._repo = new AccountLinkRepository(this._context, NullLogger<AccountLinkRepository>.Instance);
    }

    public void Dispose()
    {
        this._context.Dispose();
        this._connection.Dispose();
    }

    private static AccountLink NewLink(string discord, string telegram, LinkSide def = LinkSide.Discord) =>
        new(0, discord, telegram, def, null, null, DateTime.UtcNow);

    [Fact]
    public async Task Add_ThenGetByEitherId_FindsRow()
    {
        var added = await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));

        Assert.True(added.Id > 0);
        var byDiscord = await this._repo.GetByHumanIdAsync("100000000000000001");
        var byTelegram = await this._repo.GetByHumanIdAsync("200000001");
        Assert.Equal(added.Id, byDiscord?.Id);
        Assert.Equal(added.Id, byTelegram?.Id);
        Assert.Null(await this._repo.GetByHumanIdAsync("999"));
    }

    [Fact]
    public async Task Add_SecondLinkForSameTelegramId_Throws()
    {
        await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));

        var ex = await Assert.ThrowsAsync<AccountLinkConflictException>(
            () => this._repo.AddAsync(NewLink("100000000000000002", "200000001")));
        Assert.Equal(LinkSide.Telegram, ex.Side);
    }

    [Fact]
    public async Task Add_SecondLinkForSameDiscordId_Throws()
    {
        await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));

        var ex = await Assert.ThrowsAsync<AccountLinkConflictException>(
            () => this._repo.AddAsync(NewLink("100000000000000001", "200000002")));
        Assert.Equal(LinkSide.Discord, ex.Side);
    }

    [Fact]
    public async Task Add_DistinctPairs_BothStored()
    {
        await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));
        await this._repo.AddAsync(NewLink("100000000000000002", "200000002"));

        Assert.Equal(2, (await this._repo.GetAllAsync()).Count);
    }

    [Fact]
    public async Task RemoveByHumanId_ByTelegramId_RemovesRow()
    {
        await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));

        Assert.Equal(1, await this._repo.RemoveByHumanIdAsync("200000001"));
        Assert.Empty(await this._repo.GetAllAsync());
    }

    [Fact]
    public async Task RemoveByHumanId_Unknown_ReturnsZero() =>
        Assert.Equal(0, await this._repo.RemoveByHumanIdAsync("nobody"));

    [Fact]
    public async Task SetDefault_ChangesOnlyThatRow()
    {
        var a = await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));
        var b = await this._repo.AddAsync(NewLink("100000000000000002", "200000002"));

        Assert.True(await this._repo.SetDefaultAsync(a.Id, LinkSide.Telegram));
        Assert.False(await this._repo.SetDefaultAsync(9999, LinkSide.Telegram));

        var all = await this._repo.GetAllAsync();
        Assert.Equal(LinkSide.Telegram, all.Single(l => l.Id == a.Id).DefaultDestination);
        Assert.Equal(LinkSide.Discord, all.Single(l => l.Id == b.Id).DefaultDestination);
    }

    [Fact]
    public async Task TouchVerified_SetsOnlyThatSide()
    {
        var a = await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));
        var at = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(await this._repo.TouchVerifiedAsync(a.Id, LinkSide.Telegram, at));
        Assert.False(await this._repo.TouchVerifiedAsync(9999, LinkSide.Telegram, at));

        var row = (await this._repo.GetAllAsync()).Single();
        Assert.Equal(at, row.TelegramVerifiedAt);
        Assert.Null(row.DiscordVerifiedAt);
    }

    [Fact]
    public async Task Add_ANonUniqueFailure_PropagatesAsDbUpdateException()
    {
        // NOT NULL is a constraint violation too (SQLite code 19), but not a uniqueness one: it must not read as "already linked".
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => this._repo.AddAsync(NewLink(null!, "200000001")));

        Assert.IsNotType<AccountLinkConflictException>(ex);
    }

    [Fact]
    public async Task Add_AfterAConflict_TheContextStillSaves()
    {
        await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));
        await Assert.ThrowsAsync<AccountLinkConflictException>(() => this._repo.AddAsync(NewLink("100000000000000002", "200000001")));

        var next = await this._repo.AddAsync(NewLink("100000000000000003", "200000003"));

        Assert.True(next.Id > 0);
        Assert.True(await this._repo.SetDefaultAsync(next.Id, LinkSide.Telegram));
        Assert.Equal(2, (await this._repo.GetAllAsync()).Count);
    }

    [Fact]
    public async Task GetAll_AnUnreadableRow_IsSkippedAndTheGoodOneReturned()
    {
        await this._repo.AddAsync(NewLink("100000000000000001", "200000001"));
        var bad = await this._repo.AddAsync(NewLink("100000000000000002", "200000002"));
        await this._context.Database.ExecuteSqlRawAsync("UPDATE account_links SET default_destination = 'webhook' WHERE id = {0}", bad.Id);

        var all = await this._repo.GetAllAsync();

        var only = Assert.Single(all);
        Assert.Equal("100000000000000001", only.DiscordId);
    }
}
