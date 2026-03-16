using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class LureRepository : BaseRepository<LureEntity, Lure>, ILureRepository
{
    public LureRepository(PoracleContext context, IMapper mapper) : base(context, mapper) { }

    protected override DbSet<LureEntity> DbSet => Context.Lures;
    protected override Expression<Func<LureEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => l => l.Id == userId && l.ProfileNo == profileNo;
    protected override Expression<Func<LureEntity, bool>> UidFilter(int uid)
        => l => l.Uid == uid;
    protected override Expression<Func<LureEntity, bool>> UserFilter(string userId)
        => l => l.Id == userId;
}
