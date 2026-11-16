using Microsoft.AspNetCore.SignalR;
using SOBD2_backend.Hubs;

namespace SOBD2_backend.Services;

public class ClientBroadcaster
{
    private readonly IHubContext<TelemetryClientHub> _hub;
    private readonly PidCatalog _catalog;

    public ClientBroadcaster(
        IHubContext<TelemetryClientHub> hub,
        PidCatalog catalog)
    {
        _hub = hub;
        _catalog = catalog;
    }

    public async Task BroadcastLatestAsync(LatestValueStore store)
    {
        var snapshot = store.All
            .Select(reading =>
            {
                var descriptor = _catalog.Get(reading.Pid);

                return new
                {
                    pid = reading.Pid.ToString(),
                    name = descriptor?.Name ?? "PID desconhecido",
                    unit = descriptor?.Unit ?? string.Empty,
                    value = reading.Value,
                    timestamp = reading.TimestampMs
                };
            })
            .OrderBy(reading => reading.pid)
            .ToList();

        await _hub.Clients.All.SendAsync("pidValue", snapshot);
    }

    public async Task BroadcastChartPoint(
        string chartName,
        ChartPoint point)
    {
        await _hub.Clients.All.SendAsync("chartPoint", new
        {
            chart = chartName,
            x = point.X,
            y = point.Y,
            timestamp = point.TimestampMs
        });
    }
}