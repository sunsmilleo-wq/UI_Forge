using System;
using System.Linq;
using System.Text;

namespace UiForge.Core
{
    /// <summary>Результат определения кодировки: текст + всё, что нужно, чтобы записать файл обратно один в один.</summary>
    public sealed class DecodedText
    {
        public string Text { get; init; } = "";
        /// <summary>null — содержимое похоже на бинарные данные.</summary>
        public Encoding? Encoding { get; init; }
        public bool HasBom { get; init; }
        /// <summary>0, 2 или 4: перед текстом лежит заголовок с длиной содержимого (little-endian).
        /// При записи длина пересчитывается заново по новому тексту.</summary>
        public int LengthPrefixBytes { get; init; }
        /// <summary>Сколько лишних символов U+FEFF стоит в начале после основного BOM (в играх бывает
        /// «двойной» и даже 12-кратный BOM). Они убираются из текста и возвращаются при сохранении.</summary>
        public int ExtraBoms { get; init; }
    }

    /// <summary>Определение и (де)кодирование текстового содержимого файла из PCK.</summary>
    public static class TextCodec
    {
        public static DecodedText Decode(byte[] data)
        {
            if (data.Length == 0)
                return new DecodedText { Text = "", Encoding = Encoding.Unicode, HasBom = false };

            // 1) Явный BOM.
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Make(Encoding.Unicode.GetString(data, 2, data.Length - 2), Encoding.Unicode, true);
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Make(Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2), Encoding.BigEndianUnicode, true);
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Make(Encoding.UTF8.GetString(data, 3, data.Length - 3), Encoding.UTF8, true);

            Encoding? gbkEncoding = TryGetGbk();
            var candidates = new[] { Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF8, gbkEncoding }
                .Where(e => e != null).Select(e => e!).ToArray();

            // 2) Без BOM: берём первую кодировку, при которой текст реально начинается с "<".
            foreach (var enc in candidates)
            {
                string? s = SafeDecode(data, enc);
                if (LooksLikeXmlStart(s)) return Make(s!, enc, false);
            }

            // 3) Служебный заголовок с длиной перед XML (частый приём в игровых контейнерах).
            //    Принимаем, только если ОБА условия сошлись: длина из заголовка ≈ остаток файла
            //    И после заголовка текст начинается с "<". Ложные срабатывания практически исключены.
            foreach (int prefixLen in new[] { 4, 2 })
            {
                if (data.Length <= prefixLen + 2) continue;
                long declared = prefixLen == 4 ? BitConverter.ToUInt32(data, 0) : BitConverter.ToUInt16(data, 0);
                long remaining = data.Length - prefixLen;
                if (declared <= 0 || Math.Abs(declared - remaining) > 8) continue;

                byte[] rest = new byte[remaining];
                Buffer.BlockCopy(data, prefixLen, rest, 0, rest.Length);

                // после заголовка может быть и BOM
                if (rest.Length >= 2 && rest[0] == 0xFF && rest[1] == 0xFE)
                    return Make(Encoding.Unicode.GetString(rest, 2, rest.Length - 2), Encoding.Unicode, true, prefixLen);

                foreach (var enc in candidates)
                {
                    string? s = SafeDecode(rest, enc);
                    if (LooksLikeXmlStart(s)) return Make(s!, enc, false, prefixLen);
                }
            }

            // 4) Запасные варианты: на XML не похоже, но это может быть просто текст.
            string? le = SafeDecode(data, Encoding.Unicode);
            if (le != null && LooksLikeUtf16LeWithoutBom(data))
                return Make(le, Encoding.Unicode, false);

            string utf8 = Encoding.UTF8.GetString(data);
            if (utf8.Count(c => c == '\uFFFD') <= utf8.Length / 20)
                return Make(utf8, Encoding.UTF8, false);

            string? gbk = SafeDecode(data, gbkEncoding);
            if (gbk != null && gbk.Count(c => c < 32 && c != '\r' && c != '\n' && c != '\t') < gbk.Length / 20)
                return Make(gbk, gbkEncoding!, false);

            // 5) Бинарные данные.
            return new DecodedText { Text = HexDump.Format(data), Encoding = null };
        }

        private static DecodedText Make(string text, Encoding enc, bool bom, int prefix = 0)
        {
            int extra = 0;
            while (extra < text.Length && text[extra] == '\uFEFF') extra++;
            return new DecodedText
            {
                Text = extra == 0 ? text : text.Substring(extra),
                Encoding = enc, HasBom = bom, LengthPrefixBytes = prefix, ExtraBoms = extra
            };
        }

        /// <summary>Кодирует текст обратно так же, как он был прочитан (кодировка, BOM, заголовок с длиной).</summary>
        public static byte[] Encode(string text, Encoding encoding, bool hasBom, int lengthPrefixBytes = 0, int extraBoms = 0)
        {
            if (extraBoms > 0) text = new string('\uFEFF', extraBoms) + text;
            byte[] body = encoding.GetBytes(text);
            if (hasBom)
            {
                byte[] preamble = encoding.GetPreamble();
                if (preamble.Length > 0)
                {
                    var withBom = new byte[preamble.Length + body.Length];
                    preamble.CopyTo(withBom, 0);
                    body.CopyTo(withBom, preamble.Length);
                    body = withBom;
                }
            }

            if (lengthPrefixBytes == 0) return body;

            byte[] header = lengthPrefixBytes == 4
                ? BitConverter.GetBytes((uint)body.Length)
                : BitConverter.GetBytes((ushort)body.Length);

            var result = new byte[header.Length + body.Length];
            header.CopyTo(result, 0);
            body.CopyTo(result, header.Length);
            return result;
        }

        /// <summary>Первые байты файла в hex — для диагностики, когда файл не открывается.</summary>
        public static string HexPreview(byte[] data, int count = 48)
        {
            int n = Math.Min(count, data.Length);
            return string.Join(" ", data.Take(n).Select(b => b.ToString("X2")));
        }

        private static Encoding? TryGetGbk()
        {
            try { return Encoding.GetEncoding(936); }
            catch { return null; }
        }

        private static string? SafeDecode(byte[] data, Encoding? encoding)
        {
            if (encoding is null) return null;
            if (encoding is UnicodeEncoding && (data.Length < 2 || data.Length % 2 != 0)) return null;
            try { return encoding.GetString(data); }
            catch { return null; }
        }

        private static bool LooksLikeXmlStart(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int i = 0;
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == '\uFEFF')) i++;
            return i < text.Length && text[i] == '<';
        }

        private static bool LooksLikeUtf16LeWithoutBom(byte[] data)
        {
            int sampleLen = Math.Min(data.Length - (data.Length % 2), 400);
            if (sampleLen < 4) return false;
            int zeroAtOdd = 0;
            for (int i = 1; i < sampleLen; i += 2)
                if (data[i] == 0x00) zeroAtOdd++;
            return zeroAtOdd > (sampleLen / 2) * 0.8;
        }
    }
}
