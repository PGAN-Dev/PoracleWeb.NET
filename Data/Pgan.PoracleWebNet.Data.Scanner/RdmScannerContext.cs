using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Data.Scanner.Entities;

namespace Pgan.PoracleWebNet.Data.Scanner;

public class RdmScannerContext(DbContextOptions<RdmScannerContext> options) : DbContext(options)
{
    public DbSet<RdmPokestopEntity> Pokestops => this.Set<RdmPokestopEntity>();
    public DbSet<RdmGymEntity> Gyms => this.Set<RdmGymEntity>();
    public DbSet<RdmStationEntity> Stations => this.Set<RdmStationEntity>();
    public DbSet<RdmWeatherEntity> Weather => this.Set<RdmWeatherEntity>();
}
