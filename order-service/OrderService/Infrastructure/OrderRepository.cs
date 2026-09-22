using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderService.Application.Exceptions;
using OrderService.Application.Interfaces;
using OrderService.Domain;

namespace OrderService.Infrastructure;

public class OrderRepository : IOrderRepository
{
    private readonly OrdersDbContext _db;

    public OrderRepository(OrdersDbContext db)
    {
        _db = db;
    }

    public async Task<CreateOrderResult> CreateOrderAsync(Order order, string? idempotencyKey)
    {
        _db.Orders.Add(order);

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            _db.IdempotencyKeys.Add(new IdempotencyKeyEntity
            {
                Key = idempotencyKey,
                Status = "completed",
                OrderId = order.Id,
            });
        }

        _db.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = Guid.NewGuid(),
            EventType = "OrderCreated",
            Payload = JsonSerializer.Serialize(new { orderId = order.Id, amount = order.Amount }),
            Processed = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync();
            return new CreateOrderResult(order, WasCreated: true);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();

            var existingKey = await _db.IdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.Key == idempotencyKey);

            var existingOrder = existingKey?.OrderId is Guid existingOrderId
                ? await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == existingOrderId)
                : null;

            if (existingOrder is not null && existingOrder.Amount != order.Amount)
            {
                throw new IdempotencyConflictException(idempotencyKey!);
            }

            return new CreateOrderResult(existingOrder!, WasCreated: false);
        }
    }

    public async Task<Order?> GetByIdAsync(Guid id)
    {
        return await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<bool> UpdateStatusAsync(Guid id, string newStatus, string expectedCurrentStatus)
    {
        var affected = await _db.Orders
            .Where(o => o.Id == id && o.Status == expectedCurrentStatus)
            .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.Status, newStatus));

        return affected > 0;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: "23505" };
    }
}
