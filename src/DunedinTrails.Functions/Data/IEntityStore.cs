using System.Text.Json.Nodes;

namespace DunedinTrails.Functions.Data;

public interface IEntityStore
{
    Task<IReadOnlyList<JsonObject>> ListAsync(string entity, JsonObject? filter = null, string? sort = null,
        int? limit = null, int skip = 0, CancellationToken cancellationToken = default);
    Task<JsonObject?> GetAsync(string entity, string id, CancellationToken cancellationToken = default);
    Task<JsonObject> CreateAsync(string entity, JsonObject data, string? createdBy, CancellationToken cancellationToken = default);
    Task<JsonObject?> UpdateAsync(string entity, string id, JsonObject patch, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string entity, string id, CancellationToken cancellationToken = default);
}
