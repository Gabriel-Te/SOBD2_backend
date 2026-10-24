using System.Collections.Concurrent;

public class LatestValueStore
{
    private readonly ConcurrentDictionary<PidKey, PidReading> _latest = new();

    public void Update(PidReading reading)
    {
        _latest[reading.Pid] = reading;
    }

    public bool TryGet(PidKey key, out PidReading? reading)
    {
        return _latest.TryGetValue(key, out reading);
    }

    // Exemplo de método para pegar todos os valores atuais
    public IEnumerable<PidReading> All => _latest.Values;
}

public class PidReading
{
    public PidKey Pid { get; }
    public double Value { get; }
    public long TimestampMs { get; }

    public PidReading(PidKey pid, double value, long timestampMs)
    {
        Pid = pid;
        Value = value;
        TimestampMs = timestampMs;
    }
}