using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DunedinTrails.Functions;

internal static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonResult Ok(object? value, int statusCode = StatusCodes.Status200OK) =>
        new(value, Options) { StatusCode = statusCode };

    public static JsonResult Error(int statusCode, string message) =>
        new(new { error = message }, Options) { StatusCode = statusCode };
}
