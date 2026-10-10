namespace UiForge.UiFormat
{
    /// <summary>Описание одного редактируемого свойства контрола для панели свойств.</summary>
    public sealed class UiPropertyDef
    {
        /// <summary>Подпись, которую видит пользователь ("Ширина", "Цвет текста"...).</summary>
        public required string Label { get; init; }

        /// <summary>Путь внутри XML-узла (см. UiPathAccessor).</summary>
        public required string Path { get; init; }

        public required UiPropertyKind Kind { get; init; }

        /// <summary>Группа для визуального объединения в панели (например "Геометрия", "Текст", "Картинки").</summary>
        public string Group { get; init; } = "Общее";
    }
}
