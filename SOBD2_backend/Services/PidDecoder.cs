using System;

public static class PidDecoder
{
    /// <summary>
    /// Decodifica os bytes crus de acordo com o PIDDescriptor.
    /// </summary>
    /// <param name="desc">Descriptor do PID (contém fórmula, bytes, etc)</param>
    /// <param name="hexData">String hexadecimal dos dados recebidos (ex: "1AF8")</param>
    /// <returns>Valor físico decodificado</returns>
    public static double Decode(PIDDescriptor desc, string? hexData)
    {
        if (string.IsNullOrWhiteSpace(hexData))
            throw new ArgumentException("Dados vazios para decodificação.");

        // Converte string hexadecimal em array de bytes
        var bytes = new byte[hexData.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hexData.Substring(i * 2, 2), 16);

        // Para facilitar, nomeia os bytes como A, B, C, D (padrão OBD-II)
        int A = bytes.Length > 0 ? bytes[0] : 0;
        int B = bytes.Length > 1 ? bytes[1] : 0;
        int C = bytes.Length > 2 ? bytes[2] : 0;
        int D = bytes.Length > 3 ? bytes[3] : 0;

        // Decodificação por PID (pode ser expandido conforme o catálogo)
        switch (desc.Key.Pid)
        {
            case 0x04: // Carga calculada do motor
            case 0x11: // TPS
            case 0x2F: // Nível de combustível
                return A * 100.0 / 255.0;

            case 0x05: // Temp. líquido arrefecimento
            case 0x0F: // Temp. ar admissão
            case 0x1F: // Temp. combustível
            case 0x46: // Temp. ambiente
                return A - 40;

            case 0x0B: // Pressão do coletor (MAP)
                return A;

            case 0x0C: // Rotação do motor (RPM)
                return (256 * A + B) / 4.0;

            case 0x0D: // Velocidade
                return A;

            case 0x10: // Vazão de ar (MAF)
                return (256 * A + B) / 100.0;

            case 0x0E: // Avanço ignição
                return (A / 2.0) - 64;

            default:
                throw new NotSupportedException($"PID {desc.Key.Pid:X2} não suportado no decodificador.");
        }
    }
}