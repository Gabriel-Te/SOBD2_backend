using SOBD2_backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ESP32Service>();
builder.Services.AddSingleton<LatestValueStore>();
builder.Services.AddSingleton<PidCatalog>();
builder.Services.AddSingleton<TelemetryHub>();
builder.Services.AddSignalR();

var app = builder.Build();

app.UseWebSockets();

app.MapGet("/ws/device", async (HttpContext ctx, ESP32Service esp32) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await esp32.HandleConnectionAsync(socket, ctx.RequestAborted);
});

// O SignalR deve apontar para o Hub do SignalR (TelemetryClientHub)
app.MapHub<TelemetryClientHub>("/hubs/telemetry");

// Força a criação do Hub de domínio na inicialização
app.Services.GetRequiredService<TelemetryHub>();

app.Run("http://0.0.0.0:5000");