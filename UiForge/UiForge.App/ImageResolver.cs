using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace UiForge.App
{
    /// <summary>
    /// Ищет картинку, на которую ссылается интерфейс (просто имя файла, иногда с подпапкой),
    /// в источнике текстур (архив или папка) и декодирует её. Если файла нет — null,
    /// и дизайнер рисует плейсхолдер и показывает предупреждение.
    /// </summary>
    public sealed class ImageResolver
    {
        private static readonly string[] ImageExts = { ".tga", ".dds", ".png", ".jpg", ".jpeg", ".bmp" };

        private readonly IResourceSource _src;
        private readonly Dictionary<string, string> _pathMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _byName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

        public ImageResolver(IResourceSource src)
        {
            _src = src;
            foreach (var f in src.Files)
            {
                _pathMap[f] = f;
                string name = Path.GetFileName(f);
                if (!_byName.TryGetValue(name, out var list)) _byName[name] = list = new List<string>();
                list.Add(f);
            }
        }

        public List<string> FindImageCandidates() =>
            _src.Files.Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();

        public BitmapSource? Resolve(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            if (_cache.TryGetValue(fileName, out var cached)) return cached;

            BitmapSource? result = null;
            string? path = FindPath(fileName);
            if (path != null)
            {
                try { result = Decode(_src.Read(path), path); }
                catch { /* битый/незнакомый формат — плейсхолдер лучше падения */ }
            }
            _cache[fileName] = result;
            return result;
        }

        private string? FindPath(string fileName)
        {
            string wanted = fileName.Replace('/', '\\').TrimStart('\\');
            var variants = new List<string> { wanted };

            // Иногда в xml указан .tga, а в текстурах лежит .dds (и наоборот) — пробуем соседние расширения.
            string noExt = Path.ChangeExtension(wanted, null) ?? wanted;
            foreach (var ext in ImageExts)
                if (!wanted.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    variants.Add(noExt + ext);

            foreach (var v in variants)
            {
                if (_pathMap.TryGetValue(v, out var exact)) return exact;

                if (_byName.TryGetValue(Path.GetFileName(v), out var list))
                {
                    var suffix = list.FirstOrDefault(p => p.EndsWith("\\" + v, StringComparison.OrdinalIgnoreCase));
                    return suffix ?? list[0];
                }
            }
            return null;
        }

        private static BitmapSource? Decode(byte[] data, string name)
        {
            if (data.Length == 0) return null;
            string ext = Path.GetExtension(name).ToLowerInvariant();

            if (ext == ".dds" || DdsDecoder.IsDds(data)) return DdsDecoder.Decode(data);
            if (ext == ".tga" || TgaDecoder.IsTga(data)) return TgaDecoder.Decode(data);

            using var ms = new MemoryStream(data);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            return img;
        }
    }
}
