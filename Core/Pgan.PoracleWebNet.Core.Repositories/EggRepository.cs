using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class EggRepository(PoracleContext context, IMapper mapper) : BaseRepository<EggEntity, Egg>(context, mapper), IEggRepository
{
    protected override DbSet<EggEntity> DbSet => this.Context.Eggs;
    protected override Expression<Func<EggEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => e => e.Id == userId && e.ProfileNo == profileNo;
    protected override Expression<Func<EggEntity, bool>> UidFilter(int uid)
        => e => e.Uid == uid;
    protected override Expression<Func<EggEntity, bool>> UserFilter(string userId)
        => e => e.Id == userId;
}
