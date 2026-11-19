using System.Text.Json;
using DunedinTrails.Functions.Data;
using DunedinTrails.Functions.Functions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DunedinTrails.Functions.Tests;

public class GetTrailsForMapTests
{
    private static async Task<(int StatusCode, JsonElement Body)> InvokeAsync(string queryString = "")
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        var function = new GetTrailsForMap(new EntityStore());

        var result = Assert.IsType<JsonResult>(function.Run(context.Request));
        var json = JsonSerializer.Serialize(result.Value, (JsonSerializerOptions)result.SerializerSettings!);
        return (result.StatusCode ?? StatusCodes.Status200OK, JsonDocument.Parse(json).RootElement);
    }

    [Fact]
    public async Task Default_MatchesBase44ListNewestFirst_InSnakeCase()
    {
        var (status, body) = await InvokeAsync();

        Assert.Equal(200, status);
        var trails = body.EnumerateArray().ToList();
        Assert.Equal(7, trails.Count);

        var created = trails.Select(t => t.GetProperty("created_date").GetDateTimeOffset()).ToList();
        Assert.Equal(created.OrderByDescending(d => d), created);
        Assert.Contains(trails, t => t.TryGetProperty("route_coordinates", out _));
        Assert.All(trails, t => Assert.True(t.TryGetProperty("activity_types", out _)));
    }

    [Fact]
    public async Task SortAndLimit_AreApplied()
    {
        var (status, body) = await InvokeAsync("?sort=name&limit=2");

        Assert.Equal(200, status);
        var names = body.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Mt Cargill Downhill", "Pineapple Track (suggested)"], names);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=1001")]
    [InlineData("?limit=abc")]
    [InlineData("?sort=-password")]
    public async Task InvalidQuery_ReturnsBadRequest(string query)
    {
        var (status, body) = await InvokeAsync(query);

        Assert.Equal(400, status);
        Assert.True(body.TryGetProperty("error", out _));
    }
}
