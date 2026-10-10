using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UiForge.App
{
    public sealed class FolderSource : IResourceSource
    {
        private readonly string _root;
        private readonly List<string> _files;

        public FolderSource(string folder)
        {
            _root = folder;
            _files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string Kind => "dir";
        public string Location => _root;
        public string DisplayName => Path.GetFileName(_root.TrimEnd('\\', '/'));
        public IReadOnlyList<string> Files => _files;

        private string Full(string rel) => Path.Combine(_root, rel.Replace('/', '\\').TrimStart('\\'));

        public byte[] Read(string relativePath) => File.ReadAllBytes(Full(relativePath));

        public void Write(string relativePath, byte[] data)
        {
            string full = Full(relativePath);
            string bak = full + ".bak"; // одна резервная копия оригинала при первом сохранении
            if (File.Exists(full) && !File.Exists(bak)) File.Copy(full, bak);
            File.WriteAllBytes(full, data);
        }

        public void Dispose() { }
    }
}
