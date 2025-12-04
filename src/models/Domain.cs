using System.Text.Json.Serialization;

namespace Models;

// Behavior status for naughty/nice tracking
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NiceStatus
{
    Nice,
    Naughty,
    Unknown
}

public record ChildProfile(
    string Id,
    string? Name,
    int? Age,
    string[]? Preferences,
    Constraints? Constraints,
    PrivacyFlags? PrivacyFlags,
    NiceStatus Status = NiceStatus.Unknown  // Default to Unknown
);

public record Constraints(decimal? Budget);
public record PrivacyFlags(bool OptOut);

// Letter to the North Pole - replaces wishlist concept
public record LetterToNorthPole(
    string Id,
    string ChildId,
    string RequestType,  // "gift" or "behavior-update"
    string ItemName,     // Gift name or behavior description
    string? Category,
    string[]? Tags,
    int? Priority,
    string? Notes,
    NiceStatus? StatusChange  // For behavior-update type letters
);

// Legacy alias for compatibility during transition
public record WishlistItem(
    string Id,
    string ChildId,
    string ItemName,
    string? Category,
    string[]? Tags,
    int? Priority,
    string? Notes
) : LetterToNorthPole(Id, ChildId, "gift", ItemName, Category, Tags, Priority, Notes, null);

public record Recommendation(
    string Id,
    string ChildId,
    string Suggestion,
    string Rationale,
    decimal? Price,
    string BudgetFit,
    Availability? Availability
);

public record Availability(bool? InStock, int? LeadTimeDays);

public record Report(
    string Id,
    string ChildId,
    DateTime CreatedAt,
    IEnumerable<Recommendation> Recommendations,
    string Summary,
    string Label,
    string Disclaimer,
    string Format,
    string Path
);

public record WorkshopEvent(
    string Id,
    string ChildId,
    string Type,
    DateTime OccurredAt,
    string CorrelationId,
    string SchemaVersion,
    string DedupeKey
);

public record Job(
    string Id,
    string ChildId,
    string Status,
    int Attempts,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? Error
);

public record DlqEntry(
    string Id,
    string ChildId,
    DateTime OccurredAt,
    string Reason,
    string PayloadRef
);
