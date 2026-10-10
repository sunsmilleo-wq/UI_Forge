using System.Globalization;
using System.Xml.Linq;

namespace UiForge.UiFormat
{
    /// <summary>Один визуальный узел интерфейса — обёртка над XElement + его схемой.</summary>
    public sealed class UiNode
    {
        public XElement Element { get; }
        public UiControlSchema Schema { get; }

        public UiNode(XElement element, UiControlSchema schema)
        {
            Element = element;
            Schema = schema;
        }

        public string Tag => Schema.Tag;
        public string DisplayName => Schema.DisplayName;

        public string Name => Element.Attribute("Name")?.Value ?? "(без имени)";

        /// <summary>Готовая подпись для списков элементов в UI дизайнера, например "Надпись — btnClose".</summary>
        public string ListLabel => IsOffScreen ? $"{DisplayName} — {Name}  (скрыт)" : $"{DisplayName} — {Name}";

        /// <summary>Заготовки, которые игра прячет за экраном (x или y = ±10000): на холсте их не видно.</summary>
        public bool IsOffScreen => Schema.HasPosition && (System.Math.Abs(X) >= 5000 || System.Math.Abs(Y) >= 5000);

        public double X => Schema.HasPosition ? GetDouble(Schema.XPath) : 0;
        public double Y => Schema.HasPosition ? GetDouble(Schema.YPath) : 0;
        public double Width => GetDouble(Schema.WidthPath);
        public double Height => GetDouble(Schema.HeightPath);

        public void SetGeometry(double x, double y, double width, double height)
        {
            if (Schema.HasPosition)
            {
                SetDouble(Schema.XPath, x);
                SetDouble(Schema.YPath, y);
            }
            SetDouble(Schema.WidthPath, width);
            SetDouble(Schema.HeightPath, height);
        }

        public string? MainImageFile
        {
            get
            {
                if (Schema.MainImagePath is null) return null;
                var v = UiPathAccessor.Get(Element, Schema.MainImagePath);
                if (!string.IsNullOrWhiteSpace(v)) return v;
                foreach (var alt in Schema.AlternateImagePaths)
                {
                    v = UiPathAccessor.Get(Element, alt);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
                return v;
            }
        }

        /// <summary>0 — слева, 1 — по центру, 2 — справа.</summary>
        public int TextAlign
        {
            get
            {
                if (Schema.TextAlignPath is null) return Schema.TextAlignDefault;
                var raw = UiPathAccessor.Get(Element, Schema.TextAlignPath);
                return int.TryParse(raw, out var v) && v is >= 0 and <= 2 ? v : Schema.TextAlignDefault;
            }
        }

        public bool TextBold
        {
            get
            {
                if (Schema.TextBoldPath is null) return false;
                var raw = UiPathAccessor.Get(Element, Schema.TextBoldPath);
                return raw != null && (raw.Equals("true", System.StringComparison.OrdinalIgnoreCase) || raw == "1" || raw == "255");
            }
        }

        public string? TextValue => Schema.TextValuePath is null ? null : UiPathAccessor.Get(Element, Schema.TextValuePath);
        public string? TextColor => Schema.TextColorPath is null ? null : UiPathAccessor.Get(Element, Schema.TextColorPath);
        public double? TextFontSize
        {
            get
            {
                if (Schema.TextFontSizePath is null) return null;
                var raw = UiPathAccessor.Get(Element, Schema.TextFontSizePath);
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            }
        }

        public string? GetValue(UiPropertyDef prop) => UiPathAccessor.Get(Element, prop.Path);

        public void SetValue(UiPropertyDef prop, string value) => UiPathAccessor.Set(Element, prop.Path, value);

        private double GetDouble(string path)
        {
            var raw = UiPathAccessor.Get(Element, path);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private void SetDouble(string path, double value)
        {
            // Не трогаем то, что не менялось: у многих надписей нет Width/Height (размер по тексту),
            // и записывать им Width="0" значило бы менять файл без причины.
            if (System.Math.Abs(GetDouble(path) - value) < 0.0005) return;
            UiPathAccessor.Set(Element, path, value.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }
}
