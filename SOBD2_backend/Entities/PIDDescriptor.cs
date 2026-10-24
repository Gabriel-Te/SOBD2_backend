public class PIDDescriptor
{
    public PidKey Key { get; }
    public string Name { get; }
    public string Unit { get; }
    public int ResponseBytes { get; }
    public double Min { get; }
    public double Max { get; }
    public string Formula { get; }

    public PIDDescriptor(byte mode, byte pid, string name, string unit, int responseBytes, double min, double max, string formula)
    {
        Key = new PidKey(mode, pid);
        Name = name;
        Unit = unit;
        ResponseBytes = responseBytes;
        Min = min;
        Max = max;
        Formula = formula;
    }
}