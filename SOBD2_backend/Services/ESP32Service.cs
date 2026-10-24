using System.Net.WebSockets;
using System.Text;

namespace SOBD2_backend.Services
{
    public class ESP32Service
    {
        private WebSocket? _socket;

        public bool IsConnected => _socket?.State == WebSocketState.Open;

        // Eventos: quem quiser ouvir (o Hub) se inscreve
        public event Func<string, Task>? OnMessage;
        public event Func<Task>? OnDisconnected;

        public async Task HandleConnectionAsync(WebSocket socket, CancellationToken ct)
        {
            _socket = socket;
            var buffer = new byte[4096];

            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, ct);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    if (OnMessage is not null) await OnMessage(text);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
            finally
            {
                _socket = null;
                if (OnDisconnected is not null) await OnDisconnected();
            }
        }

        public async Task SendAsync(string json, CancellationToken ct = default)
        {
            if (_socket is not { State: WebSocketState.Open }) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
    }
}