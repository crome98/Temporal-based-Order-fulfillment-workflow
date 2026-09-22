using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Text.Json;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.Protocol.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace OrderService.Infrastructure;

public class OutboxProcessor : BackgroundService
{
    private const string TaskQueue = "order-fulfillment";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions PayloadDeserializeOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITemporalClient _temporalClient;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ITemporalClient temporalClient, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _temporalClient = temporalClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessOutboxAsync(stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessOutboxAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();


        _logger.LogInformation("Worker is running");

        var unprocessedMessages = await db.OutboxMessages
            .Where(m => !m.Processed)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var message in unprocessedMessages)
        {
            try
            {
                await StartWorkflowForMessageAsync(message);
                message.Processed = true;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {MessageId}, will retry next poll", message.Id);
            }
        }
    }

    private async Task StartWorkflowForMessageAsync(OutboxMessageEntity message)
    {
        if (message.EventType != "OrderCreated")
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<OrderCreatedPayload>(message.Payload, PayloadDeserializeOptions)!;

        _logger.LogInformation("Attempting StartWorkflowAsync for order {OrderId}", payload.OrderId);

        try
        {
            var handle = await _temporalClient.StartWorkflowAsync(
                "OrderFulfillmentWorkflow",
                new object[] { payload.OrderId, payload.Amount },
                new WorkflowOptions(id: payload.OrderId.ToString(), taskQueue: TaskQueue)
                {
                    IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
                });

            _logger.LogInformation("StartWorkflowAsync succeeded for order {OrderId}, RunId {RunId}", payload.OrderId, handle.ResultRunId);
        }
        catch (WorkflowAlreadyStartedException ex)
        {
            // A prior poll already started this workflow successfully but failed to mark the
            // row processed afterward (e.g. a DB blip) - the workflow existing is success, not a failure.
            _logger.LogWarning(ex, "WorkflowAlreadyStartedException for order {OrderId} - treating as already-started success", payload.OrderId);
        }
    }

    private record OrderCreatedPayload(Guid OrderId, decimal Amount);
}
