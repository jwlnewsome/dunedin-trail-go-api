using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DunedinTrails.Functions.Data;
using Microsoft.Azure.Cosmos;
using Moq;

namespace DunedinTrails.Functions.Tests;

public class CosmosEntityStoreTests
{
    private readonly Mock<CosmosClient> _client = new();
    private readonly Mock<Container> _catalog = new();
    private readonly Mock<Container> _users = new();

    public CosmosEntityStoreTests()
    {
        _client.Setup(c => c.GetContainer("db", "catalog")).Returns(_catalog.Object);
        _client.Setup(c => c.GetContainer("db", "user_data")).Returns(_users.Object);
    }

    private CosmosEntityStore Store => new(_client.Object, "db");

    private static ResponseMessage Response(HttpStatusCode status, string? json = null, string? etag = null)
    {
        var response = new ResponseMessage(status);
        if (json is not null) response.Content = new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (etag is not null) response.Headers.Add("ETag", etag);
        return response;
    }

    [Fact]
    public async Task List_ReadsAllPagesAndAppliesExistingFilteringAndSorting()
    {
        var iterator = new Mock<FeedIterator>();
        iterator.SetupSequence(i => i.HasMoreResults).Returns(true).Returns(true).Returns(false);
        iterator.SetupSequence(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(HttpStatusCode.OK, """{"Documents":[{"id":"a","entity_type":"Trail","name":"Beta","tags":["walking"],"_rid":"hidden"}]}"""))
            .ReturnsAsync(Response(HttpStatusCode.OK, """{"Documents":[{"id":"b","entity_type":"Trail","name":"Alpha","tags":["walking"]}]}"""));
        _catalog.Setup(c => c.GetItemQueryStreamIterator(It.IsAny<QueryDefinition>(), null,
                It.Is<QueryRequestOptions>(o => o.PartitionKey == new PartitionKey("Trail"))))
            .Returns(iterator.Object);

        var rows = await Store.ListAsync("Trail", new() { ["tags"] = "walking" }, "name", limit: 1);

        Assert.Single(rows);
        Assert.Equal("b", rows[0]["id"]!.GetValue<string>());
        Assert.False(rows[0].ContainsKey("entity_type"));
        _catalog.VerifyAll();
    }

    [Fact]
    public async Task Create_PersonalRecordUsesMockAdminPartitionAndProtectsMetadata()
    {
        JsonObject? saved = null;
        _users.Setup(c => c.CreateItemStreamAsync(It.IsAny<Stream>(), new PartitionKey("user-admin"),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, PartitionKey, ItemRequestOptions, CancellationToken>((stream, _, _, _) =>
                saved = JsonNode.Parse(stream)!.AsObject())
            .ReturnsAsync(() => Response(HttpStatusCode.Created));

        var result = await Store.CreateAsync("TrailWishlist", JsonNode.Parse(
            """{"id":"spoof","user_id":"other","entity_type":"Trail","trail_id":"trail-one"}""")!.AsObject(), "admin@example.com");

        Assert.NotEqual("spoof", result["id"]!.GetValue<string>());
        Assert.Equal("TrailWishlist", saved!["entity_type"]!.GetValue<string>());
        Assert.Equal("user-admin", saved["user_id"]!.GetValue<string>());
        Assert.False(result.ContainsKey("entity_type"));
        _users.VerifyAll();
    }

    [Fact]
    public async Task Update_RetriesConflictsWithoutLosingConcurrentChanges()
    {
        _catalog.SetupSequence(c => c.ReadItemStreamAsync("trail-one", new PartitionKey("Trail"),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(HttpStatusCode.OK, """{"id":"trail-one","entity_type":"Trail","name":"Old","other":1}""", "v1"))
            .ReturnsAsync(Response(HttpStatusCode.OK, """{"id":"trail-one","entity_type":"Trail","name":"Old","other":2,"_rid":"internal"}""", "v2"));
        var written = new List<(string? ETag, JsonObject Record)>();
        _catalog.Setup(c => c.ReplaceItemStreamAsync(It.IsAny<Stream>(), "trail-one", new PartitionKey("Trail"),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, PartitionKey, ItemRequestOptions, CancellationToken>((stream, _, _, options, _) =>
                written.Add((options.IfMatchEtag, JsonNode.Parse(stream)!.AsObject())))
            .ReturnsAsync(() => Response(written.Count == 1 ? HttpStatusCode.PreconditionFailed : HttpStatusCode.OK));

        var result = await Store.UpdateAsync("Trail", "trail-one",
            JsonNode.Parse("""{"name":"New","entity_type":"User","id":"other"}""")!.AsObject());

        Assert.Equal(["v1", "v2"], written.Select(w => w.ETag));
        Assert.Equal(2, result!["other"]!.GetValue<int>());
        Assert.Equal("New", result["name"]!.GetValue<string>());
        Assert.Equal("Trail", written[1].Record["entity_type"]!.GetValue<string>());
        Assert.Equal("trail-one", written[1].Record["id"]!.GetValue<string>());
        Assert.False(written[1].Record.ContainsKey("_rid"));
    }

    [Fact]
    public async Task Get_DoesNotReturnAnotherEntityFromSharedUserPartition()
    {
        _users.Setup(c => c.ReadItemStreamAsync("entry", new PartitionKey("user-admin"),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(HttpStatusCode.OK, """{"id":"entry","entity_type":"UserRoute","user_id":"user-admin"}""", "v1"));

        Assert.Null(await Store.GetAsync("TrailWishlist", "entry"));
    }

    [Fact]
    public async Task Get_UsesUserIdAsPartitionKeyAndSurfacesAccessDenial()
    {
        _users.Setup(c => c.ReadItemStreamAsync("user-admin", new PartitionKey("user-admin"),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(HttpStatusCode.Forbidden));

        await Assert.ThrowsAsync<CosmosException>(() => Store.GetAsync("User", "user-admin"));
    }
}
