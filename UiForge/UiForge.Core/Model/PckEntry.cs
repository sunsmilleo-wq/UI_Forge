namespace UiForge.Core.Model
{
    /// <summary>
    /// Одна запись в архиве PCK — файл или папка.
    /// Не зависит от конкретной реализации движка (WinPck / Native).
    /// </summary>
    public sealed class PckEntry
    {
        /// <summary>Полный путь внутри архива, например "Window\\back.tga".</summary>
        public string RelativePath { get; init; } = string.Empty;

        /// <summary>Отображаемое имя (последний сегмент пути).</summary>
        public string Name { get; init; } = string.Empty;

        public bool IsDirectory { get; init; }

        /// <summary>Размер файла после распаковки, байт. 0 для папок.</summary>
        public long OriginalSize { get; init; }

        /// <summary>Размер файла в архиве (как хранится), байт. 0 для папок.</summary>
        public long CompressedSize { get; init; }

        /// <summary>Смещение данных файла внутри архива, если применимо.</summary>
        public long Offset { get; init; }

        /// <summary>Дочерние записи — только для папок.</summary>
        public IReadOnlyList<PckEntry> Children { get; init; } = Array.Empty<PckEntry>();

        /// <summary>
        /// Внутренний кэш нативного указателя узла (используется WinPckEngine,
        /// чтобы не искать узел заново по пути при извлечении/удалении).
        /// Для будущего NativePckEngine всегда будет IntPtr.Zero — там это не нужно.
        /// </summary>
        public IntPtr NativeNodeCache { get; init; } = IntPtr.Zero;
    }
}
