using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class QuestRepository(PoracleContext context, IMapper mapper) : BaseRepository<QuestEntity, Quest>(context, mapper), IQuestRepository
{
    protected override DbSet<QuestEntity> DbSet => this.Context.Quests;
    protected override Expression<Func<QuestEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => q => q.Id == userId && q.ProfileNo == profileNo;
    protected override Expression<Func<QuestEntity, bool>> UidFilter(int uid)
        => q => q.Uid == uid;
    protected override Expression<Func<QuestEntity, bool>> UserFilter(string userId)
        => q => q.Id == userId;
}
