using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Data;

public class PoracleWebContext(DbContextOptions<PoracleWebContext> options) : DbContext(options)
{
    public DbSet<UserGeofenceEntity> UserGeofences
    {
        get; set;
    }
}
