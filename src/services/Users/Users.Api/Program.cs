using BuildingBlocks.Api;
using System.Text.Json.Serialization;
using BuildingBlocks.Auth;
using BuildingBlocks.Observability;
using Users.Api.Endpoints;
using Users.Application;
using Users.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddMarketplaceObservability("users");

// Composition root: o único lugar que conhece Application e Infrastructure ao mesmo tempo (ADR-004).
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddMarketplaceJwtAuth(builder.Configuration);
builder.Services.AddUsersApplication();
builder.Services.AddUsersInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateUsersDatabaseAsync();
}

app.UseMarketplaceExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "users", status = "healthy" }));
app.MapUsersEndpoints();
app.MapAuthEndpoints();

app.Run();

// Exposto para WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
