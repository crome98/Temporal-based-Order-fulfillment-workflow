using Temporalio.Client;
using Temporalio.Worker;

const string TaskQueue = "order-fulfillment";

var temporalAddress = Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS") ?? "localhost:7233";
var client = await TemporalClient.ConnectAsync(new(temporalAddress));

var opsWebhookUrl = Environment.GetEnvironmentVariable("OPS_WEBHOOK_URL")
    ?? throw new InvalidOperationException("OPS_WEBHOOK_URL environment variable is required.");

var activities = new OrderActivities(new HttpClient(), opsWebhookUrl);

using var worker = new TemporalWorker(client, new TemporalWorkerOptions(TaskQueue)
    .AddWorkflow<OrderFulfillmentWorkflow>()
    .AddActivity(activities.ChargePaymentAsync)
    .AddActivity(activities.ShipOrderAsync)
    .AddActivity(activities.MarkOrderShippingAsync)
    .AddActivity(activities.CancelOrderAsync)
    .AddActivity(activities.RefundPaymentAsync)
    .AddActivity(activities.NotifyOpsAsync));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"Worker started, polling task queue: {TaskQueue}");
await worker.ExecuteAsync(cts.Token);
