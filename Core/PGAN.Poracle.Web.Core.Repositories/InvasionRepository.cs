using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class InvasionRepository(PoracleContext context, IMapper mapper) : BaseRepository<InvasionEntity, Invasion>(context, mapper), IInvasionRepository
{
    protected override DbSet<InvasionEntity> DbSet => this.Context.Invasions;
    protected override Expression<Func<InvasionEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => i => i.Id == userId && i.ProfileNo == profileNo;
    protected override Expression<Func<InvasionEntity, bool>> UidFilter(int uid)
        => i => i.Uid == uid;
    protected override Expression<Func<InvasionEntity, bool>> UserFilter(string userId)
        => i => i.Id == userId;
}
