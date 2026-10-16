var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));

// Leituras públicas do catálogo (vitrine e produto) ficam alguns segundos no gateway: a mesma requisição não vai
// ao Catalog de novo. A política padrão já não guarda escrita, requisição com login nem resposta que não seja 200.
// O estoque mostrado pode ficar até esse tempo atrasado; a compra sempre confere o estoque real no Catalog.
var catalogCacheSeconds = builder.Configuration.GetValue("OutputCache:CatalogSeconds", 5);
builder.Services.AddOutputCache(options =>
    options.AddPolicy("catalog-public", policy => policy
        .Expire(TimeSpan.FromSeconds(catalogCacheSeconds))
        .SetVaryByQuery("*")));

var app = builder.Build();

app.UseCors();
app.UseOutputCache();
app.MapGet("/health", () => Results.Ok(new { service = "gateway", status = "healthy" }));
app.MapReverseProxy();

app.Run();

// Exposto para WebApplicationFactory<Program> nos testes de integração.
public partial class Program;
