using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace UiForge.UiFormat
{
    /// <summary>
    /// Разобранный файл интерфейса: корневой DIALOG + плоский список его прямых
    /// дочерних контролов известных типов. XElement остаётся источником истины —
    /// правки (drag/resize на холсте, панель свойств, текстовый редактор) пишутся
    /// прямо в него, поэтому всё, что схема не знает (незнакомые атрибуты, порядок,
    /// комментарии), сохраняется как есть при обратной сериализации.
    /// </summary>
    public sealed class UiDialogDocument
    {
        public XDocument Document { get; }
        public UiNode Root { get; }
        public List<UiNode> Children { get; }

        private UiDialogDocument(XDocument doc, UiNode root, List<UiNode> children)
        {
            Document = doc;
            Root = root;
            Children = children;
        }

        /// <summary>Разбирает текст XML. Бросает InvalidDataException, если это не файл интерфейса.</summary>
        public static UiDialogDocument Parse(string xmlText)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Parse(UiXmlSanitizer.Sanitize(xmlText), LoadOptions.PreserveWhitespace);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Не удалось разобрать XML: {ex.Message}", ex);
            }

            if (doc.Root is null)
                throw new InvalidDataException("В файле нет корневого элемента.");

            string rootTag = doc.Root.Name.LocalName.ToUpperInvariant();
            if (!UiControlSchema.ByTag.TryGetValue(rootTag, out var rootSchema))
            {
                throw new InvalidDataException(
                    $"Это не похоже на файл интерфейса — неизвестный корневой тег \"{doc.Root.Name.LocalName}\". " +
                    "Дизайнер понимает файлы, где корень — DIALOG.");
            }

            var root = new UiNode(doc.Root, rootSchema);

            var children = new List<UiNode>();
            foreach (var el in doc.Root.Elements())
            {
                string tag = el.Name.LocalName.ToUpperInvariant();
                if (UiControlSchema.ByTag.TryGetValue(tag, out var schema) && schema.HasPosition)
                    children.Add(new UiNode(el, schema));
            }

            return new UiDialogDocument(doc, root, children);
        }

        /// <summary>Создаёт новый узел выбранного типа с координатами по умолчанию и добавляет его в конец.</summary>
        public UiNode AddChild(string tag, double x, double y, double width, double height)
        {
            if (!UiControlSchema.ByTag.TryGetValue(tag, out var schema) || !schema.HasPosition)
                throw new InvalidOperationException($"Тип \"{tag}\" нельзя добавить как элемент интерфейса.");

            var element = new XElement(tag);
            AppendFormatted(element);
            var node = new UiNode(element, schema);
            node.SetGeometry(x, y, width, height);
            Children.Add(node);
            return node;
        }

        public void RemoveChild(UiNode node)
        {
            node.Element.Remove();
            Children.Remove(node);
        }

        /// <summary>Добавляет элемент в конец корня с тем же отступом/переводом строки, что у соседей,
        /// чтобы новый тег не «прилипал» к соседнему в одну строку.</summary>
        private void AppendFormatted(XElement element)
        {
            var root = Document.Root!;
            string indent = (root.Elements().FirstOrDefault()?.PreviousNode as XText)?.Value ?? "\n\t";
            if (string.IsNullOrEmpty(indent) || !indent.Contains('\n')) indent = "\n\t";

            if (root.LastNode is XText last && string.IsNullOrWhiteSpace(last.Value))
                last.AddBeforeSelf(new XText(indent), element);
            else
                root.Add(new XText(indent), element, new XText(indent.Contains("\r\n") ? "\r\n" : "\n"));
        }

        /// <summary>Клонирует узел (глубокая копия XElement) и добавляет копию рядом с оригиналом.</summary>
        public UiNode Duplicate(UiNode source)
        {
            var clone = new XElement(source.Element);
            AppendFormatted(clone);
            var node = new UiNode(clone, source.Schema);
            Children.Add(node);
            return node;
        }

        /// <summary>Сериализует документ обратно в текст, сохраняя XML-декларацию, если она была.</summary>
        public string ToXmlString()
        {
            string body = Document.ToString(SaveOptions.None);
            string text = Document.Declaration is null ? body : Document.Declaration + "\n" + body;
            return UiXmlSanitizer.Restore(text);
        }
    }
}
