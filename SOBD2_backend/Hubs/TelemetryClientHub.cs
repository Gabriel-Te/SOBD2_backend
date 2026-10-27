using Microsoft.AspNetCore.SignalR;
namespace SOBD2_backend.Hubs;


public class TelemetryClientHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("deviceStatus", new
        {
            connected = true,
            connectionId = Context.ConnectionId
        });

        await base.OnConnectedAsync();
    }
}