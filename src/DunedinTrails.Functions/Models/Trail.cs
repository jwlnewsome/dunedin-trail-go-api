namespace DunedinTrails.Functions.Models;

/// <summary>
/// Mirrors the Base44 Trail entity (base44/entities/Trail.jsonc) plus the Base44 system fields.
/// Serialised as snake_case so the existing frontend can consume it unchanged.
/// </summary>
public sealed record Trail
{
    public required string Id { get; init; }
    public required DateTimeOffset CreatedDate { get; init; }
    public required DateTimeOffset UpdatedDate { get; init; }
    public string? CreatedBy { get; init; }

    public required string Name { get; init; }
    public string? Slug { get; init; }
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = "draft";
    public string TrailStatus { get; init; } = "open";
    public string RouteType { get; init; } = "out_and_back";
    public required string Difficulty { get; init; }
    public IReadOnlyList<string> ActivityTypes { get; init; } = [];

    public double? DistanceKm { get; init; }
    public int? DurationMinutes { get; init; }
    public int? DurationMin { get; init; }
    public int? DurationMax { get; init; }
    public double? ElevationGainM { get; init; }

    public bool? WheelchairFriendly { get; init; }
    public bool? DogFriendly { get; init; }
    public bool? BusAccessible { get; init; }
    public bool? FamilyFriendly { get; init; }
    public bool? Urban { get; init; }

    public string? StartLocation { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public string? Region { get; init; }
    public string? Area { get; init; }
    public IReadOnlyList<double[]>? RouteCoordinates { get; init; }
    public IReadOnlyList<string>? HubIds { get; init; }

    public string? ImageUrl { get; init; }
    public IReadOnlyList<string>? Features { get; init; }
    public string? GettingThere { get; init; }
    public string? ParkingInfo { get; init; }
}
