using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class RaidRepository(PoracleContext context, IMapper mapper) : BaseRepository<RaidEntity, Raid>(context, mapper), IRaidRepository
{
    protected override DbSet<RaidEntity> DbSet => this.Context.Raids;
    protected override Expression<Func<RaidEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => r => r.Id == userId && r.ProfileNo == profileNo;
    protected override Expression<Func<RaidEntity, bool>> UidFilter(int uid)
        => r => r.Uid == uid;
    protected override Expression<Func<RaidEntity, bool>> UserFilter(string userId)
        => r => r.Id == userId;
}
