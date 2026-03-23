using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pgan.PoracleWebNet.Data;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// Uses a dummy connection string since migrations only need the model, not a real DB connection.
/// </summary>
public class PoracleWebContextDesignTimeFactory : IDesignTimeDbContextFactory<PoracleWebContext>
{
    public PoracleWebContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PoracleWebContext>();
        optionsBuilder.UseMySQL("Server=localhost;Database=poracle_web;Uid=root;Pwd=dummy;");
        return new PoracleWebContext(optionsBuilder.Options);
    }
}
