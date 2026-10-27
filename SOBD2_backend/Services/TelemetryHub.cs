using System.Text.Json;
using SOBD2_backend.Services;
using System.Collections.Generic;
using System.Linq;

public class TelemetryHub
{
    private readonly ESP32Service _esp32;
    private readonly LatestValueStore _latest;
    private readonly PidCatalog _catalog;
    private readonly ClientBroadcaster _broadcaster;

    private HashSet<PidKey> _supportedPids = new();

    private readonly List<IChartStrategy> _strategies = new()
    {
        new RpmVsTimeChartStrategy(),
        // adicione as outras strategies aqui
    };

    public TelemetryHub(
        ESP32Service esp32,
        LatestValueStore latest,
        PidCatalog catalog,
        ClientBroadcaster broadcaster)
    {
        _esp32 = esp32;
        _latest = latest;
        _catalog = catalog;
        _broadcaster = broadcaster;

        _esp32.OnMessage += HandleDeviceMessageAsync;
    }

    private async Task HandleDeviceMessageAsync(string json)
    {
        Console.WriteLine("Mensagem recebida (JSON bruto):");
        Console.WriteLine(json);
        DeviceMessage? msg = null;
        try
        {
            msg = JsonSerializer.Deserialize<DeviceMessage>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao desserializar mensagem: {ex.Message}");
            return;
        }

        switch (msg?.Type)
        {
            case "supportedPids":
                if (msg.Pids is not null)
                {
                    try
                    {
                        _supportedPids = new HashSet<PidKey>(
                            msg.Pids.Select(pidStr =>
                            {
                                try
                                {
                                    return new PidKey(0x01, Convert.ToByte(pidStr, 16));
                                }
                                catch
                                {
                                    Console.WriteLine($"PID inválido recebido: {pidStr}");
                                    return default;
                                }
                            }).Where(pk => !pk.Equals(default(PidKey)))
                        );
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao processar lista de PIDs suportados: {ex.Message}");
                        _supportedPids.Clear();
                    }

                    Console.WriteLine("PIDs suportados recebidos:");
                    Console.WriteLine(string.Join(", ", _supportedPids.Select(pk => pk.Pid.ToString("X2"))));

                    var subscribe = new
                    {
                        type = "subscribe",
                        groups = new[]
                        {
                            new {
                                periodMs = 200,
                                pids = _supportedPids.Select(pk => pk.Pid.ToString("X2")).ToList()
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
                if (!string.IsNullOrWhiteSpace(msg.Pid))
                {
                    PidKey pidKey;
                    try
                    {
                        pidKey = new PidKey(0x01, Convert.ToByte(msg.Pid, 16));
                    }
                    catch
                    {
                        Console.WriteLine($"PID inválido em rawReading: {msg.Pid}");
                        break;
                    }

                    if (_supportedPids.Contains(pidKey))
                    {
                        var desc = _catalog.Get(pidKey);
                        if (desc != null)
                        {
                            double valor;
                            try
                            {
                                valor = PidDecoder.Decode(desc, msg.Data);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Erro ao decodificar PID {msg.Pid}: {ex.Message}");
                                break;
                            }
                            var reading = new PidReading(desc.Key, valor, msg.Timestamp);
                            _latest.Update(reading);

                            await _broadcaster.BroadcastLatestAsync(_latest);

                            Console.WriteLine($"RawReading recebido: PID={msg.Pid}, Data={msg.Data}, Timestamp={msg.Timestamp}");
                            Console.WriteLine($"→ {desc.Name} ({desc.Unit}): {valor}");

                            // Avalia gráficos
                            await EvaluateCharts(msg.Timestamp);
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
                }
                else
                {
                    Console.WriteLine("PID ausente ou inválido em rawReading.");
                }
                break;

            default:
                Console.WriteLine($"Tipo de mensagem não tratado: {msg?.Type}");
                break;
        }
    }

    // Chame este método sempre que receber um novo valor de PID
    private async Task EvaluateCharts(long nowMs)
    {
        var values = _latest.All.ToDictionary(r => r.Pid, r => r);

        foreach (var strategy in _strategies)
        {
            if (strategy.IsAvailable(_supportedPids))
            {
                var point = strategy.TryBuildPoint(values, nowMs);

                if (point is not null)
                {
                    await _broadcaster.BroadcastChartPoint(strategy.Title, point);

                    Console.WriteLine(
                        $"Novo ponto para gráfico '{strategy.Title}': X={point.X}, Y={string.Join(",", point.Y)}");
                }
            }
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