using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace UiForge.App
{
    /// <summary>Подсвечивает в редакторе кода фрагменты XML выбранных элементов и элемента под курсором на холсте.</summary>
    public sealed class ElementHighlightRenderer : IBackgroundRenderer
    {
        private static readonly Brush SelectedBrush = Freeze(new SolidColorBrush(Color.FromArgb(70, 0x7C, 0x6F, 0xF0)));
        private static readonly Brush HoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(38, 0xF2, 0xC9, 0x4C)));
        private static readonly Pen SelectedPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(200, 0x91, 0x86, 0xF4)), 1));

        public List<(int Start, int End)> Selected { get; } = new();
        public (int Start, int End)? Hover { get; set; }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document == null || !textView.VisualLinesValid) return;

            if (Hover is { } h) Paint(textView, drawingContext, h, HoverBrush, null);
            foreach (var s in Selected) Paint(textView, drawingContext, s, SelectedBrush, SelectedPen);
        }

        private static void Paint(TextView textView, DrawingContext dc, (int Start, int End) range, Brush brush, Pen? pen)
        {
            int len = textView.Document.TextLength;
            int start = System.Math.Clamp(range.Start, 0, len);
            int end = System.Math.Clamp(range.End, start, len);
            if (end <= start) return;

            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 3 };
            builder.AddSegment(textView, new TextSegment { StartOffset = start, EndOffset = end });
            var geometry = builder.CreateGeometry();
            if (geometry != null) dc.DrawGeometry(brush, pen, geometry);
        }

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
        private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
    }
}
