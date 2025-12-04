using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public interface IProfilePreferenceExtractor
{
    Task<IReadOnlyList<string>> ExtractAsync(string childId, CancellationToken ct = default);
}

public sealed class ProfilePreferenceExtractor : IProfilePreferenceExtractor
{
    private readonly IWishlistRepository _wishlists;
    public ProfilePreferenceExtractor(IWishlistRepository wishlists) => _wishlists = wishlists;

    public async Task<IReadOnlyList<string>> ExtractAsync(string childId, CancellationToken ct = default)
    {
        var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        await foreach (var w in _wishlists.ListAsync(childId).WithCancellation(ct))
        {
            if (!string.IsNullOrWhiteSpace(w.Category)) set.Add(w.Category!);
        }
        return set.Count == 0 ? new List<string>() : set.ToList();
    }
}