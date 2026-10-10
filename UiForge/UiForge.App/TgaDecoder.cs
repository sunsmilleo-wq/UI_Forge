using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UiForge.App
{
    /// <summary>
    /// Минимальный декодер .tga — несжатый и RLE truecolor 24/32 бита на пиксель
    /// (ровно то, что встречается в текстурах интерфейса этой игры). WPF не умеет
    /// читать .tga сам, поэтому распаковываем вручную и отдаём обычный BitmapSource,
    /// как и DdsDecoder рядом.
    /// </summary>
    public static class TgaDecoder
    {
        public static bool IsTga(byte[] data)
        {
            if (data.Length < 18) return false;
            byte imageType = data[2];
            // 2 = truecolor, 10 = RLE truecolor, 3/11 = grayscale — остальное не поддерживаем.
            return imageType is 2 or 10 or 3 or 11;
        }

        public static BitmapSource Decode(byte[] data)
        {
            if (!IsTga(data)) throw new InvalidDataException("Не поддерживаемый .tga (нужен truecolor или grayscale, без палитры).");

            byte idLength = data[0];
            byte imageType = data[2];
            int width = data[12] | (data[13] << 8);
            int height = data[14] | (data[15] << 8);
            int bpp = data[16];
            byte descriptor = data[17];
            bool topDown = (descriptor & 0x20) != 0;

            if (width <= 0 || height <= 0)
                throw new InvalidDataException("Некорректные размеры .tga.");

            int bytesPerPixel = bpp / 8;
            bool isGray = imageType is 3 or 11;
            bool isRle = imageType is 10 or 11;

            int offset = 18 + idLength;
            var bgra = new byte[width * height * 4];

            int pixelIndex = 0;
            int totalPixels = width * height;

            if (!isRle)
            {
                for (int p = 0; p < totalPixels; p++)
                    ReadPixel(data, ref offset, bytesPerPixel, isGray, bgra, p * 4);
            }
            else
            {
                while (pixelIndex < totalPixels)
                {
                    byte header = data[offset++];
                    int count = (header & 0x7F) + 1;

                    if ((header & 0x80) != 0)
                    {
                        var one = new byte[4];
                        ReadPixel(data, ref offset, bytesPerPixel, isGray, one, 0);
                        for (int i = 0; i < count && pixelIndex < totalPixels; i++, pixelIndex++)
                            Array.Copy(one, 0, bgra, pixelIndex * 4, 4);
                    }
                    else
                    {
                        for (int i = 0; i < count && pixelIndex < totalPixels; i++, pixelIndex++)
                            ReadPixel(data, ref offset, bytesPerPixel, isGray, bgra, pixelIndex * 4);
                    }
                }
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);

            if (!topDown)
            {
                var flipped = new TransformedBitmap(bitmap, new ScaleTransform(1, -1));
                flipped.Freeze();
                return flipped;
            }

            bitmap.Freeze();
            return bitmap;
        }

        private static void ReadPixel(byte[] data, ref int offset, int bytesPerPixel, bool isGray, byte[] dest, int destOffset)
        {
            if (isGray)
            {
                byte g = data[offset];
                dest[destOffset + 0] = g;
                dest[destOffset + 1] = g;
                dest[destOffset + 2] = g;
                dest[destOffset + 3] = 255;
                offset += bytesPerPixel;
                return;
            }

            byte b = data[offset + 0];
            byte g2 = data[offset + 1];
            byte r = data[offset + 2];
            byte a = bytesPerPixel >= 4 ? data[offset + 3] : (byte)255;

            dest[destOffset + 0] = b;
            dest[destOffset + 1] = g2;
            dest[destOffset + 2] = r;
            dest[destOffset + 3] = a;

            offset += bytesPerPixel;
        }
    }
}
