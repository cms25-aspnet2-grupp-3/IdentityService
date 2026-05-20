using Azure.Messaging.ServiceBus;
using IdentityService.Data;
using IdentityService.Messaging;
using IdentityService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────
// Database
// ──────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ──────────────────────────────────────────────
// Azure Service Bus
// ──────────────────────────────────────────────
// builder.Services.AddSingleton(_ =>
//     new ServiceBusClient(builder.Configuration.GetConnectionString("ServiceBus")));
// builder.Services.AddScoped<ServiceBusPublisher>();

// ──────────────────────────────────────────────
// gRPC
// ──────────────────────────────────────────────
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// ──────────────────────────────────────────────
// Build & Middleware
// ──────────────────────────────────────────────
var app = builder.Build();

// Auto-apply migrations and seed roles in dev
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapGrpcService<IdentityGrpcService>();

// Simple health check — lets Azure know the service is alive
app.MapGet("/", () => "Identity-Service is running. gRPC only.");

app.Run();
