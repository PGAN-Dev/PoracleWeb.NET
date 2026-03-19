using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class NestRepository(PoracleContext context, IMapper mapper) : BaseRepository<NestEntity, Nest>(context, mapper), INestRepository
{
    protected override DbSet<NestEntity> DbSet => this.Context.Nests;
    protected override Expression<Func<NestEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => n => n.Id == userId && n.ProfileNo == profileNo;
    protected override Expression<Func<NestEntity, bool>> UidFilter(int uid)
        => n => n.Uid == uid;
    protected override Expression<Func<NestEntity, bool>> UserFilter(string userId)
        => n => n.Id == userId;
}
