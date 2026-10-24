public readonly struct PidKey : IEquatable<PidKey>
{
    public byte Mode { get; }
    public byte Pid { get; }

    public PidKey(byte mode, byte pid)
    {
        Mode = mode;
        Pid = pid;
    }

    public override bool Equals(object? obj) => obj is PidKey other && Equals(other);

    public bool Equals(PidKey other) => Mode == other.Mode && Pid == other.Pid;

    public override int GetHashCode() => HashCode.Combine(Mode, Pid);

    public override string ToString() => $"{Mode:X2}-{Pid:X2}";
}