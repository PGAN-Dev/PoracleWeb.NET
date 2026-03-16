using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Data.Scanner.Entities;

namespace PGAN.Poracle.Web.Data.Scanner;

public class RdmScannerContext : DbContext
{
    public RdmScannerContext(DbContextOptions<RdmScannerContext> options) : base(options) { }

    public DbSet<RdmPokestopEntity> Pokestops => Set<RdmPokestopEntity>();
    public DbSet<RdmGymEntity> Gyms => Set<RdmGymEntity>();
}
