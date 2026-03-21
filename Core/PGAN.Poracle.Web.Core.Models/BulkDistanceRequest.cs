namespace PGAN.Poracle.Web.Core.Models;

public class BulkDistanceRequest
{
    public List<int> Uids { get; set; } = [];
    public int Distance
    {
        get; set;
    }
}
