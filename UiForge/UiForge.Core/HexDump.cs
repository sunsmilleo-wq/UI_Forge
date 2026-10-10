using System.Text;

namespace UiForge.Core
{
    /// <summary>
    /// Классический hex-дамп (offset | hex-байты | ascii) — для предпросмотра
    /// содержимого файлов, которые не похожи на текст (см. использование в
    /// UiForge.App при просмотре файлов из архива). Не привязан к UI —
    /// чистое форматирование байт в строку.
    /// </summary>
    public static class HexDump
    {
        public static string Format(byte[] data, int maxBytes = 65536)
        {
            if (data.Length == 0) return "(пустой файл)";

            int len = Math.Min(data.Length, maxBytes);
            var sb = new StringBuilder(len * 4);

            for (int offset = 0; offset < len; offset += 16)
            {
                sb.Append(offset.ToString("X8")).Append("  ");

                int rowLen = Math.Min(16, len - offset);
                for (int i = 0; i < 16; i++)
                {
                    if (i < rowLen)
                        sb.Append(data[offset + i].ToString("X2")).Append(' ');
                    else
                        sb.Append("   ");

                    if (i == 7) sb.Append(' ');
                }

                sb.Append(" |");
                for (int i = 0; i < rowLen; i++)
                {
                    byte b = data[offset + i];
                    char c = (b >= 0x20 && b < 0x7F) ? (char)b : '.';
                    sb.Append(c);
                }
                sb.Append('|');
                sb.AppendLine();
            }

            if (data.Length > maxBytes)
                sb.AppendLine($"\n... показаны первые {maxBytes:N0} из {data.Length:N0} байт ...");

            return sb.ToString();
        }
    }
}
