using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class InvasionRepository : BaseRepository<InvasionEntity, Invasion>, IInvasionRepository
{
    public InvasionRepository(PoracleContext context, IMapper mapper) : base(context, mapper) { }

    protected override DbSet<InvasionEntity> DbSet => Context.Invasions;
    protected override Expression<Func<InvasionEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => i => i.Id == userId && i.ProfileNo == profileNo;
    protected override Expression<Func<InvasionEntity, bool>> UidFilter(int uid)
        => i => i.Uid == uid;
    protected override Expression<Func<InvasionEntity, bool>> UserFilter(string userId)
        => i => i.Id == userId;
}
