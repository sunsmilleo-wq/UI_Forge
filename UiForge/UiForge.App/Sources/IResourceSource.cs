using System;
using System.Collections.Generic;

namespace UiForge.App
{
    /// <summary>
    /// Откуда берём файлы: архив .pck или обычная папка с распакованными файлами
    /// (как «две папки» в старом UIEdit). Дизайнеру всё равно, что именно за источником.
    /// </summary>
    public interface IResourceSource : IDisposable
    {
        /// <summary>"pck" или "dir" — нужно, чтобы запомнить источник между запусками.</summary>
        string Kind { get; }
        string Location { get; }
        string DisplayName { get; }

        /// <summary>Относительные пути всех файлов (разделитель '\').</summary>
        IReadOnlyList<string> Files { get; }

        byte[] Read(string relativePath);

        /// <summary>Заменяет содержимое существующего файла. После вызова Files может обновиться.</summary>
        void Write(string relativePath, byte[] data);
    }
}
