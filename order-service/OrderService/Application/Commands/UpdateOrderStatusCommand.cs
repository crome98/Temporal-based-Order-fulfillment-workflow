using MediatR;
using OrderService.Domain;

namespace OrderService.Application.Commands;

public record UpdateOrderStatusCommand(Guid Id, string Status) : IRequest<Order?>;
