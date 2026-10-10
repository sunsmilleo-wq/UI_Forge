using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using UiForge.UiFormat;

namespace UiForge.App
{
    /// <summary>
    /// Двусторонняя связь холста и кода: выделил/навёл на холсте — подсветилось в коде;
    /// потянул элемент — код меняется на лету; правишь код — холст обновляется сам.
    /// </summary>
    public partial class MainWindow
    {
        private readonly ElementHighlightRenderer _highlight = new();
        private readonly DispatcherTimer _codeDebounce = new() { Interval = TimeSpan.FromMilliseconds(650) };
        private bool _settingEditorText;
        private bool _syncingCaret;

        /// <summary>Позиции (начало, конец) в тексте для каждого дочернего элемента корня, по порядку.</summary>
        private readonly List<(int Start, int End)> _lineMap = new();

        private void InitCodeEditor()
        {
            ApplyXmlTheme();
            XmlEditor.TextArea.TextView.BackgroundRenderers.Add(_highlight);
            XmlEditor.TextChanged += XmlEditor_TextChanged;
            XmlEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
            _codeDebounce.Tick += CodeDebounce_Tick;

            XmlEditor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(0x5B, 0x5D, 0x70));
            XmlEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(90, 0x7C, 0x6F, 0xF0));
            XmlEditor.TextArea.SelectionForeground = null;
            XmlEditor.TextArea.SelectionBorder = null;
        }

        private void ApplyXmlTheme()
        {
            try { ApplyXmlThemeCore(); } catch { /* тема — косметика, без неё тоже работает */ }
        }

        private void ApplyXmlThemeCore()
        {
            var def = HighlightingManager.Instance.GetDefinition("XML");
            if (def == null) return;

            void C(string name, string hex)
            {
                var color = def.GetNamedColor(name);
                if (color != null)
                    color.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(hex));
            }

            C("XmlTag", "#C792EA");
            C("AttributeName", "#82AAFF");
            C("AttributeValue", "#C3E88D");
            C("Comment", "#6C7086");
            C("XmlDeclaration", "#8E8FA3");
            C("Entity", "#F78C6C");
            C("CData", "#F78C6C");
            C("DocType", "#8E8FA3");
            XmlEditor.SyntaxHighlighting = def;
        }

        // ---------- запись текста в редактор ----------

        private void SetEditorText(string text, bool keepScroll = true)
        {
            double offset = XmlEditor.VerticalOffset;
            int caret = XmlEditor.CaretOffset;

            _settingEditorText = true;
            XmlEditor.Text = text;
            XmlEditor.CaretOffset = Math.Min(caret, text.Length);
            _settingEditorText = false;

            RebuildLineMap(text);

            if (keepScroll)
                Dispatcher.BeginInvoke(new Action(() => XmlEditor.ScrollToVerticalOffset(offset)), DispatcherPriority.Loaded);

            if (_document != null) UpdateCodeHighlight(false);
        }

        private DateTime _lastLiveCodeUpdate = DateTime.MinValue;

        /// <summary>Быстрое обновление кода во время перетаскивания/resize (без пересборки холста).
        /// Троттлинг — иначе на каждое из десятков MouseMove в секунду пришлось бы заново
        /// сериализовать документ и перестраивать подсветку в AvalonEdit, и на больших файлах
        /// перетаскивание начало бы подтормаживать.</summary>
        private void LiveCodeUpdate(bool force = false)
        {
            if (_document is null) return;
            var now = DateTime.UtcNow;
            if (!force && (now - _lastLiveCodeUpdate).TotalMilliseconds < 70) return;
            _lastLiveCodeUpdate = now;

            SetEditorText(_document.ToXmlString());
            _dirty = true;
            UpdateTitle();
        }

        private void RebuildLineMap(string text)
        {
            _lineMap.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                var doc = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
                var root = doc.Root;
                if (root is null) return;

                var textDoc = XmlEditor.Document;
                var elements = root.Elements().ToList();
                var starts = new List<int>();

                foreach (var el in elements)
                {
                    var info = (IXmlLineInfo)el;
                    if (!info.HasLineInfo() || info.LineNumber < 1 || info.LineNumber > textDoc.LineCount)
                    {
                        starts.Add(-1);
                        continue;
                    }
                    int off = textDoc.GetOffset(info.LineNumber, Math.Max(1, info.LinePosition));
                    // LinePosition указывает на имя тега, нам нужен сам '<'
                    while (off > 0 && off < text.Length && text[off] != '<') off--;
                    starts.Add(off);
                }

                int rootEnd = text.LastIndexOf("</", StringComparison.Ordinal);
                if (rootEnd < 0) rootEnd = text.Length;

                for (int i = 0; i < starts.Count; i++)
                {
                    if (starts[i] < 0) { _lineMap.Add((-1, -1)); continue; }

                    int end = i + 1 < starts.Count && starts[i + 1] >= 0 ? starts[i + 1] : rootEnd;
                    while (end > starts[i] && char.IsWhiteSpace(text[end - 1])) end--;
                    _lineMap.Add((starts[i], end));
                }
            }
            catch
            {
                _lineMap.Clear(); // XML сейчас невалиден — подсветки не будет, пока не исправят
            }
        }

        private bool TryGetRange(UiNode node, out (int Start, int End) range)
        {
            range = (-1, -1);
            if (_document?.Document.Root is null) return false;

            int idx = _document.Document.Root.Elements().ToList().IndexOf(node.Element);
            if (idx < 0 || idx >= _lineMap.Count) return false;

            range = _lineMap[idx];
            return range.Start >= 0 && range.End > range.Start;
        }

        // ---------- подсветка выбранного / hover ----------

        private void UpdateCodeHighlight(bool scroll)
        {
            _highlight.Selected.Clear();
            foreach (var n in _selection)
                if (TryGetRange(n, out var r)) _highlight.Selected.Add(r);

            _highlight.Hover = _hoverNode != null && !IsSel(_hoverNode) && TryGetRange(_hoverNode, out var h) ? h : null;
            XmlEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

            if (scroll && PrimaryNode != null && TryGetRange(PrimaryNode, out var pr))
                ScrollCodeTo(pr.Start);
        }

        private void ScrollCodeTo(int offset)
        {
            var textDoc = XmlEditor.Document;
            if (textDoc.TextLength == 0) return;

            int line = textDoc.GetLineByOffset(Math.Min(offset, textDoc.TextLength)).LineNumber;
            var tv = XmlEditor.TextArea.TextView;

            if (tv.VisualLinesValid && tv.VisualLines.Count > 0)
            {
                int first = tv.VisualLines.First().FirstDocumentLine.LineNumber;
                int last = tv.VisualLines.Last().LastDocumentLine.LineNumber;
                if (line >= first + 1 && line <= last - 1) return; // и так на виду
            }
            XmlEditor.ScrollToLine(Math.Max(1, line - 2));
        }

        // ---------- клик в коде → выделение на холсте ----------

        private void Caret_PositionChanged(object? sender, EventArgs e)
        {
            if (_settingEditorText || _syncingCaret || _document?.Document.Root is null) return;
            if (!XmlEditor.TextArea.IsKeyboardFocusWithin) return;

            int off = XmlEditor.CaretOffset;
            var elements = _document.Document.Root.Elements().ToList();

            for (int i = 0; i < _lineMap.Count && i < elements.Count; i++)
            {
                var (s, en) = _lineMap[i];
                if (s < 0 || off < s || off > en) continue;

                var node = _document.Children.FirstOrDefault(n => ReferenceEquals(n.Element, elements[i]));
                if (node == null) return;
                if (_selection.Count == 1 && ReferenceEquals(_selection[0].Element, node.Element)) return;

                _syncingCaret = true;
                _selection.Clear();
                _selection.Add(node);
                ApplySelectionChange(scrollCode: false);
                _syncingCaret = false;
                return;
            }
        }

        // ---------- правка кода → холст ----------

        private void XmlEditor_TextChanged(object? sender, EventArgs e)
        {
            if (_settingEditorText || _currentFile is null) return;
            _dirty = true;
            UpdateTitle();
            _codeDebounce.Stop();
            _codeDebounce.Start();
        }

        private void CodeDebounce_Tick(object? sender, EventArgs e)
        {
            _codeDebounce.Stop();
            if (_currentFile is null) return;

            string text = XmlEditor.Text;
            UiDialogDocument parsed;
            try
            {
                parsed = UiDialogDocument.Parse(text);
            }
            catch (Exception ex)
            {
                CodeStatus.Text = "⚠ " + FirstLine(ex.Message);
                CodeStatus.Foreground = (Brush)FindResource("Warn");
                return;
            }

            CodeStatus.Text = "✔ XML корректен";
            CodeStatus.Foreground = (Brush)FindResource("Good");

            if (_document != null && parsed.ToXmlString() == _document.ToXmlString())
            {
                RebuildLineMap(text);
                UpdateCodeHighlight(false);
                return;
            }

            bool wasRaw = _document is null;
            var old = _document;

            // выделение переносим по номерам элементов — узлы в новом документе другие объекты
            var indexes = old == null
                ? new List<int>()
                : _selection.Select(s => old.Children.FindIndex(c => ReferenceEquals(c.Element, s.Element)))
                            .Where(i => i >= 0).ToList();

            if (old != null) PushUndoState(old.ToXmlString());
            _document = parsed;
            _rawMode = false;
            _selection.Clear();
            foreach (int i in indexes)
                if (i < parsed.Children.Count) _selection.Add(parsed.Children[i]);
            _hoverNode = null;
            _dirty = true;

            RebuildLineMap(text);
            RenderCanvas();
            OnSelectionChanged(false);
            UpdateButtons();
            UpdateTitle();
            StatusText.Text = wasRaw ? "Распознано как интерфейс — холст включён (не сохранено)."
                                     : "Код применён (не сохранено).";
        }

        private static string FirstLine(string s)
        {
            int n = s.IndexOfAny(new[] { '\r', '\n' });
            return n < 0 ? s : s[..n];
        }
    }
}
