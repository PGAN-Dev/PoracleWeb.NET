namespace PGAN.Poracle.Web.Core.Models;

public class Human
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Type { get; set; }
    public int Enabled { get; set; }
    public string? Area { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Fails { get; set; }
    public string? Language { get; set; }
    public int AdminDisable { get; set; }
    public int CurrentProfileNo { get; set; }
    public string? CommunityMembership { get; set; }
}
