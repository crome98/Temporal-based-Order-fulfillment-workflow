namespace OrderService.Domain;

public class Order
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
}
