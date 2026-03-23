using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Repositories;

public class LureRepository(PoracleContext context, IMapper mapper) : BaseRepository<LureEntity, Lure>(context, mapper), ILureRepository
{
    protected override DbSet<LureEntity> DbSet => this.Context.Lures;
    protected override Expression<Func<LureEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => l => l.Id == userId && l.ProfileNo == profileNo;
    protected override Expression<Func<LureEntity, bool>> UidFilter(int uid)
        => l => l.Uid == uid;
    protected override Expression<Func<LureEntity, bool>> UserFilter(string userId)
        => l => l.Id == userId;
}
