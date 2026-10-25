using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Repositories;

public interface IAccountLinkRepository
{
    /// <summary>The link holding this id as either its Discord or its Telegram id, or null.</summary>
    public Task<AccountLink?> GetByHumanIdAsync(string humanId);

    public Task<IReadOnlyList<AccountLink>> GetAllAsync();

    /// <exception cref="AccountLinkConflictException">Either id is already in another link.</exception>
    public Task<AccountLink> AddAsync(AccountLink link);

    public Task<bool> SetDefaultAsync(int id, LinkSide side);

    public Task<bool> TouchVerifiedAsync(int id, LinkSide side, DateTime at);

    /// <summary>Removes the link holding this id as either side. Returns the number of rows removed.</summary>
    public Task<int> RemoveByHumanIdAsync(string humanId);
}
