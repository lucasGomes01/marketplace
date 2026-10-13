using BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.UnitTests;

public sealed record Ping(string Message);

public sealed class PingHandler : IHandler<Ping, string>
{
    public Task<Result<string>> HandleAsync(Ping request, CancellationToken cancellationToken = default) =>
        Task.FromResult<Result<string>>($"pong: {request.Message}");
}

public abstract class AbstractHandler : IHandler<Ping, int>
{
    public abstract Task<Result<int>> HandleAsync(Ping request, CancellationToken cancellationToken = default);
}

public class HandlerRegistrationTests
{
    [Fact]
    public async Task Registers_every_concrete_handler_of_the_assembly()
    {
        var services = new ServiceCollection().AddHandlersFrom(typeof(PingHandler).Assembly).BuildServiceProvider();
        using var scope = services.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<IHandler<Ping, string>>();
        var result = await handler.HandleAsync(new Ping("hi"));

        Assert.Equal("pong: hi", result.Value);
    }

    [Fact]
    public void Skips_abstract_handlers()
    {
        var services = new ServiceCollection().AddHandlersFrom(typeof(PingHandler).Assembly).BuildServiceProvider();

        Assert.Null(services.CreateScope().ServiceProvider.GetService<IHandler<Ping, int>>());
    }
}
