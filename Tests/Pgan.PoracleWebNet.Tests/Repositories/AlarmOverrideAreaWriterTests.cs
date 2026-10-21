using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Repositories;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Tests.Repositories;

/// <summary>
/// <see cref="AlarmOverrideAreaWriter"/> runs raw SQL, which the EF in-memory provider cannot execute,
/// so these use SQLite in-memory with the ten alarm tables created by hand (PoracleContext maps none).
/// </summary>
public sealed class AlarmOverrideAreaWriterTests : IDisposable
{
    private static readonly string[] Tables =
        ["monsters", "raid", "egg", "quest", "invasion", "lures", "nests", "gym", "forts", "maxbattle"];

    private readonly SqliteConnection _connection;
    private readonly PoracleContext _context;
    private readonly AlarmOverrideAreaWriter _sut;

    public AlarmOverrideAreaWriterTests()
    {
        this._connection = new SqliteConnection("DataSource=:memory:");
        this._connection.Open();
        this._context = new PoracleContext(new DbContextOptionsBuilder<PoracleContext>().UseSqlite(this._connection).Options);
        foreach (var table in Tables)
        {
            this.Exec($"CREATE TABLE {table} (id TEXT NOT NULL, uid INTEGER NOT NULL, override_areas TEXT NULL)");
            this.Exec($"INSERT INTO {table} (id, uid, override_areas) VALUES ('u1', 5, NULL), ('u2', 6, '[\"keep\"]')");
        }

        this._sut = new AlarmOverrideAreaWriter(this._context);
    }

    public void Dispose()
    {
        this._context.Dispose();
        this._connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Exec(string sql)
    {
        using var command = this._connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string? Read(string table, string id, int uid)
    {
        using var command = this._connection.CreateCommand();
        command.CommandText = $"SELECT override_areas FROM {table} WHERE id = '{id}' AND uid = {uid}";
        var value = command.ExecuteScalar();
        return value is DBNull or null ? null : (string)value;
    }

    [Theory]
    [InlineData("pokemon", "monsters")]
    [InlineData("raid", "raid")]
    [InlineData("egg", "egg")]
    [InlineData("quest", "quest")]
    [InlineData("invasion", "invasion")]
    [InlineData("lure", "lures")]
    [InlineData("nest", "nests")]
    [InlineData("gym", "gym")]
    [InlineData("fort", "forts")]
    [InlineData("maxbattle", "maxbattle")]
    public async Task EveryTrackingTypeWritesItsOwnTable(string type, string table)
    {
        Assert.True(await this._sut.SetAlarmOverrideAreasAsync("u1", type, 5, ["My_Fence", "my fence", "Park"]));

        Assert.Equal("""["my fence","park"]""", this.Read(table, "u1", 5));
    }

    [Fact]
    public async Task AnotherUsersAlarmIsNotReachedEvenWithItsUid()
    {
        Assert.False(await this._sut.SetAlarmOverrideAreasAsync("u1", "pokemon", 6, ["x"]));

        Assert.Equal("""["keep"]""", this.Read("monsters", "u2", 6));
    }

    [Fact]
    public async Task AMissingRowReportsFalse() =>
        Assert.False(await this._sut.SetAlarmOverrideAreasAsync("u1", "pokemon", 999, ["x"]));

    [Fact]
    public async Task ATypeOutsideTheTenIsRefusedBeforeAnySql() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => this._sut.SetAlarmOverrideAreasAsync("u1", "monsters; DROP TABLE monsters", 5, ["x"]));

    [Fact]
    public async Task ABlankHumanIdIsRefused() =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() => this._sut.SetAlarmOverrideAreasAsync(" ", "pokemon", 5, ["x"]));
}
