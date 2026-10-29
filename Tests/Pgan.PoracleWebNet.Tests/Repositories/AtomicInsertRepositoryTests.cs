using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Repositories;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Tests.Repositories;

/// <summary>
/// A duplicate write on a unique key must never reach the provider as an error: a webhook's id is its URL
/// with the token in it, and both EF Core's own Update error line and the global exception handler print
/// the MySQL duplicate-entry text, key included. The race is reproduced by letting a competing writer
/// land its row in the gap between our read and our write (the interceptor fires just before the first
/// INSERT into the table). See #1101.
/// </summary>
public sealed class AtomicInsertRepositoryTests : IDisposable
{
    private const string Webhook = "https://discord.example/api/webhooks/123456789/SECRET-TOKEN";

    private readonly SqliteConnection _connection;
    private readonly PoracleWebContext _context;
    private readonly RaceInterceptor _race = new();

    public AtomicInsertRepositoryTests()
    {
        this._connection = new SqliteConnection("DataSource=:memory:");
        this._connection.Open();
        var options = new DbContextOptionsBuilder<PoracleWebContext>()
            .UseSqlite(this._connection)
            .AddInterceptors(this._race)
            .Options;
        this._context = new PoracleWebContext(options);
        this._context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        this._context.Dispose();
        this._connection.Dispose();
    }

    private int Count(string table)
    {
        using var cmd = this._connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void ArmRace(string table, string competingSql)
    {
        this._race.Connection = this._connection;
        this._race.Table = table;
        this._race.CompetingSql = competingSql;
    }

    // --- webhook_delegates ---

    [Fact]
    public async Task AddDelegate_NewGrant_InsertsAndReturnsRow()
    {
        var repo = new WebhookDelegateRepository(this._context);

        var added = await repo.AddAsync(Webhook, "user-1");

        Assert.True(added.Id > 0);
        Assert.Equal(Webhook, added.WebhookId);
        Assert.Equal("user-1", added.UserId);
        Assert.Equal(1, this.Count("webhook_delegates"));
    }

    [Fact]
    public async Task AddDelegate_ExistingGrant_ReturnsSameRowWithoutDuplicating()
    {
        var repo = new WebhookDelegateRepository(this._context);
        var first = await repo.AddAsync(Webhook, "user-1");

        var second = await repo.AddAsync(Webhook, "user-1");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, this.Count("webhook_delegates"));
    }

    [Fact]
    public async Task AddDelegate_SameUserOnAnotherWebhook_IsAnotherGrant()
    {
        var repo = new WebhookDelegateRepository(this._context);

        var a = await repo.AddAsync(Webhook, "user-1");
        var b = await repo.AddAsync(Webhook + "-other", "user-1");

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, this.Count("webhook_delegates"));
    }

    [Fact]
    public async Task AddDelegate_CompetingGrantLandsFirst_DoesNotThrowAndReturnsTheRow()
    {
        var repo = new WebhookDelegateRepository(this._context);
        this.ArmRace("webhook_delegates",
            $"INSERT INTO webhook_delegates (webhook_id, user_id, created_at) VALUES ('{Webhook}', 'user-1', '2026-01-01 00:00:00')");

        var added = await repo.AddAsync(Webhook, "user-1");

        Assert.True(this._race.Fired, "the race was never staged, so the test proves nothing");
        Assert.True(added.Id > 0);
        Assert.Equal("user-1", added.UserId);
        Assert.Equal(1, this.Count("webhook_delegates"));
    }

    [Fact]
    public async Task RemoveDelegate_AfterAdd_RemovesIt()
    {
        var repo = new WebhookDelegateRepository(this._context);
        await repo.AddAsync(Webhook, "user-1");
        await repo.AddAsync(Webhook, "user-2");

        Assert.True(await repo.RemoveAsync(Webhook, "user-1"));
        Assert.False(await repo.RemoveAsync(Webhook, "user-1"));

        Assert.Equal(["user-2"], (await repo.GetByWebhookIdAsync(Webhook)).Select(d => d.UserId).ToArray());
    }

    // --- quick_pick_applied_states ---

    private static QuickPickAppliedState State(string userId, List<int> uids, string alarmType = "monster") => new()
    {
        UserId = userId,
        ProfileNo = 1,
        QuickPickId = "pick-1",
        AlarmType = alarmType,
        ExcludePokemonIds = [25],
        TrackedUids = uids,
    };

    [Fact]
    public async Task AppliedState_NewRow_IsStored()
    {
        var repo = new QuickPickAppliedStateRepository(this._context);

        await repo.CreateOrUpdateAsync(State("user-1", [10, 11]));

        var stored = await repo.GetAsync("user-1", 1, "pick-1");
        Assert.NotNull(stored);
        Assert.Equal([10, 11], stored.TrackedUids);
        Assert.Equal([25], stored.ExcludePokemonIds);
        Assert.Equal("monster", stored.AlarmType);
    }

    [Fact]
    public async Task AppliedState_ExistingRow_IsUpdatedInPlace_KeepingItsAlarmType()
    {
        var repo = new QuickPickAppliedStateRepository(this._context);
        await repo.CreateOrUpdateAsync(State("user-1", [10], "raid"));

        await repo.CreateOrUpdateAsync(State("user-1", [10, 12], "monster"));

        Assert.Equal(1, this.Count("quick_pick_applied_states"));
        var stored = await repo.GetAsync("user-1", 1, "pick-1");
        Assert.Equal([10, 12], stored!.TrackedUids);
        // The update path has never rewritten the stored type; the apply path relies on it.
        Assert.Equal("raid", stored.AlarmType);
    }

    [Fact]
    public async Task AppliedState_CompetingWriteLandsFirst_DoesNotThrowAndOurWriteWins()
    {
        var repo = new QuickPickAppliedStateRepository(this._context);
        this.ArmRace("quick_pick_applied_states",
            "INSERT INTO quick_pick_applied_states (user_id, profile_no, quick_pick_id, alarm_type, applied_at, tracked_uids_json) "
            + "VALUES ('user-1', 1, 'pick-1', 'monster', '2026-01-01 00:00:00', '[99]')");

        await repo.CreateOrUpdateAsync(State("user-1", [10, 11]));

        Assert.True(this._race.Fired, "the race was never staged, so the test proves nothing");
        Assert.Equal(1, this.Count("quick_pick_applied_states"));
        Assert.Equal([10, 11], (await repo.GetAsync("user-1", 1, "pick-1"))!.TrackedUids);
    }

    [Fact]
    public async Task AppliedState_Delete_StillRemovesTheRow()
    {
        var repo = new QuickPickAppliedStateRepository(this._context);
        await repo.CreateOrUpdateAsync(State("user-1", [10]));

        await repo.DeleteAsync("user-1", 1, "pick-1");

        Assert.Null(await repo.GetAsync("user-1", 1, "pick-1"));
    }

    private sealed class RaceInterceptor : DbCommandInterceptor
    {
        public SqliteConnection? Connection { get; set; }
        public string? Table { get; set; }
        public string? CompetingSql { get; set; }
        public bool Fired { get; private set; }

        private void MaybeRace(DbCommand command)
        {
            if (this.Fired || this.Connection is null || this.Table is null || this.CompetingSql is null)
            {
                return;
            }

            if (!command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains(this.Table, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this.Fired = true;
            using var competing = this.Connection.CreateCommand();
            competing.CommandText = this.CompetingSql;
            competing.ExecuteNonQuery();
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            this.MaybeRace(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            this.MaybeRace(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            this.MaybeRace(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            this.MaybeRace(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
