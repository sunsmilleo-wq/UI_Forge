namespace UiForge.UiFormat
{
    /// <summary>Тип редактора, который должна показать панель свойств для значения.</summary>
    public enum UiPropertyKind
    {
        /// <summary>Число с плавающей точкой (X, Y, Width, Height, FontSize...).</summary>
        Number,

        /// <summary>Произвольная строка (Name, Text...).</summary>
        Text,

        /// <summary>Цвет — хранится как есть (строка), но показывается с цветным образцом.</summary>
        Color,

        /// <summary>Путь к файлу-картинке внутри архива — показывается превью + кнопка "Обзор...".</summary>
        ImageFile
    }
}
