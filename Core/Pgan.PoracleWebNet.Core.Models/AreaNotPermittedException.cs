namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// An Areas save named fences the caller may not subscribe to: neither selectable for them, nor their
/// own drawn fences, nor already on the profile.
/// </summary>
public sealed class AreaNotPermittedException : Exception
{
    public AreaNotPermittedException(IReadOnlyList<string> names)
        : base("These areas are not available to you: " + string.Join(", ", names))
    {
        this.Names = names;
    }

    public AreaNotPermittedException()
        : this([])
    {
    }

    public AreaNotPermittedException(string message)
        : base(message)
    {
        this.Names = [];
    }

    public AreaNotPermittedException(string message, Exception innerException)
        : base(message, innerException)
    {
        this.Names = [];
    }

    public IReadOnlyList<string> Names
    {
        get;
    }
}
