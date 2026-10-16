using BuildingBlocks.Api;
using BuildingBlocks.Auth;
using BuildingBlocks.Observability;
using Catalog.Api.Endpoints;
using Catalog.Application;
using Catalog.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddMarketplaceObservability("catalog");

// Composition root (ADR-004).
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddMarketplaceJwtAuth(builder.Configuration);
builder.Services.AddCatalogApplication();
builder.Services.AddCatalogInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateCatalogDatabaseAsync();
}

app.UseMarketplaceExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "catalog", status = "healthy" }));
app.MapProductsEndpoints();

app.Run();

// Exposto para WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
