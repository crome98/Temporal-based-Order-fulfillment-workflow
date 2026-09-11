namespace OrderService.Application.Exceptions;

public class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string idempotencyKey)
        : base($"Idempotency key '{idempotencyKey}' was already used with different request content.")
    {
    }
}
