using Microsoft.EntityFrameworkCore;
using OrderService.Api;
using OrderService.Application.Interfaces;
using OrderService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();

var app = builder.Build();

app.MapOrderEndpoints();

app.Run();
