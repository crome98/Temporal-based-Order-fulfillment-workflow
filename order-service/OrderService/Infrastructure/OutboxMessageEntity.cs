namespace OrderService.Infrastructure;

public class OutboxMessageEntity
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public bool Processed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
