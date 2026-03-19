using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class GymRepository(PoracleContext context, IMapper mapper) : BaseRepository<GymEntity, Gym>(context, mapper), IGymRepository
{
    protected override DbSet<GymEntity> DbSet => this.Context.Gyms;
    protected override Expression<Func<GymEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => g => g.Id == userId && g.ProfileNo == profileNo;
    protected override Expression<Func<GymEntity, bool>> UidFilter(int uid)
        => g => g.Uid == uid;
    protected override Expression<Func<GymEntity, bool>> UserFilter(string userId)
        => g => g.Id == userId;
}
