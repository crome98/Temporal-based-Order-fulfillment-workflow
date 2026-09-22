using Microsoft.EntityFrameworkCore;
using OrderService.Api;
using OrderService.Application.Interfaces;
using OrderService.Infrastructure;
using Temporalio.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();

var temporalAddress = Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS") ?? "localhost:7233";
builder.Services.AddSingleton<ITemporalClient>(await TemporalClient.ConnectAsync(new(temporalAddress)));
builder.Services.AddHostedService<OutboxProcessor>();

var app = builder.Build();

app.MapOrderEndpoints();

app.Run();
