using Microsoft.EntityFrameworkCore;
using PGAN.Poracle.Web.Data.Scanner.Entities;

namespace PGAN.Poracle.Web.Data.Scanner;

public class RdmScannerContext(DbContextOptions<RdmScannerContext> options) : DbContext(options)
{
    public DbSet<RdmPokestopEntity> Pokestops => this.Set<RdmPokestopEntity>();
    public DbSet<RdmGymEntity> Gyms => this.Set<RdmGymEntity>();
}
