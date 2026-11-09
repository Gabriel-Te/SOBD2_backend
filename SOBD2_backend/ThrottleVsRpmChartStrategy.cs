public class ThrottleVsRpmChartStrategy : IChartStrategy
{
    private const long MaxAgeMs = 500;

    private static readonly PidKey RpmPid = new(0x01, 0x0C);
    private static readonly PidKey ThrottlePid = new(0x01, 0x11);

    public string Title => "TPS vs RPM";

    public IReadOnlyList<PidKey> RequiredPids { get; } = new[]
    {
        RpmPid,
        ThrottlePid
    };

    public bool IsAvailable(HashSet<PidKey> availablePids)
        => RequiredPids.All(availablePids.Contains);

    public ChartPoint? TryBuildPoint(
        IDictionary<PidKey, PidReading> values,
        long nowMs)
    {
        if (!TryGetFresh(values, RpmPid, nowMs, out var rpm) ||
            !TryGetFresh(values, ThrottlePid, nowMs, out var throttle))
        {
            return null;
        }

        return new ChartPoint(
            x: rpm.Value,
            y: new[] { throttle.Value },
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
