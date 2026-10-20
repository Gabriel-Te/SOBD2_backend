using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20)
});

app.Map("/ws/device", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("Dispositivo conectado");
    var buffer = new byte[4096];

    try
    {
        while (socket.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), context.RequestAborted);
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                break;
            }

            var text = Encoding.UTF8.GetString(ms.ToArray());
            Console.WriteLine($"Recebido: {text}");

            var ack = Encoding.UTF8.GetBytes("{\"type\":\"ack\"}");
            await socket.SendAsync(
                new ArraySegment<byte>(ack),
                WebSocketMessageType.Text, true, context.RequestAborted);
        }
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
    {
        Console.WriteLine("Dispositivo desconectou de forma abrupta");
    }

    Console.WriteLine("Dispositivo desconectado");
});

app.Run("http://0.0.0.0:5000");