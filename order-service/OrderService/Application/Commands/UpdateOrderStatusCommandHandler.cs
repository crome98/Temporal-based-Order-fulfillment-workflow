using MediatR;
using OrderService.Application.Exceptions;
using OrderService.Application.Interfaces;
using OrderService.Domain;

namespace OrderService.Application.Commands;

public class UpdateOrderStatusCommandHandler : IRequestHandler<UpdateOrderStatusCommand, Order?>
{
    private readonly IOrderRepository _repository;

    public UpdateOrderStatusCommandHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Order?> Handle(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(request.Id);
        if (order is null)
        {
            return null;
        }

        var succeeded = await _repository.UpdateStatusAsync(request.Id, request.Status, order.Status);
        if (!succeeded)
        {
            throw new OrderStatusConflictException(request.Id, order.Status);
        }

        order.Status = request.Status;
        return order;
    }
}
