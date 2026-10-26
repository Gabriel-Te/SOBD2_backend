public class RpmVsTimeChartStrategy : IChartStrategy
{
    public string Title => "RPM vs Tempo";
    public IReadOnlyList<PidKey> RequiredPids { get; } = new[] { new PidKey(0x01, 0x0C) };

    public bool IsAvailable(HashSet<PidKey> availablePids)
        => RequiredPids.All(availablePids.Contains);

    public ChartPoint? TryBuildPoint(IDictionary<PidKey, PidReading> values, long nowMs)
    {
        if (values.TryGetValue(RequiredPids[0], out var rpm))
        {
            // Exemplo: aceita valores até 500ms de idade
            if (nowMs - rpm.TimestampMs <= 500)
                return new ChartPoint(x: nowMs, y: new[] { rpm.Value }, timestampMs: nowMs);
        }
        return null;
    }
}