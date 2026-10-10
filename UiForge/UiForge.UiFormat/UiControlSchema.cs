using System.Collections.Generic;

namespace UiForge.UiFormat
{
    /// <summary>
    /// Описывает один тип узла интерфейса (тег XML): где у него находятся
    /// геометрия/картинка/текст и какие свойства показывать в панели.
    /// Чтобы научить дизайнер новому тегу — достаточно добавить сюда запись,
    /// без единой строчки кода парсинга (в отличие от UIEdit, где на каждый
    /// тег был свой блок ручного разбора XmlDocument).
    /// </summary>
    public sealed class UiControlSchema
    {
        public required string Tag { get; init; }

        /// <summary>Понятное название для панели свойств и дерева ("Надпись", "Кнопка"...).</summary>
        public required string DisplayName { get; init; }

        /// <summary>Есть ли x/y (у корневого DIALOG их нет — он всегда в начале координат).</summary>
        public bool HasPosition { get; init; } = true;

        public string XPath { get; init; } = "@x";
        public string YPath { get; init; } = "@y";
        public string WidthPath { get; init; } = "@Width";
        public string HeightPath { get; init; } = "@Height";

        /// <summary>Путь к главной картинке, которую рисуем как фон контрола на холсте (если есть).</summary>
        public string? MainImagePath { get; init; }

        /// <summary>Путь к тексту, который рисуем поверх контрола (если есть).</summary>
        public string? TextValuePath { get; init; }
        public string? TextColorPath { get; init; }
        public string? TextFontSizePath { get; init; }
        public string? TextBoldPath { get; init; }

        /// <summary>Атрибут выравнивания текста (0 — слева, 1 — по центру, 2 — справа), если он есть у элемента.</summary>
        public string? TextAlignPath { get; init; }
        /// <summary>Выравнивание, если атрибута нет: 0 слева, 1 по центру (кнопки).</summary>
        public int TextAlignDefault { get; init; }
        public bool TextVCenter { get; init; }
        public bool TextWrap { get; init; }

        /// <summary>Запасные пути картинок, если главной у элемента нет (например, у PROGRESS нет FrameImage — только FillImage).</summary>
        public string[] AlternateImagePaths { get; init; } = System.Array.Empty<string>();

        /// <summary>Свойства, показываемые в панели справа (без геометрии — та формируется отдельно).</summary>
        public required List<UiPropertyDef> Properties { get; init; }

