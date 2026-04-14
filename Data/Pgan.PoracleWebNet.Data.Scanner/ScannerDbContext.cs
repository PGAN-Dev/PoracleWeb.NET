using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Data.Scanner.Entities;

namespace Pgan.PoracleWebNet.Data.Scanner;

public class ScannerDbContext(DbContextOptions<ScannerDbContext> options) : DbContext(options)
{
    public DbSet<ScannerPokestopEntity> Pokestops => this.Set<ScannerPokestopEntity>();
    public DbSet<ScannerGymEntity> Gyms => this.Set<ScannerGymEntity>();
    public DbSet<ScannerStationEntity> Stations => this.Set<ScannerStationEntity>();
    public DbSet<ScannerWeatherEntity> Weather => this.Set<ScannerWeatherEntity>();
}
