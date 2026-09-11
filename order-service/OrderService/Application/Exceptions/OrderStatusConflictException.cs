namespace OrderService.Application.Exceptions;

public class OrderStatusConflictException : Exception
{
    public OrderStatusConflictException(Guid orderId, string expectedStatus)
        : base($"Order '{orderId}' was not in the expected status '{expectedStatus}' - it was modified concurrently.")
    {
    }
}
