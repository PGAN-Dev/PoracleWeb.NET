using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Repositories;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Tests.Repositories;

/// <summary>
/// Deleting a user removed the humans row alone, so the profiles rows survived — invisible to every API
/// surface, but re-creating the same id adopted them verbatim, and PoracleNG's human-create then collided
/// on the surviving (id, profile_no) and errored after committing the human. See #481, #482.
/// </summary>
public class HumanRepositoryDeleteTests : IDisposable
{
    private readonly PoracleContext _context;
    private readonly HumanRepository _sut;

    public HumanRepositoryDeleteTests()
    {
        var options = new DbContextOptionsBuilder<PoracleContext>()
            .UseInMemoryDatabase(databaseName: $"HumanRepositoryDelete_{Guid.NewGuid()}")
            .Options;
        this._context = new PoracleContext(options);
        this._sut = new HumanRepository(this._context);
    }

    public void Dispose()
    {
        this._context.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedAsync(string id, params int[] profileNos)
    {
        this._context.Humans.Add(new HumanEntity
        {
            Id = id,
            Name = "Test",
            Type = "webhook",
            Area = "[]",
            CommunityMembership = "[]",
        });

        foreach (var no in profileNos)
        {
            this._context.Profiles.Add(new ProfileEntity
            {
                Id = id,
                ProfileNo = no,
                Name = $"Profile {no}",
                Area = "[]",
            });
        }

        await this._context.SaveChangesAsync();
    }

    [Fact]
    public async Task DeleteUserRemovesEveryProfileItOwned()
    {
        await this.SeedAsync("gone", 1, 2, 3);

        var deleted = await this._sut.DeleteUserAsync("gone");

        Assert.True(deleted);
        Assert.Empty(await this._context.Profiles.Where(p => p.Id == "gone").ToListAsync());
        Assert.Null(await this._context.Humans.FirstOrDefaultAsync(h => h.Id == "gone"));
    }

    [Fact]
    public async Task DeleteUserLeavesEveryoneElseAlone()
    {
        await this.SeedAsync("gone", 1, 2);
        await this.SeedAsync("stays", 1, 2);

        await this._sut.DeleteUserAsync("gone");

        Assert.Equal(2, await this._context.Profiles.CountAsync(p => p.Id == "stays"));
        Assert.NotNull(await this._context.Humans.FirstOrDefaultAsync(h => h.Id == "stays"));
    }

    [Fact]
    public async Task DeletingAnUnknownUserReportsNothingDeleted()
    {
        await this.SeedAsync("stays", 1);

        Assert.False(await this._sut.DeleteUserAsync("never-existed"));
        Assert.Single(await this._context.Profiles.Where(p => p.Id == "stays").ToListAsync());
    }
}
