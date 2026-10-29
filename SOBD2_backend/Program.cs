using SOBD2_backend.Services;
using SOBD2_backend.Hubs;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ESP32Service>();
builder.Services.AddSingleton<LatestValueStore>();
builder.Services.AddSingleton<PidCatalog>();
builder.Services.AddSingleton<TelemetryHub>();
builder.Services.AddSingleton<ClientBroadcaster>();

builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalHost", policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseWebSockets();
app.UseCors("AllowLocalHost");
app.UseDefaultFiles();
app.UseStaticFiles();

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

app.MapHub<TelemetryClientHub>("/hubs/telemetry");

app.Services.GetRequiredService<TelemetryHub>();

app.Run("http://0.0.0.0:5000");