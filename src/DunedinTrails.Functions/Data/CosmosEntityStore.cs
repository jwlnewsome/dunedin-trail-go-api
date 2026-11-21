using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Azure.Cosmos;

namespace DunedinTrails.Functions.Data;

public sealed class CosmosEntityStore : IEntityStore
{
    private static readonly HashSet<string> PersonalEntities = ["User", "TrailCompletion", "TrailWishlist", "UserRoute"];
    private static readonly HashSet<string> ProtectedFields =
        ["id", "created_date", "updated_date", "created_by", "entity_type", "user_id", "_rid", "_self", "_etag", "_attachments", "_ts"];
    private static readonly string[] CosmosFields = ["_rid", "_self", "_etag", "_attachments", "_ts"];

    private readonly Container _catalog;
    private readonly Container _users;
    private readonly TimeProvider _time;

    public CosmosEntityStore(CosmosClient client, string database, TimeProvider? time = null)
    {
        _catalog = client.GetContainer(database, "catalog");
        _users = client.GetContainer(database, "user_data");
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<JsonObject>> ListAsync(string entity, JsonObject? filter = null, string? sort = null,
        int? limit = null, int skip = 0, CancellationToken cancellationToken = default)
    {
        var (container, partition) = Target(entity, null);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.entity_type = @entity")
            .WithParameter("@entity", entity);
        var options = new QueryRequestOptions();
        if (partition is not null) options.PartitionKey = new PartitionKey(partition);
        using var iterator = container.GetItemQueryStreamIterator(query, requestOptions: options);
        var records = new List<JsonObject>();
        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await JsonNode.ParseAsync(response.Content, cancellationToken: cancellationToken);
            var documents = result?["Documents"] as JsonArray
                ?? throw new InvalidOperationException("Cosmos DB returned a query response without documents.");
            records.AddRange(documents.Select(node => Clean(node!.AsObject())));
        }
        return EntityStore.ApplyList(records, filter, sort, limit, skip);
    }

    public async Task<JsonObject?> GetAsync(string entity, string id, CancellationToken cancellationToken = default)
    {
        var result = await ReadAsync(entity, id, cancellationToken);
        return result is null ? null : Clean(result.Value.Record);
    }

    public async Task<JsonObject> CreateAsync(string entity, JsonObject data, string? createdBy, CancellationToken cancellationToken = default)
    {
        var record = data.DeepClone().AsObject();
        foreach (var field in ProtectedFields) record.Remove(field);
        var id = Guid.NewGuid().ToString("N");
        var now = Timestamp();
        record["id"] = id;
        record["created_date"] = now;
        record["updated_date"] = now;
        if (createdBy is not null) record["created_by"] = createdBy;
        record["entity_type"] = entity;
        var (container, partition) = Target(entity, id);
        if (PersonalEntities.Contains(entity)) record["user_id"] = partition;
        using var content = Serialize(record);
        using var response = await container.CreateItemStreamAsync(content, new PartitionKey(partition), cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
        return Clean(record);
    }

    public async Task<JsonObject?> UpdateAsync(string entity, string id, JsonObject patch, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var current = await ReadAsync(entity, id, cancellationToken);
            if (current is null) return null;
            var record = current.Value.Record;
            foreach (var (key, value) in patch)
            {
                if (!ProtectedFields.Contains(key)) record[key] = value?.DeepClone();
            }
            record["updated_date"] = Timestamp();
            foreach (var field in CosmosFields) record.Remove(field);
            var (container, partition) = Target(entity, id);
            using var content = Serialize(record);
            using var response = await container.ReplaceItemStreamAsync(content, id, new PartitionKey(partition),
                new ItemRequestOptions { IfMatchEtag = current.Value.ETag }, cancellationToken);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed && attempt < 2) continue;
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return Clean(record);
        }
        throw new InvalidOperationException("Unable to update the document due to concurrent modifications.");
    }

    public async Task<bool> DeleteAsync(string entity, string id, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(entity, id, cancellationToken);
        if (current is null) return false;
        var (container, partition) = Target(entity, id);
        using var response = await container.DeleteItemStreamAsync(id, new PartitionKey(partition),
            new ItemRequestOptions { IfMatchEtag = current.Value.ETag }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<(JsonObject Record, string ETag)?> ReadAsync(string entity, string id, CancellationToken cancellationToken)
    {
        var (container, partition) = Target(entity, id);
        using var response = await container.ReadItemStreamAsync(id, new PartitionKey(partition), cancellationToken: cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var record = (await JsonNode.ParseAsync(response.Content, cancellationToken: cancellationToken))?.AsObject()
            ?? throw new InvalidOperationException("Cosmos DB returned an empty document.");
        if (record["entity_type"]?.GetValue<string>() != entity) return null;
        var etag = response.Headers.ETag
            ?? throw new InvalidOperationException("Cosmos DB returned a document without an ETag.");
        return (record, etag);
    }

    private (Container Container, string? Partition) Target(string entity, string? id)
    {
        if (!EntityStore.IsKnownEntity(entity)) throw new ArgumentException($"Unknown entity '{entity}'.", nameof(entity));
        if (!PersonalEntities.Contains(entity)) return (_catalog, entity);
        return (_users, entity == "User" ? id : SeedData.AdminUserId);
    }

    private static MemoryStream Serialize(JsonObject record) => new(Encoding.UTF8.GetBytes(record.ToJsonString()));

    private static JsonObject Clean(JsonObject record)
    {
        var result = record.DeepClone().AsObject();
        result.Remove("entity_type");
        foreach (var field in CosmosFields) result.Remove(field);
        return result;
    }

    private string Timestamp() => _time.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
