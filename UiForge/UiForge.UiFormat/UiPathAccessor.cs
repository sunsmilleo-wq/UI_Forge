using System.Linq;
using System.Xml.Linq;

namespace UiForge.UiFormat
{
    /// <summary>
    /// Путь вида "Resource/FrameImage/@FileName" или "@x":
    ///   - сегменты разделены '/';
    ///   - последний сегмент, начинающийся с '@' — имя атрибута на найденном элементе;
    ///   - промежуточный сегмент — имя дочернего элемента (при Set создаётся, если его нет);
    ///   - промежуточный сегмент вида "#N" — N-й (с нуля) дочерний элемент по счёту,
    ///     независимо от имени тега (нужно для некоторых узлов вида "Resource/Image",
    ///     где реальное имя вложенного тега не фиксировано в схеме).
    /// </summary>
    public static class UiPathAccessor
    {
        public static string? Get(XElement root, string path)
        {
            var segments = path.Split('/');
            XElement? current = root;

            for (int i = 0; i < segments.Length - 1 && current != null; i++)
                current = ResolveChild(current, segments[i], create: false);

            if (current == null) return null;

            string last = segments[^1];
            if (last.StartsWith('@'))
                return Restore(FindAttr(current, last[1..])?.Value);

            // Путь без "@" в конце — значит интересует сам элемент (например, наличие).
            return Restore(current.Element(last)?.Value);
        }

        public static void Set(XElement root, string path, string value)
        {
            var segments = path.Split('/');
            XElement current = root;

            for (int i = 0; i < segments.Length - 1; i++)
                current = ResolveChild(current, segments[i], create: true)!;

            string last = segments[^1];
            if (last.StartsWith('@'))
            {
                var existingAttr = FindAttr(current, last[1..]);
                if (existingAttr != null) existingAttr.Value = value;
                else current.SetAttributeValue(last[1..], value);
            }
            else
            {
                var child = current.Element(last);
                if (child == null)
                {
                    child = new XElement(last);
                    current.Add(child);
                }
                child.Value = value;
            }
        }

        /// <summary>Есть ли по пути реальное значение (используется, чтобы прятать в панели
        /// свойств необязательные блоки — например картинку, которой в этом узле нет).</summary>
        /// <summary>В файлах встречаются width/height/align в нижнем регистре — ищем атрибут без учёта регистра.</summary>
        private static XAttribute? FindAttr(XElement e, string name) =>
            e.Attribute(name) ?? e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, System.StringComparison.OrdinalIgnoreCase));

        private static string? Restore(string? v) => v is null ? null : UiXmlSanitizer.Restore(v);

        public static bool Exists(XElement root, string path) => Get(root, path) != null;

        private static XElement? ResolveChild(XElement parent, string segment, bool create)
        {
            if (segment.StartsWith('#'))
            {
                // "#N" — N-й дочерний элемент; "#N|Имя" — то же, но при создании назвать его "Имя".
                string idxPart = segment[1..];
                string createName = "Item";
                int bar = idxPart.IndexOf('|');
                if (bar >= 0) { createName = idxPart[(bar + 1)..]; idxPart = idxPart[..bar]; }

                if (int.TryParse(idxPart, out int index) && index >= 0)
                {
                    var children = parent.Elements().ToList();
                    if (index < children.Count) return children[index];
                    if (!create) return null;

                    XElement? createdIndexed = null;
                    for (int i = children.Count; i <= index; i++)
                    {
                        createdIndexed = new XElement(createName);
                        parent.Add(createdIndexed);
                    }
                    return createdIndexed;
                }
            }

            var existing = parent.Element(segment);
            if (existing != null || !create) return existing;

            var createdByName = new XElement(segment);
            parent.Add(createdByName);
            return createdByName;
        }
    }
}
