using BuildingBlocks.Api;
using BuildingBlocks.Auth;
using BuildingBlocks.Observability;
using Payments.Api.Endpoints;
using Payments.Application;
using Payments.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddMarketplaceObservability("payments");

// Composition root (ADR-004).
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddMarketplaceJwtAuth(builder.Configuration);
builder.Services.AddPaymentsApplication();
builder.Services.AddPaymentsInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigratePaymentsDatabaseAsync();
}

app.UseMarketplaceExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "payments", status = "healthy" }));
app.MapPayoutsEndpoints();
app.MapExchangeRatesEndpoints();

app.Run();

// Exposto para WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
