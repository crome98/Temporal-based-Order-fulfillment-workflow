using MediatR;
using OrderService.Domain;

namespace OrderService.Application.Queries;

public record GetOrderQuery(Guid Id) : IRequest<Order?>;
