using System;
using System.Collections.Generic;
using System.IO;
using UiForge.Core;
using UiForge.Core.Model;

namespace UiForge.App
{
    public sealed class PckSource : IResourceSource
    {
        private readonly IPckEngine _engine;
        private readonly Dictionary<string, PckEntry> _map = new(StringComparer.OrdinalIgnoreCase);
        private List<string> _files = new();

        public PckSource(IPckEngine engine, string archivePath)
        {
            _engine = engine;
            Location = archivePath;
            Reindex();
        }

        public string Kind => "pck";
        public string Location { get; }
        public string DisplayName => Path.GetFileName(Location);
        public IReadOnlyList<string> Files => _files;

        private void Reindex()
        {
            _map.Clear();
            var list = new List<string>();
            Walk(_engine.GetRoot(), list);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            _files = list;
        }

        private void Walk(PckEntry entry, List<string> list)
        {
            if (!entry.IsDirectory)
            {
                _map[entry.RelativePath] = entry;
                list.Add(entry.RelativePath);
                return;
            }
            foreach (var child in entry.Children)
                Walk(child, list);
        }

        private static string Normalize(string p) => p.Replace('/', '\\').TrimStart('\\');

        public byte[] Read(string relativePath)
        {
            if (!_map.TryGetValue(Normalize(relativePath), out var entry))
                throw new FileNotFoundException("Нет в архиве: " + relativePath);
            return _engine.ReadFile(entry);
        }

        public void Write(string relativePath, byte[] data)
        {
            if (!_map.TryGetValue(Normalize(relativePath), out var entry))
                throw new FileNotFoundException("Нет в архиве: " + relativePath);
            _engine.ReplaceFileContent(entry, data);
            Reindex(); // архив переоткрыт — старые PckEntry недействительны
        }

        public void Dispose() => _engine.Dispose();
    }
}
