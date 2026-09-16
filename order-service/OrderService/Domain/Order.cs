namespace OrderService.Domain;

public class Order
{
    public Guid Id { get; private set; }
    public string Status { get; private set; } = "";
    public decimal Amount { get; private set; }

    private static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        ["Placed"] = new[] { "Shipping", "Cancelled" },
        ["Shipping"] = new[] { "Delivered" },
        ["Cancelled"] = Array.Empty<string>(),
        ["Delivered"] = Array.Empty<string>(),
    };

    private Order()
    {
    }

    public Order(Guid id, string status, decimal amount)
    {
        Id = id;
        Status = status;
        Amount = amount;
    }

    public void EnsureCanTransitionTo(string newStatus)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new InvalidOrderStatusTransitionException(Status, newStatus);
        }
    }

    public void ApplyStatusChange(string newStatus)
    {
        Status = newStatus;
    }
}
