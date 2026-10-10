using System.Linq;
using System.Windows.Media;

namespace UiForge.App
{
    /// <summary>
    /// Разбирает цвет из атрибута интерфейса ("FFFFFFFF", "#RRGGBB" и т.п.) в Color.
    /// Точный формат, который использует игра, в открытых источниках не описан —
    /// это лучшее приближение (hex RGB/ARGB), с безопасным запасным цветом.
    /// Если правильный формат окажется другим — достаточно поправить один этот файл.
    /// </summary>
    public static class PwColor
    {
        public static Color Parse(string? raw, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            // Формат игры: "R,G,B,A" десятичными числами, например "255,203,74,255" (проверено на
            // ~5000 значений: последняя компонента почти всегда 255 — это альфа).
            if (raw.Contains(','))
            {
                var parts = raw.Split(',');
                if (parts.Length is 3 or 4 &&
                    parts.All(x => byte.TryParse(x.Trim(), out _)))
                {
                    byte r = byte.Parse(parts[0].Trim()), g = byte.Parse(parts[1].Trim()), b = byte.Parse(parts[2].Trim());
                    byte a = parts.Length == 4 ? byte.Parse(parts[3].Trim()) : (byte)255;
                    return Color.FromArgb(a, r, g, b);
                }
                return fallback;
            }

            string hex = raw.Trim().TrimStart('#');
            try
            {
                if (hex.Length == 8)
                {
                    byte a = System.Convert.ToByte(hex[..2], 16);
                    byte r = System.Convert.ToByte(hex[2..4], 16);
                    byte g = System.Convert.ToByte(hex[4..6], 16);
                    byte b = System.Convert.ToByte(hex[6..8], 16);
                    return Color.FromArgb(a, r, g, b);
                }
                if (hex.Length == 6)
                {
                    byte r = System.Convert.ToByte(hex[..2], 16);
                    byte g = System.Convert.ToByte(hex[2..4], 16);
                    byte b = System.Convert.ToByte(hex[4..6], 16);
                    return Color.FromRgb(r, g, b);
                }
            }
            catch { /* не похоже на hex — используем запасной цвет */ }

            return fallback;
        }
    }
}
