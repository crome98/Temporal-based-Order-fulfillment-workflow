using Microsoft.EntityFrameworkCore;
using OrderService.Domain;

namespace OrderService.Infrastructure;

public class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<IdempotencyKeyEntity> IdempotencyKeys => Set<IdempotencyKeyEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Id).HasColumnName("id");
            entity.Property(o => o.Status).HasColumnName("status");
            entity.Property(o => o.Amount).HasColumnName("amount");
        });

        modelBuilder.Entity<IdempotencyKeyEntity>(entity =>
        {
            entity.ToTable("idempotency_keys");
            entity.HasKey(k => k.Key);
            entity.Property(k => k.Key).HasColumnName("key");
            entity.Property(k => k.Status).HasColumnName("status");
            entity.Property(k => k.OrderId).HasColumnName("order_id");
        });

        modelBuilder.Entity<OutboxMessageEntity>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).HasColumnName("id");
            entity.Property(m => m.EventType).HasColumnName("event_type");
            entity.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(m => m.Processed).HasColumnName("processed");
            entity.Property(m => m.CreatedAt).HasColumnName("created_at");
        });
    }
}
