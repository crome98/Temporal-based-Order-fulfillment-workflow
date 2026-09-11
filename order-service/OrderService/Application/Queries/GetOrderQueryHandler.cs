using MediatR;
using OrderService.Application.Interfaces;
using OrderService.Domain;

namespace OrderService.Application.Queries;

public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, Order?>
{
    private readonly IOrderRepository _repository;

    public GetOrderQueryHandler(IOrderRepository repository)
    {
        _repository = repository;
    }

    public Task<Order?> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        return _repository.GetByIdAsync(request.Id);
    }
}
