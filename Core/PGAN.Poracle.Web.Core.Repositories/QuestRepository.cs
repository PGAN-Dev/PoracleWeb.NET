using System.Linq.Expressions;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Repositories;

public class QuestRepository : BaseRepository<QuestEntity, Quest>, IQuestRepository
{
    public QuestRepository(PoracleContext context, IMapper mapper) : base(context, mapper) { }

    protected override DbSet<QuestEntity> DbSet => Context.Quests;
    protected override Expression<Func<QuestEntity, bool>> UserProfileFilter(string userId, int profileNo)
        => q => q.Id == userId && q.ProfileNo == profileNo;
    protected override Expression<Func<QuestEntity, bool>> UidFilter(int uid)
        => q => q.Uid == uid;
    protected override Expression<Func<QuestEntity, bool>> UserFilter(string userId)
        => q => q.Id == userId;
}
