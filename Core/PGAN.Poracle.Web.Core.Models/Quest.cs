namespace PGAN.Poracle.Web.Core.Models;

public class Quest
{
    public int Uid
    {
        get; set;
    }
    public string Id { get; set; } = string.Empty;
    public string? Ping
    {
        get; set;
    }
    public int Distance
    {
        get; set;
    }
    public int Reward
    {
        get; set;
    }
    public int RewardType
    {
        get; set;
    }
    public int Shiny
    {
        get; set;
    }
    public int Clean
    {
        get; set;
    }
    public string? Template
    {
        get; set;
    }
    public int ProfileNo
    {
        get; set;
    }
    public int Form
    {
        get; set;
    }
}
