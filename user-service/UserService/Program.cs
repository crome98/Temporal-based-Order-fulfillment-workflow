var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var failureRatePercent = 0;
var random = new Random();

app.MapGet("/admin/config", () => Results.Ok(new { failureRatePercent }));

app.MapPost("/admin/config", (ConfigRequest request) =>
{
    failureRatePercent = request.FailureRatePercent;
    return Results.Ok(new { failureRatePercent });
});

app.MapFallback(() =>
{
    var roll = random.Next(0, 100);
    if (roll < failureRatePercent)
    {
        return Results.StatusCode(500);
    }

    return Results.Ok("OK from user-service");
});

app.Run();

record ConfigRequest(int FailureRatePercent);
