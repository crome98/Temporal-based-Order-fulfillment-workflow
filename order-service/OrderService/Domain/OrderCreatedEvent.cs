using MediatR;

namespace OrderService.Domain;

public record OrderCreatedEvent(Guid OrderId, decimal Amount) : INotification;
