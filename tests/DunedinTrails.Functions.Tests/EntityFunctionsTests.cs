using System.Text;
using System.Text.Json;
using DunedinTrails.Functions.Data;
using DunedinTrails.Functions.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DunedinTrails.Functions.Tests;

public class EntityFunctionsTests
{
    private readonly EntityStore _store = new();
    private EntityFunctions Functions => new(_store);

    private static HttpRequest Request(string queryString = "", string? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        if (body is not null) context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }

    private static (int Status, JsonElement Body) Read(IActionResult actionResult)
    {
        var result = Assert.IsType<JsonResult>(actionResult);
        var json = JsonSerializer.Serialize(result.Value, (JsonSerializerOptions)result.SerializerSettings!);
        return (result.StatusCode ?? 200, JsonDocument.Parse(json).RootElement.Clone());
    }

    [Fact]
    public async Task List_FiltersWithEqualityAndIn_AndSorts()
    {
        var q = Uri.EscapeDataString("""{"status":{"$in":["active","draft"]},"activity_types":"Walking"}""");
        var (status, body) = Read(await Functions.List(Request($"?q={q}&sort=-distance_km"), "Trail"));

        Assert.Equal(200, status);
        var rows = body.EnumerateArray().ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Contains("Walking", r.GetProperty("activity_types").EnumerateArray().Select(a => a.GetString())));
        var distances = rows.Select(r => r.GetProperty("distance_km").GetDouble()).ToList();
        Assert.Equal(distances.OrderByDescending(d => d), distances);
    }

    [Fact]
    public async Task Create_Update_Delete_RoundTrip()
    {
        var (createStatus, created) = Read(await Functions.Create(
            Request(body: """{"trail_id":"trail-ross-creek","rating":5,"id":"spoofed"}"""), "TrailReview", default));
        Assert.Equal(201, createStatus);
        var id = created.GetProperty("id").GetString()!;
        Assert.NotEqual("spoofed", id);
        Assert.Equal(SeedData.AdminEmail, created.GetProperty("created_by").GetString());

        var (_, updated) = Read(await Functions.Update(Request(body: """{"rating":3}"""), "TrailReview", id, default));
        Assert.Equal(3, updated.GetProperty("rating").GetInt32());
        Assert.Equal("trail-ross-creek", updated.GetProperty("trail_id").GetString());

        Assert.Equal(200, Read(await Functions.Delete(Request(), "TrailReview", id)).Status);
        Assert.Equal(404, Read(await Functions.Get(Request(), "TrailReview", id)).Status);
    }

    [Fact]
    public async Task NewTrails_AppearInPublicTrailList()
    {
        _store.Create("Trail", new() { ["name"] = "Brand New Track", ["difficulty"] = "easy" }, SeedData.AdminEmail);

        var (_, body) = Read(await new GetTrailsForMap(_store).Run(Request()));
        Assert.Equal("Brand New Track", body[0].GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("Password", "")]
    [InlineData("Trail", "?q=not-json")]
    [InlineData("Trail", "?limit=0")]
    public async Task List_RejectsBadInput(string entity, string query)
    {
        var (status, body) = Read(await Functions.List(Request(query), entity));
        Assert.InRange(status, 400, 404);
        Assert.True(body.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task UpdateMe_PersistsPrefs_ButNotRole()
    {
        var auth = new AuthFunctions(_store);
        await auth.UpdateMe(Request(body: """{"map_prefs":{"style":"topo"},"role":"user"}"""), default);

        var (_, me) = Read(await auth.Me(Request()));
        Assert.Equal("admin", me.GetProperty("role").GetString());
        Assert.Equal("topo", me.GetProperty("map_prefs").GetProperty("style").GetString());
    }
}
