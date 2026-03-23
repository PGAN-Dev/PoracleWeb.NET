using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

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
