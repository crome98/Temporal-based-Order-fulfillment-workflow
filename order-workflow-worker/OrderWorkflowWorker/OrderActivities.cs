using System.Net;
using System.Net.Http.Json;
using Temporalio.Activities;
using Temporalio.Exceptions;

public class OrderActivities
{
    public const string PaymentDeclinedErrorType = "PaymentDeclined";

    private readonly HttpClient _httpClient;
    private readonly string _opsWebhookUrl;

    public OrderActivities(HttpClient httpClient, string opsWebhookUrl)
    {
        _httpClient = httpClient;
        _opsWebhookUrl = opsWebhookUrl;
    }

    [Activity]
    public async Task<string> ChargePaymentAsync(Guid orderId, decimal amount)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://payment-service:8080/api/v1/payments")
        {
            Content = JsonContent.Create(new { orderId, amount }),
        };
        request.Headers.Add("Idempotency-Key", orderId.ToString());

        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            throw new ApplicationFailureException("Payment declined", errorType: PaymentDeclinedErrorType, nonRetryable: true);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Activity]
    public async Task<string> ShipOrderAsync(Guid orderId)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "http://ship-service:8080/api/v1/shipments",
            new { orderId });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Activity]
    public async Task MarkOrderShippingAsync(Guid orderId)
    {
        var response = await _httpClient.PatchAsJsonAsync(
            $"http://order-service:8080/api/v1/orders/{orderId}",
            new { status = "Shipping" });

        response.EnsureSuccessStatusCode();
    }

    [Activity]
    public async Task CancelOrderAsync(Guid orderId)
    {
        var response = await _httpClient.PatchAsJsonAsync(
            $"http://order-service:8080/api/v1/orders/{orderId}",
            new { status = "Cancelled" });

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    [Activity]
    public async Task RefundPaymentAsync(Guid orderId)
    {
        var response = await _httpClient.PostAsync(
            $"http://payment-service:8080/api/v1/payments/{orderId}/refund",
            content: null);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    [Activity]
    public async Task NotifyOpsAsync(Guid orderId, string reason)
    {
        var response = await _httpClient.PostAsJsonAsync(
            _opsWebhookUrl,
            new { orderId, reason, timestamp = DateTimeOffset.UtcNow });

        response.EnsureSuccessStatusCode();
    }
}
