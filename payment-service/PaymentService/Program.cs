using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var failureRatePercent = 0;
var random = new Random();
var payments = new ConcurrentDictionary<Guid, PaymentRecord>();
var chargeAttempts = new ConcurrentDictionary<string, ChargeAttempt>();

app.MapGet("/admin/config", () => Results.Ok(new { failureRatePercent }));

app.MapPost("/admin/config", (ConfigRequest request) =>
{
    failureRatePercent = request.FailureRatePercent;
    return Results.Ok(new { failureRatePercent });
});

app.MapPost("/api/v1/payments", (ChargeRequest request, HttpRequest httpRequest) =>
{
    var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrEmpty(idempotencyKey))
    {
        return Results.BadRequest(new { error = "Idempotency-Key header is required." });
    }

    if (chargeAttempts.TryGetValue(idempotencyKey, out var existingAttempt))
    {
        if (existingAttempt.OrderId != request.OrderId || existingAttempt.Amount != request.Amount)
        {
            return Results.Conflict(new { error = "Idempotency-Key was already used for a different request." });
        }

        return existingAttempt.WasDeclined
            ? Results.StatusCode(402)
            : Results.Ok(new { orderId = request.OrderId, amount = request.Amount, status = "Charged" });
    }

    var roll = random.Next(0, 100);
    var wasDeclined = roll < failureRatePercent;
    chargeAttempts[idempotencyKey] = new ChargeAttempt(request.OrderId, request.Amount, wasDeclined);

    if (wasDeclined)
    {
        return Results.StatusCode(402);
    }

    var record = new PaymentRecord { OrderId = request.OrderId, Amount = request.Amount, Status = "Charged" };
    payments[request.OrderId] = record;

    return Results.Ok(new { orderId = record.OrderId, amount = record.Amount, status = record.Status });
});

app.MapPost("/api/v1/payments/{orderId:guid}/refund", (Guid orderId) =>
{
    if (!payments.TryGetValue(orderId, out var record))
    {
        return Results.NotFound();
    }

    if (record.Status == "Refunded")
    {
        return Results.Conflict(new { error = "Payment already refunded." });
    }

    record.Status = "Refunded";
    return Results.Ok(new { orderId = record.OrderId, amount = record.Amount, status = record.Status });
});

app.Run();

record ConfigRequest(int FailureRatePercent);
record ChargeRequest(Guid OrderId, decimal Amount);
record ChargeAttempt(Guid OrderId, decimal Amount, bool WasDeclined);

class PaymentRecord
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Status { get; set; } = "Charged";
}
