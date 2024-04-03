using System.Text;
using DotPulsar;
using DotPulsar.Abstractions;
using DotPulsar.Extensions;
using DotPulsar.Internal;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new PulsarClientBuilder()
    .ServiceUrl(builder.Configuration.GetRequiredSection("Pulsar:ServiceUrl").Get<Uri>()!)
    .RetryInterval(builder.Configuration.GetRequiredSection("Pulsar:RetryInterval").Get<TimeSpan>())
    .ExceptionHandler(new ConsoleExceptionHandler())
    .Build());
builder.Services.AddHostedService<PulsarConsumer>();

var host = builder.Build();

await host.RunAsync();

internal sealed class PulsarConsumer(
    IPulsarClient client,
    IConfiguration configuration,
    IHostApplicationLifetime applicationLifetime,
    ILogger<PulsarConsumer> logger)
    : IHostedService, IAsyncDisposable
{
    private readonly ConsumerOptions<byte[]> _options = new(
        subscriptionName: configuration["Pulsar:SubscriptionName"]!,
        topic: configuration["Pulsar:Topic"]!,
        schema: Schema.ByteArray)
    {
        ConsumerName = configuration["Pulsar:ConsumerName"]!,
        InitialPosition = SubscriptionInitialPosition.Earliest,
    };

    public ValueTask DisposeAsync() => client.DisposeAsync();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var consumer = client.CreateConsumer(_options);

        var state = await consumer.OnStateChangeTo(
            state: ConsumerState.Active,
            cancellationToken
        );

        logger.LogInformation("Consumer state: {State}", state);

        try
        {
            await consumer.Process(
                processor: ProcessMessageAsync,
                options: new(),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await consumer.Unsubscribe(CancellationToken.None);
            logger.LogInformation("Consumer stopped");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Consumer faulted");
            throw;
        }

        async ValueTask ProcessMessageAsync(IMessage<byte[]> message, CancellationToken ct)
        {
            logger.LogInformation("Received message: {Message}", Encoding.UTF8.GetString(message.Data));
            await consumer.Acknowledge(message, ct);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        applicationLifetime.StopApplication();
        return Task.CompletedTask;
    }
}

internal sealed class ConsoleExceptionHandler : IHandleException
{
    public ValueTask OnException(ExceptionContext exceptionContext)
    {
        Console.Error.WriteLine(exceptionContext.Exception);
        return ValueTask.CompletedTask;
    }
}