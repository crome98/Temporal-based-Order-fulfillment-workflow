using MediatR;
using OrderService.Application.Interfaces;

namespace OrderService.Application.Commands;

public record CreateOrderCommand(decimal Amount, string? IdempotencyKey) : IRequest<CreateOrderResult>;
