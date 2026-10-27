// SOBD2_backend/Services/ClientBroadcaster.cs
using Microsoft.AspNetCore.SignalR;

namespace SOBD2_backend.Services;

public class ClientBroadcaster
{
    private readonly IHubContext<TelemetryClientHub> _hub;

    public ClientBroadcaster(IHubContext<TelemetryClientHub> hub)
    {
        _hub = hub;
    }

    public async Task BroadcastLatestAsync(LatestValueStore store)
    {
        var snapshot = store.All
            .Select(r => new
            {
                pid = r.Pid.ToString(),
                value = r.Value,
                timestamp = r.TimestampMs
            })
            .ToList();

        await _hub.Clients.All.SendAsync("pidValue", snapshot);
    }
}