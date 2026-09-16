using MediatR;
using OrderService.Application.Commands;
using OrderService.Application.Exceptions;
using OrderService.Application.Queries;
using OrderService.Domain;

namespace OrderService.Api;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/orders", async (IMediator mediator, HttpRequest httpRequest, CreateOrderRequestBody body) =>
        {
            var idempotencyKeyHeader = httpRequest.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrEmpty(idempotencyKeyHeader))
            {
                return Results.BadRequest(new { error = "Idempotency-Key header is required." });
            }

            var command = new CreateOrderCommand(body.Amount, idempotencyKeyHeader);

            try
            {
                var result = await mediator.Send(command);
                return result.WasCreated
                    ? Results.Created($"/api/v1/orders/{result.Order.Id}", result.Order)
                    : Results.Ok(result.Order);
            }
            catch (IdempotencyConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapGet("/api/v1/orders/{id:guid}", async (IMediator mediator, Guid id) =>
        {
            var order = await mediator.Send(new GetOrderQuery(id));
            return order is not null ? Results.Ok(order) : Results.NotFound();
        });

        app.MapPatch("/api/v1/orders/{id:guid}", async (IMediator mediator, Guid id, UpdateOrderStatusRequestBody body) =>
        {
            try
            {
                var order = await mediator.Send(new UpdateOrderStatusCommand(id, body.Status));
                return order is not null ? Results.Ok(order) : Results.NotFound();
            }
            catch (OrderStatusConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOrderStatusTransitionException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });
    }
}

public record CreateOrderRequestBody(decimal Amount);
public record UpdateOrderStatusRequestBody(string Status);
