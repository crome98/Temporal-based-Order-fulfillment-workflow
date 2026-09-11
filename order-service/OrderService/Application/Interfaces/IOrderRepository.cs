using OrderService.Domain;

namespace OrderService.Application.Interfaces;

public interface IOrderRepository
{
    Task<CreateOrderResult> CreateOrderAsync(Order order, string? idempotencyKey);
    Task<Order?> GetByIdAsync(Guid id);
    Task<bool> UpdateStatusAsync(Guid id, string newStatus, string expectedCurrentStatus);
}

public record CreateOrderResult(Order Order, bool WasCreated);
