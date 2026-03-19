namespace PGAN.Poracle.Web.Core.Models;

public class QuestData
{
    public string PokestopId { get; set; } = string.Empty;
    public string? Name
    {
        get; set;
    }
    public double Lat
    {
        get; set;
    }
    public double Lon
    {
        get; set;
    }
    public int QuestType
    {
        get; set;
    }
    public int RewardType
    {
        get; set;
    }
    public int RewardId
    {
        get; set;
    }
}
