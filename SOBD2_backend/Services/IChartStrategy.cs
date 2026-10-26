using System.Collections.Generic;

public interface IChartStrategy
{
    /// <summary>
    /// Nome/título do gráfico (ex: "RPM vs Tempo").
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Lista dos PIDs necessários para montar o gráfico.
    /// </summary>
    IReadOnlyList<PidKey> RequiredPids { get; }

    /// <summary>
    /// Retorna true se todos os PIDs necessários estão disponíveis.
    /// </summary>
    bool IsAvailable(HashSet<PidKey> availablePids);

    /// <summary>
    /// Monta um ponto do gráfico a partir dos valores atuais.
    /// Retorna null se não for possível montar (dados faltando ou expirados).
    /// </summary>
    ChartPoint? TryBuildPoint(IDictionary<PidKey, PidReading> values, long nowMs);
}

/// <summary>
/// Representa um ponto de gráfico genérico (X, Y1, Y2...).
/// </summary>
public class ChartPoint
{
    public double X { get; }
    public double[] Y { get; }
    public long TimestampMs { get; }

    public ChartPoint(double x, double[] y, long timestampMs)
    {
        X = x;
        Y = y;
        TimestampMs = timestampMs;
    }
}