        public static readonly Dictionary<string, UiControlSchema> ByTag = new()
        {
            ["DIALOG"] = new UiControlSchema
            {
                Tag = "DIALOG",
                DisplayName = "Окно",
                HasPosition = false,
                MainImagePath = "Resource/FrameImage/@FileName",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Фон окна", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["LABEL"] = new UiControlSchema
            {
                Tag = "LABEL",
                DisplayName = "Надпись",
                TextValuePath = "Text/@String",
                TextColorPath = "Text/@Color",
                TextFontSizePath = "Text/@FontSize",
                TextBoldPath = "Text/@Bold",
                TextAlignPath = "@Align",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Выравнивание (0 лево, 1 центр, 2 право)", Path = "@Align", Kind = UiPropertyKind.Number, Group = "Общее" },
                    new() { Label = "Текст", Path = "Text/@String", Kind = UiPropertyKind.Text, Group = "Текст" },
                    new() { Label = "Размер шрифта", Path = "Text/@FontSize", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Цвет текста", Path = "Text/@Color", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Цвет обводки", Path = "Text/@OutlineColor", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Цвет (верх градиента)", Path = "Text/@TextUpperColor", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Цвет (низ градиента)", Path = "Text/@TextLowerColor", Kind = UiPropertyKind.Color, Group = "Текст" },
                }
            },

            ["EDIT"] = new UiControlSchema
            {
                Tag = "EDIT",
                DisplayName = "Поле ввода",
                MainImagePath = "Resource/FrameImage/@FileName",
                TextVCenter = true,
                TextValuePath = "Text/@String",
                TextColorPath = "Text/@Color",
                TextFontSizePath = "Text/@FontSize",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Текст по умолчанию", Path = "Text/@String", Kind = UiPropertyKind.Text, Group = "Текст" },
                    new() { Label = "Размер шрифта", Path = "Text/@FontSize", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Цвет текста", Path = "Text/@Color", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Рамка", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["IMAGEPICTURE"] = new UiControlSchema
            {
                Tag = "IMAGEPICTURE",
                DisplayName = "Картинка",
                // В исходном формате имя вложенного тега с FileName не фиксировано —
                // берём первый дочерний элемент первого дочернего элемента, как и
                // референсный UIEdit.
                MainImagePath = "Resource/#0|FrameImage/@FileName",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Картинка", Path = "Resource/#0|FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["SCROLL"] = new UiControlSchema
            {
                Tag = "SCROLL",
                DisplayName = "Полоса прокрутки",
                MainImagePath = "Resource/BarFrameImage/@FileName",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Фон полосы", Path = "Resource/BarFrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Бегунок", Path = "Resource/ScrollImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Кнопка вверх", Path = "Resource/UpImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Кнопка вниз", Path = "Resource/DownImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["RADIO"] = new UiControlSchema
            {
                Tag = "RADIO",
                DisplayName = "Переключатель",
                MainImagePath = "Resource/NormalImage/@FileName",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Подсказка", Path = "Hint/@String", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Обычное состояние", Path = "Resource/NormalImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Отмечено", Path = "Resource/CheckedImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["STILLIMAGEBUTTON"] = new UiControlSchema
            {
                Tag = "STILLIMAGEBUTTON",
                DisplayName = "Кнопка",
                MainImagePath = "Resource/FrameUpImage/@FileName",
                TextAlignDefault = 1,
                TextVCenter = true,
                TextBoldPath = "Text/@Bold",
                TextValuePath = "Text/@String",
                TextColorPath = "Text/@Color",
                TextFontSizePath = "Text/@FontSize",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Подсказка", Path = "Hint/@String", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Текст", Path = "Text/@String", Kind = UiPropertyKind.Text, Group = "Текст" },
                    new() { Label = "Размер шрифта", Path = "Text/@FontSize", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Цвет текста", Path = "Text/@Color", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Цвет обводки", Path = "Text/@OutlineColor", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Внутренний цвет", Path = "Text/@InnerColor", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Обычное состояние", Path = "Resource/FrameUpImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Нажатое", Path = "Resource/FrameDownImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "При наведении", Path = "Resource/FrameOnHoverImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["CHECK"] = new UiControlSchema
            {
                Tag = "CHECK",
                DisplayName = "Флажок",
                MainImagePath = "Resource/NormalImage/@FileName",
                TextVCenter = true,
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Команда", Path = "@Command", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Подсказка", Path = "Hint/@String", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Звук клика", Path = "@SoundEffect", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Обычное", Path = "Resource/NormalImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Отмечено", Path = "Resource/CheckedImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "При наведении", Path = "Resource/OnHoverImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Отмечено + наведение", Path = "Resource/CheckedOnHoverImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["TEXT"] = new UiControlSchema
            {
                Tag = "TEXT",
                DisplayName = "Многострочный текст",
                MainImagePath = "Resource/FrameImage/@FileName",
                TextValuePath = "Text/@String",
                TextColorPath = "Text/@Color",
                TextFontSizePath = "Text/@FontSize",
                TextBoldPath = "Text/@Bold",
                TextWrap = true,
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Текст", Path = "Text/@String", Kind = UiPropertyKind.Text, Group = "Текст" },
                    new() { Label = "Размер шрифта", Path = "Text/@FontSize", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Цвет текста", Path = "Text/@Color", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Межстрочный интервал", Path = "@LineSpace", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Фон", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Кнопка вверх", Path = "Resource/UpImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Кнопка вниз", Path = "Resource/DownImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Бегунок", Path = "Resource/ScrollImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Полоса", Path = "Resource/BarImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["LIST"] = new UiControlSchema
            {
                Tag = "LIST",
                DisplayName = "Список",
                MainImagePath = "Resource/FrameImage/@FileName",
                TextFontSizePath = "Text/@FontSize",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Команда", Path = "@Command", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Колонки (MultiLine)", Path = "@MultiLine", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Межстрочный интервал", Path = "@LineSpace", Kind = UiPropertyKind.Number, Group = "Общее" },
                    new() { Label = "Размер шрифта", Path = "Text/@FontSize", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Цвет текста", Path = "Text/@Color", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Фон", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Подсветка строки", Path = "Resource/HilightImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Кнопка вверх", Path = "Resource/UpImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Кнопка вниз", Path = "Resource/DownImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Бегунок", Path = "Resource/ScrollImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Полоса", Path = "Resource/BarImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Иконка строки", Path = "Resource/ItemImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["PROGRESS"] = new UiControlSchema
            {
                Tag = "PROGRESS",
                DisplayName = "Индикатор",
                MainImagePath = "Resource/FrameImage/@FileName",
                AlternateImagePaths = new[] { "Resource/FillImage/@FileName" },
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Подсказка", Path = "Hint/@String", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Рамка", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Заполнение", Path = "Resource/FillImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Позиция", Path = "Resource/PosImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["COMBO"] = new UiControlSchema
            {
                Tag = "COMBO",
                DisplayName = "Выпадающий список",
                MainImagePath = "Resource/FrameImage/@FileName",
                TextVCenter = true,
                TextFontSizePath = "Text/@FontSize",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Имя списка (ListBoxName)", Path = "@ListBoxName", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Команда", Path = "@Command", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Подсказка", Path = "Hint/@String", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Размер шрифта", Path = "Text/@FontSize", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Цвет текста", Path = "Text/@Color", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Рамка", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Подсветка", Path = "Resource/HilightImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Рамка раскрытого", Path = "Resource/FrameExpandImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["SLIDER"] = new UiControlSchema
            {
                Tag = "SLIDER",
                DisplayName = "Ползунок",
                MainImagePath = "Resource/FrameImage/@FileName",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Уровней", Path = "@TotalLevels", Kind = UiPropertyKind.Number, Group = "Общее" },
                    new() { Label = "Команда", Path = "@Command", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Подсказка", Path = "Hint/@String", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Желоб", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Бегунок", Path = "Resource/BarImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["SUBDIALOG"] = new UiControlSchema
            {
                Tag = "SUBDIALOG",
                DisplayName = "Вложенное окно",
                MainImagePath = "Resource/FrameImage/@FileName",
                TextValuePath = "@DialogFile",
                TextAlignDefault = 1,
                TextVCenter = true,
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Файл окна", Path = "@DialogFile", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Фон", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Вверх", Path = "Resource/UpImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Вниз", Path = "Resource/DownImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Бегунок верт.", Path = "Resource/VScrollImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Полоса верт.", Path = "Resource/VBarFrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Влево", Path = "Resource/LeftImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Вправо", Path = "Resource/RightImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Бегунок гор.", Path = "Resource/HScrollImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Полоса гор.", Path = "Resource/HBarFrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["TREE"] = new UiControlSchema
            {
                Tag = "TREE",
                DisplayName = "Дерево",
                MainImagePath = "Resource/FrameImage/@FileName",
                TextFontSizePath = "Text/@FontSize",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Отступ", Path = "@Indent", Kind = UiPropertyKind.Number, Group = "Общее" },
                    new() { Label = "Размер шрифта", Path = "Text/@FontSize", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Цвет текста", Path = "Text/@Color", Kind = UiPropertyKind.Color, Group = "Текст" },
                    new() { Label = "Фон", Path = "Resource/FrameImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Подсветка", Path = "Resource/HilightImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Вверх", Path = "Resource/UpImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Вниз", Path = "Resource/DownImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Бегунок", Path = "Resource/ScrollImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Полоса", Path = "Resource/BarImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Плюс", Path = "Resource/PlusSymbolImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Минус", Path = "Resource/MinusSymbolImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                    new() { Label = "Лист", Path = "Resource/LeafSymbolImage/@FileName", Kind = UiPropertyKind.ImageFile, Group = "Картинки" },
                }
            },

            ["WINDOWPICTURE"] = new UiControlSchema
            {
                Tag = "WINDOWPICTURE",
                DisplayName = "Рамка окна",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                }
            },

            ["VERTICALTEXT"] = new UiControlSchema
            {
                Tag = "VERTICALTEXT",
                DisplayName = "Вертикальный текст",
                TextValuePath = "Text/@String",
                TextColorPath = "Text/@Color",
                TextFontSizePath = "Text/@FontSize",
                TextAlignPath = "@Align",
                Properties = new List<UiPropertyDef>
                {
                    new() { Label = "Имя", Path = "@Name", Kind = UiPropertyKind.Text, Group = "Общее" },
                    new() { Label = "Выравнивание", Path = "@Align", Kind = UiPropertyKind.Number, Group = "Общее" },
                    new() { Label = "Текст", Path = "Text/@String", Kind = UiPropertyKind.Text, Group = "Текст" },
                    new() { Label = "Размер шрифта", Path = "Text/@FontSize", Kind = UiPropertyKind.Number, Group = "Текст" },
                    new() { Label = "Цвет текста", Path = "Text/@Color", Kind = UiPropertyKind.Color, Group = "Текст" },
                }
            },
        };
    }
}
