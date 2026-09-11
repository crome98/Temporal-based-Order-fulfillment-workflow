namespace OrderService.Infrastructure;

public class IdempotencyKeyEntity
{
    public string Key { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid? OrderId { get; set; }
}
