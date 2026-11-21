using DunedinTrails.Functions.Data;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var endpoint = configuration["CosmosDb:Endpoint"]
        ?? throw new InvalidOperationException("CosmosDb:Endpoint is required.");
    return new CosmosClient(endpoint, new DefaultAzureCredential());
});
builder.Services.AddSingleton<IEntityStore>(sp =>
{
    var database = sp.GetRequiredService<IConfiguration>()["CosmosDb:Database"]
        ?? throw new InvalidOperationException("CosmosDb:Database is required.");
    return new CosmosEntityStore(sp.GetRequiredService<CosmosClient>(), database);
});

builder.Build().Run();
