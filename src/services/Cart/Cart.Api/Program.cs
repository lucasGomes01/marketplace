using BuildingBlocks.Api;
using BuildingBlocks.Auth;
using Cart.Api.Endpoints;
using Cart.Application;
using Cart.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Composition root (ADR-004).
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddMarketplaceJwtAuth(builder.Configuration);
builder.Services.AddCartApplication();
builder.Services.AddCartInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateCartDatabaseAsync();
}

app.UseMarketplaceExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "cart", status = "healthy" }));
app.MapCartEndpoints();

app.Run();

// Exposto para WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
