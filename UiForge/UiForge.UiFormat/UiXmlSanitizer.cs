using System.Text;
using System.Text.RegularExpressions;

namespace UiForge.UiFormat
{
    /// <summary>
    /// Игровые xml нередко нарушают строгий XML: нет пробела между атрибутами (FontSize="16"Color=...),
    /// «голый» '&lt;' или '&amp;' внутри значения атрибута (String="&lt;"). Игра такое терпит, строгий парсер — нет.
    /// Санитайзер на время разбора подменяет такие символы служебными (U+E000 / U+E001), а при
    /// сохранении они возвращаются обратно, так что оригинальный текст значений не искажается.
    /// </summary>
    public static class UiXmlSanitizer
    {
        public const char LtPlaceholder = '\uE000';
        public const char AmpPlaceholder = '\uE001';

        private static readonly Regex Entity =
            new(@"\G&(?:[A-Za-z_][\w.\-]*|#\d+|#x[0-9A-Fa-f]+);", RegexOptions.Compiled);

        public static string Sanitize(string t)
        {
            var sb = new StringBuilder(t.Length + 16);
            int i = 0, n = t.Length;
            bool inTag = false, justClosed = false;
            char quote = '\0';

            while (i < n)
            {
                char c = t[i];

                if (quote != '\0')
                {
                    if (c == quote) { quote = '\0'; justClosed = true; sb.Append(c); }
                    else if (c == '<') sb.Append(LtPlaceholder);
                    else if (c == '&' && !Entity.IsMatch(t, i)) sb.Append(AmpPlaceholder);
                    else sb.Append(c);
                    i++;
                    continue;
                }

                if (inTag)
                {
                    if (justClosed)
                    {
                        justClosed = false;
                        if (!char.IsWhiteSpace(c) && c != '>' && c != '/' && c != '?') sb.Append(' ');
                    }
                    if (c == '"' || c == '\'') { quote = c; sb.Append(c); }
                    else if (c == '>') { inTag = false; sb.Append(c); }
                    else sb.Append(c);
                    i++;
                    continue;
                }

                if (Starts(t, i, "<!--")) { i = CopyThrough(t, sb, i, "-->"); continue; }
                if (Starts(t, i, "<![CDATA[")) { i = CopyThrough(t, sb, i, "]]>"); continue; }
                if (Starts(t, i, "<?")) { i = CopyThrough(t, sb, i, "?>"); continue; }

                if (c == '<') { inTag = true; justClosed = false; }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>Возвращает «голые» символы на место служебных (для сохранения и для показа значений).</summary>
        public static string Restore(string s) =>
            s.IndexOf(LtPlaceholder) < 0 && s.IndexOf(AmpPlaceholder) < 0
                ? s
                : s.Replace(LtPlaceholder, '<').Replace(AmpPlaceholder, '&');

        private static bool Starts(string t, int i, string what) =>
            string.CompareOrdinal(t, i, what, 0, what.Length) == 0;

        private static int CopyThrough(string t, StringBuilder sb, int i, string end)
        {
            int j = t.IndexOf(end, i, System.StringComparison.Ordinal);
            j = j < 0 ? t.Length : j + end.Length;
            sb.Append(t, i, j - i);
            return j;
        }
    }
}
