namespace OrderService.Domain;

public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(string currentStatus, string requestedStatus)
        : base($"Cannot transition order from '{currentStatus}' to '{requestedStatus}'.")
    {
    }
}
