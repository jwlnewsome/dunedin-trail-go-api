using System.Globalization;
using System.Text.Json.Nodes;

namespace DunedinTrails.Functions.Data;

/// <summary>
/// In-memory replacement for the Base44 entity store. Records are schemaless JSON objects keyed by
/// entity name and id, and carry the Base44 system fields (id, created_date, updated_date, created_by).
/// Data is seeded on start-up and lost when the host restarts.
/// </summary>
public sealed class EntityStore : IEntityStore
{
    public static readonly IReadOnlySet<string> EntityNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Contribution", "DccAlert", "DocAlert", "Event", "Group", "LocationHub", "Playground",
        "Trail", "TrailCompletion", "TrailReview", "TrailWishlist", "User", "UserRoute",
    };

    private static readonly HashSet<string> SystemFields = ["id", "created_date", "updated_date", "created_by"];

    private readonly Dictionary<string, Dictionary<string, JsonObject>> _tables = [];
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;

    public EntityStore(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        foreach (var name in EntityNames) _tables[name] = [];
        foreach (var (entity, record) in SeedData.Create(_time.GetUtcNow()))
        {
            _tables[entity][record["id"]!.GetValue<string>()] = record;
        }
    }

    public static bool IsKnownEntity(string entity) => EntityNames.Contains(entity);

    public IReadOnlyList<JsonObject> List(string entity, JsonObject? filter = null, string? sort = null, int? limit = null, int skip = 0)
    {
        lock (_gate)
        {
            return ApplyList(Table(entity).Values, filter, sort, limit, skip);
        }
    }

    internal static IReadOnlyList<JsonObject> ApplyList(IEnumerable<JsonObject> source, JsonObject? filter, string? sort, int? limit, int skip)
    {
        IEnumerable<JsonObject> rows = source.Where(r => Matches(r, filter));
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var descending = sort.StartsWith('-');
            var field = sort.TrimStart('-', '+');
            var comparer = Comparer<JsonNode?>.Create(CompareValues);
            rows = descending ? rows.OrderByDescending(r => r[field], comparer) : rows.OrderBy(r => r[field], comparer);
        }
        rows = rows.Skip(Math.Max(0, skip));
        if (limit is > 0) rows = rows.Take(limit.Value);
        return rows.Select(Clone).ToList();
    }

    public Task<IReadOnlyList<JsonObject>> ListAsync(string entity, JsonObject? filter = null, string? sort = null,
        int? limit = null, int skip = 0, CancellationToken cancellationToken = default) =>
        Task.FromResult(List(entity, filter, sort, limit, skip));

    public Task<JsonObject?> GetAsync(string entity, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Get(entity, id));

    public Task<JsonObject> CreateAsync(string entity, JsonObject data, string? createdBy, CancellationToken cancellationToken = default) =>
        Task.FromResult(Create(entity, data, createdBy));

    public Task<JsonObject?> UpdateAsync(string entity, string id, JsonObject patch, CancellationToken cancellationToken = default) =>
        Task.FromResult(Update(entity, id, patch));

    public Task<bool> DeleteAsync(string entity, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Delete(entity, id));
    public JsonObject? Get(string entity, string id)
    {
        lock (_gate) return Table(entity).TryGetValue(id, out var row) ? Clone(row) : null;
    }

    public JsonObject Create(string entity, JsonObject data, string? createdBy)
    {
        lock (_gate)
        {
            var now = Timestamp();
            var record = Clone(data);
            foreach (var field in SystemFields) record.Remove(field);
            record["id"] = Guid.NewGuid().ToString("N");
            record["created_date"] = now;
            record["updated_date"] = now;
            if (createdBy is not null) record["created_by"] = createdBy;

            Table(entity)[record["id"]!.GetValue<string>()] = record;
            return Clone(record);
        }
    }

    public JsonObject? Update(string entity, string id, JsonObject patch)
    {
        lock (_gate)
        {
            if (!Table(entity).TryGetValue(id, out var record)) return null;
            foreach (var (key, value) in patch)
            {
                if (SystemFields.Contains(key)) continue;
                record[key] = value?.DeepClone();
            }
            record["updated_date"] = Timestamp();
            return Clone(record);
        }
    }

    public bool Delete(string entity, string id)
    {
        lock (_gate) return Table(entity).Remove(id);
    }

    private Dictionary<string, JsonObject> Table(string entity) =>
        _tables.TryGetValue(entity, out var table)
            ? table
            : throw new ArgumentException($"Unknown entity '{entity}'.", nameof(entity));

    private string Timestamp() => _time.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static JsonObject Clone(JsonObject source) => (JsonObject)source.DeepClone();

    // Supports the subset of Base44 (Mongo-style) query syntax the app uses: equality, $in and $ne.
    // Equality against an array field matches when the array contains the value.
    private static bool Matches(JsonObject record, JsonObject? filter)
    {
        if (filter is null) return true;
        foreach (var (field, condition) in filter)
        {
            var value = record[field];
            if (condition is JsonObject ops && ops.Any(o => o.Key.StartsWith('$')))
            {
                foreach (var (op, operand) in ops)
                {
                    var ok = op switch
                    {
                        "$in" => operand is JsonArray options && options.Any(o => ValueMatches(value, o)),
                        "$nin" => operand is not JsonArray excluded || !excluded.Any(o => ValueMatches(value, o)),
                        "$ne" => !ValueMatches(value, operand),
                        _ => throw new ArgumentException($"Unsupported query operator '{op}'."),
                    };
                    if (!ok) return false;
                }
            }
            else if (!ValueMatches(value, condition))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValueMatches(JsonNode? value, JsonNode? expected) =>
        JsonNode.DeepEquals(value, expected) ||
        (value is JsonArray items && expected is not JsonArray && items.Any(i => JsonNode.DeepEquals(i, expected)));

    // Missing values sort last (ascending); numbers numerically; ISO dates chronologically.
    private static int CompareValues(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null ? (b is null ? 0 : 1) : -1;
        if (a is JsonValue av && b is JsonValue bv)
        {
            if (av.TryGetValue(out double ad) && bv.TryGetValue(out double bd)) return ad.CompareTo(bd);
            if (av.TryGetValue(out bool ab) && bv.TryGetValue(out bool bb)) return ab.CompareTo(bb);
            if (av.TryGetValue(out string? sa) && bv.TryGetValue(out string? sb))
            {
                if (TryParseDate(sa, out var da) && TryParseDate(sb, out var db)) return da.CompareTo(db);
                return string.Compare(sa, sb, StringComparison.InvariantCultureIgnoreCase);
            }
        }
        return string.CompareOrdinal(a.ToJsonString(), b.ToJsonString());
    }

    private static bool TryParseDate(string value, out DateTimeOffset date)
    {
        date = default;
        return value.Length >= 10 && char.IsAsciiDigit(value[0]) && value[4] == '-' && value[7] == '-' &&
               DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }
}
