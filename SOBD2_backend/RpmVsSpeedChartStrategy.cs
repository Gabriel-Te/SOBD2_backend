public class RpmVsSpeedChartStrategy : IChartStrategy
{
    private const long MaxAgeMs = 500;

    private static readonly PidKey RpmPid = new(0x01, 0x0C);
    private static readonly PidKey SpeedPid = new(0x01, 0x0D);

    public string Title => "RPM vs Velocidade";

    public IReadOnlyList<PidKey> RequiredPids { get; } = new[]
    {
        RpmPid,
        SpeedPid
    };

    public bool IsAvailable(HashSet<PidKey> availablePids)
        => RequiredPids.All(availablePids.Contains);

    public ChartPoint? TryBuildPoint(
        IDictionary<PidKey, PidReading> values,
        long nowMs)
    {
        if (!TryGetFresh(values, RpmPid, nowMs, out var rpm) ||
            !TryGetFresh(values, SpeedPid, nowMs, out var speed))
        {
            return null;
        }

        return new ChartPoint(
            x: speed.Value,
            y: new[] { rpm.Value },
            timestampMs: nowMs);
    }

    private static bool TryGetFresh(
        IDictionary<PidKey, PidReading> values,
        PidKey pid,
        long nowMs,
        out PidReading reading)
    {
        if (!values.TryGetValue(pid, out reading!))
        {
            return false;
        }

        var ageMs = nowMs - reading.TimestampMs;
        return ageMs >= 0 && ageMs <= MaxAgeMs;
    }
}
