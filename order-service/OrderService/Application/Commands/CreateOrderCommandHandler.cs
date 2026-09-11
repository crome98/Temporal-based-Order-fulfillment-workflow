using MediatR;
using OrderService.Application.Interfaces;
using OrderService.Domain;

namespace OrderService.Application.Commands;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly IOrderRepository _repository;
    private readonly IPublisher _publisher;

    public CreateOrderCommandHandler(IOrderRepository repository, IPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = "Placed",
            Amount = request.Amount,
        };

        var result = await _repository.CreateOrderAsync(order, request.IdempotencyKey);

        if (result.WasCreated)
        {
            await _publisher.Publish(new OrderCreatedEvent(result.Order.Id, result.Order.Amount), cancellationToken);
        }

        return result;
    }
}
