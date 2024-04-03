using System.Text;
using System.Text.Json.Nodes;
using DotPulsar;
using DotPulsar.Abstractions;
using DotPulsar.Extensions;
using DotPulsar.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new PulsarClientBuilder()
    .ServiceUrl(builder.Configuration.GetValue<Uri>("Pulsar:ServiceUrl")!)
    .RetryInterval(builder.Configuration.GetValue("Pulsar:RetryInterval", TimeSpan.FromSeconds(3)))
    .ExceptionHandler(new ConsoleExceptionHandler())
    .Build());
builder.Services.AddHealthChecks()
    .AddCheck<PulsarHealthCheck>(
        name: "Pulsar",
        failureStatus: HealthStatus.Unhealthy,
        tags: [],
        timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

app.MapHealthChecks("/health", options: new()
{
    Predicate = check => true,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
});

app.MapPost("/api/messages", async (
    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] JsonNode requestBody,
    [FromServices] IPulsarClient pulsarClient,
    [FromServices] IConfiguration configuration,
    [FromServices] ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    await using var producer = pulsarClient
        .NewProducer()
        .ProducerName(configuration.GetValue<string>("Pulsar:ProducerName")!)
        .Topic(configuration.GetValue<string>("Pulsar:Topic")!)
        .Create();

    var messageId = await producer
        .NewMessage()
        .EventTime(DateTimeOffset.UtcNow)
        .Property("Source", $"{Environment.MachineName}:Producer")
        .Send(Encoding.UTF8.GetBytes(requestBody["content"]!.GetValue<string>()), cancellationToken);

    logger.LogInformation("Message sent with ID: {MessageId}", messageId);

    return Results.Accepted();
});

await app.RunAsync();

internal sealed class ConsoleExceptionHandler : IHandleException
{
    public ValueTask OnException(ExceptionContext exceptionContext)
    {
        Console.Error.WriteLine(exceptionContext.Exception);
        return ValueTask.CompletedTask;
    }
}

internal sealed class PulsarHealthCheck(
    IConfiguration configuration,
    IPulsarClient client)
    : IHealthCheck, IAsyncDisposable
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var reader = client
                .NewReader()
                .Topic(configuration.GetValue<string>("Pulsar:Topic")!)
                .StartMessageId(MessageId.Earliest)
                .Create();
                
            return await GetHealthCheckResultFromStateAsync(reader);
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }

        async ValueTask<HealthCheckResult> GetHealthCheckResultFromStateAsync(IReader reader)
        {
            var state = await reader.StateChangedTo(
                state: ReaderState.Connected,
                cancellationToken: cancellationToken);
                
            return state.ReaderState switch
            {
                ReaderState.Connected => HealthCheckResult.Healthy(),
                ReaderState.PartiallyConnected => HealthCheckResult.Degraded(),
                _ => HealthCheckResult.Unhealthy($"Reader state: {state.ReaderState}")
            };
        }
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
