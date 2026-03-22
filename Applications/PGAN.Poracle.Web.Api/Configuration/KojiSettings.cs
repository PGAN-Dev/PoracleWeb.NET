namespace PGAN.Poracle.Web.Api.Configuration;

public class KojiSettings
{
    public string ApiAddress { get; set; } = string.Empty;
    public string BearerToken { get; set; } = string.Empty;
    public int ProjectId
    {
        get; set;
    }
    public string ProjectName { get; set; } = string.Empty;
}
