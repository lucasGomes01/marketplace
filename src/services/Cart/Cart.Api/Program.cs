var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { service = "cart", status = "healthy" }));

app.Run();

// Exposto para WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
