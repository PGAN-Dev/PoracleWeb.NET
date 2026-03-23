using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Api.Controllers;

[Route("api/dashboard")]
public class DashboardController(PoracleContext context) : BaseApiController
{
    private readonly PoracleContext _context = context;

    [HttpGet]
    public async Task<IActionResult> GetCounts()
    {
        var conn = this._context.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM monsters WHERE id = @userId AND profile_no = @profileNo) AS Monsters,
                (SELECT COUNT(*) FROM raid WHERE id = @userId AND profile_no = @profileNo) AS Raids,
                (SELECT COUNT(*) FROM egg WHERE id = @userId AND profile_no = @profileNo) AS Eggs,
                (SELECT COUNT(*) FROM quest WHERE id = @userId AND profile_no = @profileNo) AS Quests,
                (SELECT COUNT(*) FROM invasion WHERE id = @userId AND profile_no = @profileNo) AS Invasions,
                (SELECT COUNT(*) FROM lures WHERE id = @userId AND profile_no = @profileNo) AS Lures,
                (SELECT COUNT(*) FROM nests WHERE id = @userId AND profile_no = @profileNo) AS Nests,
                (SELECT COUNT(*) FROM gym WHERE id = @userId AND profile_no = @profileNo) AS Gyms";

        var userIdParam = cmd.CreateParameter();
        userIdParam.ParameterName = "@userId";
        userIdParam.Value = this.UserId;
        cmd.Parameters.Add(userIdParam);

        var profileNoParam = cmd.CreateParameter();
        profileNoParam.ParameterName = "@profileNo";
        profileNoParam.Value = this.ProfileNo;
        cmd.Parameters.Add(profileNoParam);

        await using var reader = await cmd.ExecuteReaderAsync();
        var counts = new DashboardCounts();
        if (await reader.ReadAsync())
        {
            counts.Monsters = reader.GetInt32(0);
            counts.Raids = reader.GetInt32(1);
            counts.Eggs = reader.GetInt32(2);
            counts.Quests = reader.GetInt32(3);
            counts.Invasions = reader.GetInt32(4);
            counts.Lures = reader.GetInt32(5);
            counts.Nests = reader.GetInt32(6);
            counts.Gyms = reader.GetInt32(7);
        }

        return this.Ok(counts);
    }
}
