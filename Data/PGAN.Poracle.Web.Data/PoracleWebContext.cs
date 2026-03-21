using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Data;

public class PoracleWebContext(DbContextOptions<PoracleWebContext> options) : DbContext(options)
{
    public DbSet<UserGeofenceEntity> UserGeofences { get; set; }
}
