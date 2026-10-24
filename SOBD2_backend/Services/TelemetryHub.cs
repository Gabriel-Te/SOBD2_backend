using System.Text.Json;
using SOBD2_backend.Services;
using System.Collections.Generic;

public class TelemetryHub
{
    private readonly ESP32Service _esp32;
    private readonly LatestValueStore _latest;
    private readonly PidCatalog _catalog;

    // Guarda os PIDs suportados pelo dispositivo conectado
    private HashSet<string> _supportedPids = new();

    public TelemetryHub(ESP32Service esp32, LatestValueStore latest, PidCatalog catalog)
    {
        _esp32 = esp32;
        _latest = latest;
        _catalog = catalog;

        _esp32.OnMessage += HandleDeviceMessageAsync;
        // _esp32.OnDisconnected += OnDeviceDisconnectedAsync; // se quiser tratar desconexão
    }

    private async Task HandleDeviceMessageAsync(string json)
    {
        Console.WriteLine("Mensagem recebida (JSON bruto):");
        Console.WriteLine(json);
        var msg = JsonSerializer.Deserialize<DeviceMessage>(json);

        switch (msg?.Type)
        {
            case "supportedPids":
                if (msg.Pids is not null)
                {
                    _supportedPids = new HashSet<string>(msg.Pids);
                    Console.WriteLine("PIDs suportados recebidos:");
                    Console.WriteLine(string.Join(", ", _supportedPids));

                    var subscribe = new
                    {
                        type = "subscribe",
                        groups = new[]
                        {
                            new {
                                periodMs = 200,
                                pids = _supportedPids
                            }
                        }
                    };
                    var subscribeJson = JsonSerializer.Serialize(subscribe);
                    Console.WriteLine("Enviando assinatura (subscribe) para o ESP32:");
                    Console.WriteLine(subscribeJson);
                    await _esp32.SendAsync(subscribeJson);
                }
                break;

            case "rawReading":
                if (_supportedPids.Contains(msg.Pid))
                {
                    var desc = _catalog.Get(new PidKey(0x01, Convert.ToByte(msg.Pid, 16)));
                    if (desc != null)
                    {
                        double valor = PidDecoder.Decode(desc, msg.Data);
                        var reading = new PidReading(desc.Key, valor, msg.Timestamp);
                        _latest.Update(reading);

                        Console.WriteLine($"RawReading recebido: PID={msg.Pid}, Data={msg.Data}, Timestamp={msg.Timestamp}");
                        Console.WriteLine($"→ {desc.Name} ({desc.Unit}): {valor}");
                    }
                    else
                    {
                        Console.WriteLine($"Descriptor não encontrado para PID {msg.Pid}");
                    }
                }
                else
                {
                    Console.WriteLine($"PID {msg.Pid} não está na lista de suportados, ignorando.");
                }
                break;

            default:
                Console.WriteLine($"Tipo de mensagem não tratado: {msg?.Type}");
                break;
        }
    }
}

// Exemplo mínimo de DeviceMessage para deserialização dinâmica
public class DeviceMessage
{
    public string? Type { get; set; }
    public string? DeviceId { get; set; }
    public string? Adapter { get; set; }
    public string? Protocol { get; set; }
    public string? Pid { get; set; }
    public string? Data { get; set; }
    public long Timestamp { get; set; }
    public List<string>? Pids { get; set; }
}