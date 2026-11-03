using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Application.CancelSales;
using Payments.Application.RecordSale;
using Payments.Application.ReleasePayout;
using Payments.Domain;
using Payments.Infrastructure.Mastercard;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Security;
using Payments.Infrastructure.Simulated;
using Payments.Infrastructure.Workers;

namespace Payments.Infrastructure;

public static class DependencyInjection
{
    public const string ConsumerGroup = "payments-service";

    /// <summary>
    /// Configuração:
    /// <list type="bullet">
    /// <item><c>ConnectionStrings:Default</c>, <c>Kafka:BootstrapServers</c>;</item>
    /// <item><c>Payments:CardEncryptionKey</c> (base64 de 32 bytes) — cifra o número do cartão;</item>
    /// <item><c>Payments:Provider</c>: <c>Simulated</c> (padrão) ou <c>Mastercard</c> (sandbox; seção <c>Mastercard</c>);</item>
    /// <item><c>Payouts:*</c>: taxa, carência, intervalo do despachante (ver <see cref="PayoutSettings"/>).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddPaymentsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentsDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default")));
        services.AddScoped<PaymentsRepository>();
        services.AddScoped<IPayoutRepository>(sp => sp.GetRequiredService<PaymentsRepository>());
        services.AddScoped<IPayoutCardRepository>(sp => sp.GetRequiredService<PaymentsRepository>());
        services.TryAddSingleton(TimeProvider.System);

        var key = configuration["Payments:CardEncryptionKey"]
            ?? throw new InvalidOperationException("Missing configuration 'Payments:CardEncryptionKey' (base64 of 32 bytes).");
        services.AddSingleton<ICardProtector>(new AesGcmCardProtector(key));

        var settings = configuration.GetSection(PayoutSettings.SectionName).Get<PayoutSettings>() ?? new PayoutSettings();
        services.AddSingleton(settings.ToPolicy());

        AddProviders(services, configuration);
        AddMessaging(services, configuration);

        if (settings.DispatcherEnabled)
        {
            services.AddHostedService(sp => new PayoutDispatcherWorker(
                sp.GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromSeconds(settings.DispatchIntervalSeconds),
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<PayoutDispatcherWorker>>()));
        }
        return services;
    }

    public static async Task MigratePaymentsDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
    }

    private static void AddProviders(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Payments:Provider"] ?? "Simulated";
        var cacheDuration = TimeSpan.FromMinutes(configuration.GetValue("Payments:ExchangeRateCacheMinutes", 60));
        services.AddMemoryCache();

        if (provider.Equals("Mastercard", StringComparison.OrdinalIgnoreCase))
        {
            var mastercard = configuration.GetSection(MastercardOptions.SectionName);
            services.AddOptions<MastercardOptions>().Bind(mastercard);
            var options = mastercard.Get<MastercardOptions>() ?? new MastercardOptions();
            services.AddSingleton(sp => MastercardOAuthSigner.FromOptions(options, sp.GetRequiredService<TimeProvider>()));
            services.AddTransient<MastercardOAuthHandler>();

            void Configure(HttpClient http)
            {
                http.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
                http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            }
            services.AddHttpClient<IPayoutGateway, MastercardSendPayoutGateway>(Configure).AddHttpMessageHandler<MastercardOAuthHandler>();
            services.AddHttpClient<MastercardExchangeRateProvider>(Configure).AddHttpMessageHandler<MastercardOAuthHandler>();
            services.AddSingleton<IExchangeRateProvider>(sp => new CachedExchangeRateProvider(
                sp.GetRequiredService<MastercardExchangeRateProvider>(), sp.GetRequiredService<IMemoryCache>(), cacheDuration));
            return;
        }
        if (!provider.Equals("Simulated", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown Payments:Provider '{provider}'. Use Simulated or Mastercard.");
        }

        services.AddSingleton<IPayoutGateway, SimulatedPayoutGateway>();
        services.AddSingleton<IExchangeRateProvider>(sp => new CachedExchangeRateProvider(
            new SimulatedExchangeRateProvider(sp.GetRequiredService<TimeProvider>()), sp.GetRequiredService<IMemoryCache>(), cacheDuration));
    }

    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaOptions>()
            .Bind(configuration.GetSection(KafkaOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BootstrapServers), "Kafka:BootstrapServers is required.")
            .ValidateOnStart();

        services.AddKafkaEventConsumer<CartCheckedOut, RecordSaleHandler>(CartCheckedOut.EventType, ConsumerGroup);
        services.AddKafkaEventConsumer<StockRejected, CancelSalesHandler>(StockRejected.EventType, ConsumerGroup);
        services.AddKafkaEventConsumer<ShipmentStatusChanged, ReleasePayoutHandler>(ShipmentStatusChanged.EventType, ConsumerGroup);
    }
}

/// <summary>Seção <c>Payouts</c> da configuração.</summary>
public sealed class PayoutSettings
{
    public const string SectionName = "Payouts";

    /// <summary>Comissão da plataforma (0.10 = 10%).</summary>
    public decimal FeeRate { get; set; } = 0.10m;

    /// <summary>Dias entre a entrega e o pagamento (janela de devolução). 0 = paga logo após o "Recebi".</summary>
    public double HoldDays { get; set; }

    public int RetryDelaySeconds { get; set; } = 60;

    public int DispatchIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 20;

    /// <summary>Desligado nos testes que controlam o despachante na mão.</summary>
    public bool DispatcherEnabled { get; set; } = true;

    public PayoutPolicy ToPolicy() => new(
        PlatformFee.Of(FeeRate), TimeSpan.FromDays(HoldDays), TimeSpan.FromSeconds(RetryDelaySeconds), BatchSize);
}
