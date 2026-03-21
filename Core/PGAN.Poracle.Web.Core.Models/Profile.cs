namespace PGAN.Poracle.Web.Core.Models;

public class Profile
{
    public string Id { get; set; } = string.Empty;
    public int ProfileNo
    {
        get; set;
    }
    public string? Name
    {
        get; set;
    }
    public string Area { get; set; } = "[]";
    public double Latitude
    {
        get; set;
    }
    public double Longitude
    {
        get; set;
    }
    public bool Active
    {
        get; set;
    }
}
