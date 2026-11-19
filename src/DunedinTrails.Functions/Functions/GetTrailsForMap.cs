using DunedinTrails.Functions.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DunedinTrails.Functions.Functions;

/// <summary>
/// Trail list for the map; replaces <c>base44.entities.Trail.list('-created_date', 1000)</c>.
/// </summary>
public sealed class GetTrailsForMap(EntityStore store)
{
    public const string DefaultSort = "-created_date";
    public const int DefaultLimit = 1000;
    public const int MaxLimit = 1000;

    public static readonly IReadOnlySet<string> SortableFields =
        new HashSet<string>(StringComparer.Ordinal) { "created_date", "updated_date", "name", "distance_km" };

    [Function(nameof(GetTrailsForMap))]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "trails")] HttpRequest req)
    {
        var sort = req.Query["sort"].FirstOrDefault() is { Length: > 0 } s ? s : DefaultSort;
        var limit = DefaultLimit;

        if (req.Query["limit"].FirstOrDefault() is { Length: > 0 } rawLimit &&
            (!int.TryParse(rawLimit, out limit) || limit < 1 || limit > MaxLimit))
        {
            return ApiJson.Error(StatusCodes.Status400BadRequest, $"limit must be an integer between 1 and {MaxLimit}.");
        }

        if (!SortableFields.Contains(sort.TrimStart('-')))
        {
            return ApiJson.Error(StatusCodes.Status400BadRequest,
                $"sort must be one of: {string.Join(", ", SortableFields)} (prefix with '-' for descending).");
        }

        return ApiJson.Ok(store.List("Trail", sort: sort, limit: limit));
    }
}
