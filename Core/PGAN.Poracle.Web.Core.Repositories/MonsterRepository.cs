using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class MonsterRepository : BaseRepository<MonsterEntity, Monster>, IMonsterRepository
{
    public MonsterRepository(PoracleContext context, IMapper mapper) : base(context, mapper) { }

    protected override DbSet<MonsterEntity> DbSet => Context.Monsters;
    protected override Expression<Func<MonsterEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => e => e.Id == userId && e.ProfileNo == profileNo;
    protected override Expression<Func<MonsterEntity, bool>> UidFilter(int uid)
        => e => e.Uid == uid;
    protected override Expression<Func<MonsterEntity, bool>> UserFilter(string userId)
        => e => e.Id == userId;
}
