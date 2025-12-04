using Models;
using Drasicrhsit.Infrastructure;

namespace Services;

public interface IWishlistService
{
    Task<WishlistItem> AddAsync(string childId, string text, string? category, double? budgetEstimate, CancellationToken ct = default);
    Task<LetterToNorthPole> AddLetterAsync(string childId, string requestType, string text, string? category, double? budgetEstimate, string? statusChange = null, CancellationToken ct = default);
}

public sealed class WishlistService : IWishlistService
{
    readonly IWishlistRepository _repo;
    public WishlistService(IWishlistRepository repo) => _repo = repo;

    public async Task<WishlistItem> AddAsync(string childId, string text, string? category, double? budgetEstimate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text); // CA1062/CA1510
        var entity = new WishlistItemEntity
        {
            ChildId = childId,
            RequestType = "gift",
            Text = text,
            Category = category,
            BudgetEstimate = budgetEstimate,
            DedupeKey = DedupeKeyHasher.Compute(childId, text.ToLowerInvariant().Trim())
        };
        await _repo.UpsertAsync(entity);
        return new WishlistItem(entity.id, entity.ChildId, entity.Text, entity.Category, null, null, null);
    }

    public async Task<LetterToNorthPole> AddLetterAsync(string childId, string requestType, string text, string? category, double? budgetEstimate, string? statusChange = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entity = new WishlistItemEntity
        {
            ChildId = childId,
            RequestType = requestType,
            Text = text,
            Category = category,
            BudgetEstimate = budgetEstimate,
            StatusChange = statusChange,
            DedupeKey = DedupeKeyHasher.Compute(childId, requestType + text.ToLowerInvariant().Trim() + (statusChange ?? ""))
        };
        await _repo.UpsertAsync(entity);

        NiceStatus? status = statusChange switch
        {
            "Nice" => NiceStatus.Nice,
            "Naughty" => NiceStatus.Naughty,
            _ => null
        };

        return new LetterToNorthPole(entity.id, entity.ChildId, entity.RequestType, entity.Text, entity.Category, null, null, null, status);
    }
}
