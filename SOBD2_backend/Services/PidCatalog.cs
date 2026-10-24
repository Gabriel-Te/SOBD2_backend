using System.Collections.Concurrent;

public class PidCatalog
{
    private readonly Dictionary<PidKey, PIDDescriptor> _catalog = new()
    {
        [new PidKey(0x01, 0x04)] = new PIDDescriptor(0x01, 0x04, "Carga calculada do motor", "%", 1, 0, 100, "A * 100 / 255"),
        [new PidKey(0x01, 0x05)] = new PIDDescriptor(0x01, 0x05, "Temp. líquido arrefecimento", "°C", 1, -40, 215, "A - 40"),
        [new PidKey(0x01, 0x0B)] = new PIDDescriptor(0x01, 0x0B, "Pressão do coletor (MAP)", "kPa", 1, 0, 255, "A"),
        [new PidKey(0x01, 0x0C)] = new PIDDescriptor(0x01, 0x0C, "Rotação do motor", "rpm", 2, 0, 16383.75, "(256*A + B) / 4"),
        [new PidKey(0x01, 0x0D)] = new PIDDescriptor(0x01, 0x0D, "Velocidade", "km/h", 1, 0, 255, "A"),
        [new PidKey(0x01, 0x0F)] = new PIDDescriptor(0x01, 0x0F, "Temp. ar admissão", "°C", 1, -40, 215, "A - 40"),
        [new PidKey(0x01, 0x11)] = new PIDDescriptor(0x01, 0x11, "TPS", "%", 1, 0, 100, "A * 100 / 255"),
        [new PidKey(0x01, 0x10)] = new PIDDescriptor(0x01, 0x10, "Vazão de ar (MAF)", "g/s", 2, 0, 655.35, "(256*A + B) / 100"),
        [new PidKey(0x01, 0x0E)] = new PIDDescriptor(0x01, 0x0E, "Avanço ignição", "°", 1, -64, 63.5, "(A / 2) - 64"),
        [new PidKey(0x01, 0x1F)] = new PIDDescriptor(0x01, 0x1F, "Temp. combustível", "°C", 1, -40, 215, "A - 40"),
        [new PidKey(0x01, 0x2F)] = new PIDDescriptor(0x01, 0x2F, "Nível de combustível", "%", 1, 0, 100, "A * 100 / 255"),
        [new PidKey(0x01, 0x46)] = new PIDDescriptor(0x01, 0x46, "Temp. ambiente", "°C", 1, -40, 215, "A - 40"),
    };

    public PIDDescriptor? Get(PidKey key) => _catalog.TryGetValue(key, out var desc) ? desc : null;
    public IEnumerable<PIDDescriptor> All => _catalog.Values;
}