using Microsoft.Extensions.Logging;
using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

[Workflow]
public class OrderFulfillmentWorkflow
{
    private static readonly ActivityOptions ActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(5),
        RetryPolicy = new RetryPolicy { MaximumAttempts = 3 },
    };

    // Post-pivot: a real shipment already happened, so failures here must be retried to
    // success (or escalated), never compensated - a much more patient policy than the
    // forward-path steps, which can reasonably fail fast.
    private static readonly ActivityOptions PostPivotActivityOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(5),
        RetryPolicy = new RetryPolicy { MaximumAttempts = 10 },
    };

    private static readonly EventId RefundNotificationFailedEventId = new(1001, "RefundNotificationFailed");

    [WorkflowRun]
    public async Task<string> RunAsync(Guid orderId, decimal amount)
    {
        string paymentResult;
        string shipmentResult;

        try
        {
            paymentResult = await Workflow.ExecuteActivityAsync(
                (OrderActivities act) => act.ChargePaymentAsync(orderId, amount),
                ActivityOptions);

            shipmentResult = await Workflow.ExecuteActivityAsync(
                (OrderActivities act) => act.ShipOrderAsync(orderId),
                ActivityOptions);
        }
        catch (ActivityFailureException ex) when (ex.InnerException is ApplicationFailureException { ErrorType: OrderActivities.PaymentDeclinedErrorType })
        {
            // What if payment was completed but the network partition happens between worker and payment gateway?
            // See ChargePaymentAsync: a real network/timeout failure never reaches the 402 check, so it falls
            // to the catch-all below instead - this branch only ever fires on a correctly-received 402.
            await Workflow.ExecuteActivityAsync(
                (OrderActivities act) => act.CancelOrderAsync(orderId),
                ActivityOptions);

            throw;
        }
        catch (ActivityFailureException)
        {
            try
            {
                await Workflow.ExecuteActivityAsync(
                    (OrderActivities act) => act.RefundPaymentAsync(orderId),
                    ActivityOptions);
            }
            catch (ActivityFailureException)
            {
                try
                {
                    await Workflow.ExecuteActivityAsync(
                        (OrderActivities act) => act.NotifyOpsAsync(orderId, "Refund failed - manual review required."),
                        ActivityOptions);
                }
                catch (ActivityFailureException notifyEx)
                {
                    Workflow.Logger.LogError(RefundNotificationFailedEventId, notifyEx, "Failed to notify ops about refund failure for order {OrderId}", orderId);
                }

                throw;
            }

            await Workflow.ExecuteActivityAsync(
                (OrderActivities act) => act.CancelOrderAsync(orderId),
                ActivityOptions);

            throw;
        }

        // Past the pivot now - ShipOrderAsync succeeded, a real package is in transit.
        // No compensation path here on purpose; see PostPivotActivityOptions above.
        await Workflow.ExecuteActivityAsync(
            (OrderActivities act) => act.MarkOrderShippingAsync(orderId),
            PostPivotActivityOptions);

        return $"Order {orderId} fulfilled. Payment: {paymentResult}. Shipment: {shipmentResult}.";
    }
}
