using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UiForge.App
{
    /// <summary>
    /// Минимальный декодер .dds — ровно то, что встречается в текстурах этой
    /// игры (проверено на реальных файлах: DXT1/DXT3/DXT5 и несжатый BGRA/BGR).
    /// WPF не умеет читать .dds сам по себе (нет встроенного кодека), поэтому
    /// распаковываем блоки вручную и отдаём обычный BitmapSource — дальше он
    /// используется как обычная картинка (ImageBrush и т.п.).
    /// Берём только самый крупный mip-уровень (первый в файле) — этого
    /// достаточно для статичного просмотра, мип-цепочка не нужна.
    /// </summary>
    public static class DdsDecoder
    {
        public static bool IsDds(byte[] data) =>
            data.Length >= 4 && data[0] == 'D' && data[1] == 'D' && data[2] == 'S' && data[3] == ' ';

        public static BitmapSource Decode(byte[] data)
        {
            if (!IsDds(data)) throw new InvalidDataException("Не .dds файл (нет сигнатуры 'DDS ').");
            if (data.Length < 128) throw new InvalidDataException("Файл короче стандартного заголовка .dds.");

            int height = BitConverter.ToInt32(data, 12);
            int width = BitConverter.ToInt32(data, 16);
            int pfFlags = BitConverter.ToInt32(data, 4 + 76);
            string fourCC = System.Text.Encoding.ASCII.GetString(data, 4 + 80, 4);
            int rgbBitCount = BitConverter.ToInt32(data, 4 + 88);
            uint rMask = BitConverter.ToUInt32(data, 4 + 92);
            uint gMask = BitConverter.ToUInt32(data, 4 + 96);
            uint bMask = BitConverter.ToUInt32(data, 4 + 100);
            uint aMask = BitConverter.ToUInt32(data, 4 + 104);

            const int dataOffset = 128; // 4 (magic) + 124 (заголовок)
            const int DDPF_FOURCC = 0x4;
            const int DDPF_RGB = 0x40;

            byte[] bgra;
            if ((pfFlags & DDPF_FOURCC) != 0)
            {
                bgra = fourCC switch
                {
                    "DXT1" => DecodeBlockCompressed(data, dataOffset, width, height, DecodeDxt1Block),
                    "DXT3" => DecodeBlockCompressed(data, dataOffset, width, height, DecodeDxt3Block),
                    "DXT5" => DecodeBlockCompressed(data, dataOffset, width, height, DecodeDxt5Block),
                    _ => throw new InvalidDataException($"Не поддерживаемый формат сжатия .dds: '{fourCC}'."),
                };
            }
            else if ((pfFlags & DDPF_RGB) != 0)
            {
                bgra = DecodeUncompressed(data, dataOffset, width, height, rgbBitCount, rMask, gMask, bMask, aMask);
            }
            else
            {
                throw new InvalidDataException("Неизвестный формат пикселей .dds (ни FourCC, ни RGB-флаг).");
            }

            var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                bgra, width * 4);
            bmp.Freeze();
            return bmp;
        }

        private delegate void BlockDecoder(byte[] data, int blockOffset, byte[] outBgra, int outStride, int px, int py, int width, int height);

        private static byte[] DecodeBlockCompressed(byte[] data, int dataOffset, int width, int height, BlockDecoder decodeBlock)
        {
            var outBgra = new byte[width * height * 4];
            int stride = width * 4;
            int blocksW = (width + 3) / 4;
            int blocksH = (height + 3) / 4;
            int blockSize = decodeBlock == DecodeDxt1Block ? 8 : 16;
            int offset = dataOffset;

            for (int by = 0; by < blocksH; by++)
            {
                for (int bx = 0; bx < blocksW; bx++)
                {
                    decodeBlock(data, offset, outBgra, stride, bx * 4, by * 4, width, height);
                    offset += blockSize;
                }
            }
            return outBgra;
        }

        private static void Rgb565ToRgb888(ushort c, out byte r, out byte g, out byte b)
        {
            r = (byte)(((c >> 11) & 0x1F) * 255 / 31);
            g = (byte)(((c >> 5) & 0x3F) * 255 / 63);
            b = (byte)((c & 0x1F) * 255 / 31);
        }

        private static void PutPixel(byte[] outBgra, int stride, int x, int y, int width, int height,
            byte r, byte g, byte b, byte a)
        {
            if (x >= width || y >= height) return;
            int idx = y * stride + x * 4;
            outBgra[idx + 0] = b;
            outBgra[idx + 1] = g;
            outBgra[idx + 2] = r;
            outBgra[idx + 3] = a;
        }

        /// <summary>Общая для DXT1/3/5 расшифровка цветовой половины блока (8 байт: 2 цвета RGB565 + 2-битные индексы).</summary>
        private static void DecodeColorBlock(byte[] data, int off, bool punchThroughAlpha,
            out (byte r, byte g, byte b, byte a)[] colors, out uint indices)
        {
            ushort c0 = BitConverter.ToUInt16(data, off);
            ushort c1 = BitConverter.ToUInt16(data, off + 2);
            indices = BitConverter.ToUInt32(data, off + 4);

            Rgb565ToRgb888(c0, out byte r0, out byte g0, out byte b0);
            Rgb565ToRgb888(c1, out byte r1, out byte g1, out byte b1);

            colors = new (byte, byte, byte, byte)[4];
            colors[0] = (r0, g0, b0, 255);
            colors[1] = (r1, g1, b1, 255);

            if (!punchThroughAlpha || c0 > c1)
            {
                colors[2] = ((byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3), (byte)255);
                colors[3] = ((byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3), (byte)255);
            }
            else
            {
                colors[2] = ((byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2), (byte)255);
                colors[3] = (0, 0, 0, 0); // прозрачный — режим DXT1 с "пробивной" альфой
            }
        }

        private static void DecodeDxt1Block(byte[] data, int off, byte[] outBgra, int stride, int px, int py, int width, int height)
        {
            DecodeColorBlock(data, off, punchThroughAlpha: true, out var colors, out uint indices);
            for (int i = 0; i < 16; i++)
            {
                int idx = (int)((indices >> (i * 2)) & 0x3);
                var c = colors[idx];
                PutPixel(outBgra, stride, px + i % 4, py + i / 4, width, height, c.r, c.g, c.b, c.a);
            }
        }

        private static void DecodeDxt3Block(byte[] data, int off, byte[] outBgra, int stride, int px, int py, int width, int height)
        {
            // Первые 8 байт — альфа, по 4 бита на пиксель (16 пикселей = 64 бита).
            ulong alphaBits = BitConverter.ToUInt64(data, off);
            DecodeColorBlock(data, off + 8, punchThroughAlpha: false, out var colors, out uint indices);

            for (int i = 0; i < 16; i++)
            {
                int idx = (int)((indices >> (i * 2)) & 0x3);
                var c = colors[idx];
                int nibble = (int)((alphaBits >> (i * 4)) & 0xF);
                byte a = (byte)((nibble << 4) | nibble);
                PutPixel(outBgra, stride, px + i % 4, py + i / 4, width, height, c.r, c.g, c.b, a);
            }
        }

        private static void DecodeDxt5Block(byte[] data, int off, byte[] outBgra, int stride, int px, int py, int width, int height)
        {
            byte a0 = data[off];
            byte a1 = data[off + 1];
            ulong alphaIdxBits = 0;
            for (int i = 0; i < 6; i++) alphaIdxBits |= (ulong)data[off + 2 + i] << (8 * i);

            var alphas = new byte[8];
            alphas[0] = a0; alphas[1] = a1;
            if (a0 > a1)
            {
                for (int i = 1; i <= 5; i++) alphas[1 + i] = (byte)(((6 - i) * a0 + i * a1) / 6);
            }
            else
            {
                for (int i = 1; i <= 3; i++) alphas[1 + i] = (byte)(((4 - i) * a0 + i * a1) / 4);
                alphas[6] = 0; alphas[7] = 255;
            }

            DecodeColorBlock(data, off + 8, punchThroughAlpha: false, out var colors, out uint indices);

            for (int i = 0; i < 16; i++)
            {
                int cIdx = (int)((indices >> (i * 2)) & 0x3);
                var c = colors[cIdx];
                int aIdx = (int)((alphaIdxBits >> (i * 3)) & 0x7);
                byte a = alphas[aIdx];
                PutPixel(outBgra, stride, px + i % 4, py + i / 4, width, height, c.r, c.g, c.b, a);
            }
        }

        private static byte[] DecodeUncompressed(byte[] data, int dataOffset, int width, int height,
            int bitCount, uint rMask, uint gMask, uint bMask, uint aMask)
        {
            var outBgra = new byte[width * height * 4];
            int bytesPerPixel = bitCount / 8;
            if (bytesPerPixel is < 1 or > 4)
                throw new InvalidDataException($"Не поддерживаемая глубина цвета несжатого .dds: {bitCount} бит.");

            int shiftFor(uint mask)
            {
                if (mask == 0) return -1;
                int s = 0;
                while ((mask & 1) == 0) { mask >>= 1; s++; }
                return s;
            }
            int bitsFor(uint mask)
            {
                int n = 0;
                while (mask != 0) { n += (int)(mask & 1); mask >>= 1; }
                return n;
            }
            byte scaleTo8(uint value, int bits) => bits <= 0 ? (byte)255 : (byte)(bits >= 8 ? value >> (bits - 8) : value << (8 - bits));

            int rShift = shiftFor(rMask), gShift = shiftFor(gMask), bShift = shiftFor(bMask), aShift = shiftFor(aMask);
            int rBits = bitsFor(rMask), gBits = bitsFor(gMask), bBits = bitsFor(bMask), aBits = bitsFor(aMask);

            int srcOffset = dataOffset;
            int stride = width * 4;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    uint pixel = 0;
                    for (int b = 0; b < bytesPerPixel; b++) pixel |= (uint)data[srcOffset + b] << (8 * b);
                    srcOffset += bytesPerPixel;

                    byte r = rShift < 0 ? (byte)0 : scaleTo8((pixel & rMask) >> rShift, rBits);
                    byte g = gShift < 0 ? (byte)0 : scaleTo8((pixel & gMask) >> gShift, gBits);
                    byte bl = bShift < 0 ? (byte)0 : scaleTo8((pixel & bMask) >> bShift, bBits);
                    byte a = aShift < 0 ? (byte)255 : scaleTo8((pixel & aMask) >> aShift, aBits);

                    PutPixel(outBgra, stride, x, y, width, height, r, g, bl, a);
                }
            }
            return outBgra;
        }
    }
}
