using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pgan.PoracleWebNet.Data.Scanner.Entities;

[Table("weather")]
public class ScannerWeatherEntity
{
    [Key]
    [Column("id")]
    public long Id
    {
        get; set;
    }

    [Column("level")]
    public int? Level
    {
        get; set;
    }

    [Column("latitude")]
    public double Latitude
    {
        get; set;
    }

    [Column("longitude")]
    public double Longitude
    {
        get; set;
    }

    [Column("gameplay_condition")]
    public int? GameplayCondition
    {
        get; set;
    }

    [Column("wind_direction")]
    public int? WindDirection
    {
        get; set;
    }

    [Column("cloud_level")]
    public int? CloudLevel
    {
        get; set;
    }

    [Column("rain_level")]
    public int? RainLevel
    {
        get; set;
    }

    [Column("wind_level")]
    public int? WindLevel
    {
        get; set;
    }

    [Column("snow_level")]
    public int? SnowLevel
    {
        get; set;
    }

    [Column("fog_level")]
    public int? FogLevel
    {
        get; set;
    }

    [Column("severity")]
    public int? Severity
    {
        get; set;
    }

    [Column("warn_weather")]
    public int? WarnWeather
    {
        get; set;
    }

    [Column("updated")]
    public long? Updated
    {
        get; set;
    }
}
