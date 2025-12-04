namespace Drasicrhsit.Infrastructure;

public interface IFallbackUtils
{
    string Rationale(string reason);
    IReadOnlyList<string> DefaultPreferences();
}

public sealed class FallbackUtils : IFallbackUtils
{
    public string Rationale(string reason) => "Fallback used: " + reason;
    public IReadOnlyList<string> DefaultPreferences() => new[] { "books", "creative", "outdoor" };
}
