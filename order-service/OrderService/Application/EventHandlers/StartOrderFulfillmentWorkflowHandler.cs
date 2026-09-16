using MediatR;
using OrderService.Domain;
using Temporalio.Client;

namespace OrderService.Application.EventHandlers;

public class StartOrderFulfillmentWorkflowHandler : INotificationHandler<OrderCreatedEvent>
{
    private const string TaskQueue = "order-fulfillment";

    private readonly ITemporalClient _client;

    public StartOrderFulfillmentWorkflowHandler(ITemporalClient client)
    {
        _client = client;
    }

    public async Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        await _client.StartWorkflowAsync(
            "OrderFulfillmentWorkflow",
            new object[] { notification.OrderId, notification.Amount },
            new WorkflowOptions(id: notification.OrderId.ToString(), taskQueue: TaskQueue));
    }
}
