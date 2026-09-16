using MediatR;
using OrderService.Domain;

namespace OrderService.Application.EventHandlers;

public class LogOrderCreatedEventHandler : INotificationHandler<OrderCreatedEvent>
{
    private readonly ILogger<LogOrderCreatedEventHandler> _logger;

    public LogOrderCreatedEventHandler(ILogger<LogOrderCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OrderCreatedEvent observed: OrderId={OrderId}, Amount={Amount}",
            notification.OrderId,
            notification.Amount);

        return Task.CompletedTask;
    }
}
