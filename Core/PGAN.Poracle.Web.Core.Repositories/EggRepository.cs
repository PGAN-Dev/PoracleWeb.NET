using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class EggRepository : BaseRepository<EggEntity, Egg>, IEggRepository
{
    public EggRepository(PoracleContext context, IMapper mapper) : base(context, mapper) { }

    protected override DbSet<EggEntity> DbSet => Context.Eggs;
    protected override Expression<Func<EggEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => e => e.Id == userId && e.ProfileNo == profileNo;
    protected override Expression<Func<EggEntity, bool>> UidFilter(int uid)
        => e => e.Uid == uid;
    protected override Expression<Func<EggEntity, bool>> UserFilter(string userId)
        => e => e.Id == userId;
}
