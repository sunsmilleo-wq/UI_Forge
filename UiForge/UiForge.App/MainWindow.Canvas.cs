using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UiForge.UiFormat;

namespace UiForge.App
{
    /// <summary>Холст: рисование, выделение (в т.ч. нескольких), перетаскивание, resize, прилипание, выравнивание.</summary>
    public partial class MainWindow
    {
        private readonly List<UiNode> _selection = new();
        private UiNode? _hoverNode;
        private UiNode? PrimaryNode => _selection.Count > 0 ? _selection[^1] : null;

        private static readonly FontFamily TextFont = new("Microsoft YaHei UI, Microsoft YaHei, SimSun, Segoe UI");

        private readonly HashSet<string> _missingImages = new(StringComparer.OrdinalIgnoreCase);

        // перетаскивание
        private bool _dragging, _dragMoved, _collapseOnUp;
        private Point _dragStartMouse;
        private Dictionary<UiNode, (double X, double Y)> _dragStarts = new();
        private UiNode? _dragNode;
        private FrameworkElement? _dragVisual;
        private string? _dragPreState;
        private double _boxX, _boxY, _boxW, _boxH;

        // рамка выделения мышью
        private bool _marquee, _marqueeAdd;
        private Point _marqueeStart;
        private System.Windows.Shapes.Rectangle? _marqueeRect;

        // направляющие
        private readonly List<double> _guideV = new();
        private readonly List<double> _guideH = new();

        private bool IsSel(UiNode n) => _selection.Any(s => ReferenceEquals(s.Element, n.Element));

        // ==================== Выделение ====================

        /// <summary>Перерисовать холст и всё, что зависит от выделения.</summary>
        private void ApplySelectionChange(bool scrollCode = true)
        {
            RenderCanvas();
            OnSelectionChanged(scrollCode);
        }

        private bool _syncingList;

        private void OnSelectionChanged(bool scrollCode)
        {
            UpdateButtons();

            _syncingList = true;
            ElementsList.SelectedItems.Clear();
            foreach (var n in _selection) ElementsList.SelectedItems.Add(n);
            _syncingList = false;

            BuildPropertiesPanel();
            UpdateCodeHighlight(scrollCode);
        }

        private void ElementsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingList || _document is null) return;
            _selection.Clear();
            foreach (var item in ElementsList.SelectedItems.OfType<UiNode>()) _selection.Add(item);
            ApplySelectionChange();
        }

        // ==================== Рендер ====================

        private void RenderCanvas()
        {
            DesignCanvas.Children.Clear();
            _guideV.Clear();
            _guideH.Clear();
            _marqueeRect = null;
            _missingImages.Clear();

            if (_document is null)
            {
                UpdateMissingImagesBanner();
                return;
            }

            // выделение могло указывать на узлы старого документа — оставляем только живые
            _selection.RemoveAll(s => !_document.Children.Any(c => ReferenceEquals(c.Element, s.Element)));

            double w = _document.Root.Width > 0 ? _document.Root.Width : 400;
            double h = _document.Root.Height > 0 ? _document.Root.Height : 300;
            DialogBackdrop.Width = w;
            DialogBackdrop.Height = h;

            var bg = ResolveTracked(_document.Root.MainImageFile);
            DialogBackdrop.Background = bg != null ? new ImageBrush(bg) { Stretch = Stretch.Fill } : CheckerBrush();

            _syncingList = true;
            ElementsList.ItemsSource = null;
            ElementsList.ItemsSource = _document.Children;
            _syncingList = false;

            foreach (var node in _document.Children)
                DesignCanvas.Children.Add(CreateVisualForNode(node));

            UpdateMissingImagesBanner();
        }

        private BitmapSource? ResolveTracked(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            var result = _imageResolver?.Resolve(fileName);
            if (result is null) _missingImages.Add(fileName);
            return result;
        }

        private void UpdateMissingImagesBanner()
        {
            if (_missingImages.Count == 0) { MissingImagesBanner.Visibility = Visibility.Collapsed; return; }

            string reason = _imageResolver is null ? "источник текстур не подключён" : "нет в подключённых текстурах";
            string names = string.Join(", ", _missingImages.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(8));
            if (_missingImages.Count > 8) names += $" и ещё {_missingImages.Count - 8}";
            MissingImagesText.Text = $"Не найдено картинок ({_missingImages.Count}; {reason}): {names}";
            MissingImagesBanner.Visibility = Visibility.Visible;
        }

        private FrameworkElement? FindVisualForNode(UiNode node) =>
            DesignCanvas.Children.OfType<FrameworkElement>()
                .FirstOrDefault(v => v.Tag is UiNode n && ReferenceEquals(n.Element, node.Element));

        private FrameworkElement CreateVisualForNode(UiNode node)
        {
            bool selected = IsSel(node);

            // У многих надписей нет Width/Height — игра подгоняет размер по тексту; делаем так же (NaN = по содержимому).
            var container = new Grid
            {
                Width = node.Width > 0 ? Math.Max(node.Width, 4) : double.NaN,
                Height = node.Height > 0 ? Math.Max(node.Height, 4) : double.NaN,
                MinWidth = 4,
                MinHeight = 4,
                Tag = node,
                Cursor = Cursors.SizeAll,
                ToolTip = $"{node.DisplayName} — {node.Name}"
            };
            Canvas.SetLeft(container, node.X);
            Canvas.SetTop(container, node.Y);

            var image = ResolveTracked(node.MainImageFile);
            container.Children.Add(new Border
            {
                Background = image != null ? new ImageBrush(image) { Stretch = Stretch.Fill } : PlaceholderBrush()
            });

            if (node.TextValue is { Length: > 0 } text)
            {
                int align = node.TextAlign;
                var tb = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(PwColor.Parse(node.TextColor, Colors.White)),
                    FontFamily = TextFont,
                    FontWeight = node.TextBold ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = Math.Clamp(node.TextFontSize ?? 12, 6, 72),
                    HorizontalAlignment = align == 1 ? HorizontalAlignment.Center
                                         : align == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    TextAlignment = align == 1 ? TextAlignment.Center
                                   : align == 2 ? TextAlignment.Right : TextAlignment.Left,
                    VerticalAlignment = node.Schema.TextVCenter ? VerticalAlignment.Center : VerticalAlignment.Top,
                    TextWrapping = node.Schema.TextWrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    TextTrimming = node.Schema.TextWrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                    IsHitTestVisible = false
                };
                // тень под светлым текстом — как обводка в игре, читается на любой картинке
                tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 1, Opacity = 0.8 };
                container.Children.Add(tb);
            }

            var outline = new Border
            {
                BorderBrush = selected ? (Brush)FindResource("Accent") : Brushes.Transparent,
                BorderThickness = new Thickness(selected ? 2 : 1),
                IsHitTestVisible = false
            };
            container.Children.Add(outline);

            var hoverBrush = new SolidColorBrush(Color.FromArgb(170, 0x91, 0x86, 0xF4));
            container.MouseEnter += (_, _) =>
            {
                if (_dragging || _marquee) return;
                if (!selected) outline.BorderBrush = hoverBrush;
                _hoverNode = node;
                UpdateCodeHighlight(false);
            };
            container.MouseLeave += (_, _) =>
            {
                if (!selected) outline.BorderBrush = Brushes.Transparent;
                if (_hoverNode != null && ReferenceEquals(_hoverNode.Element, node.Element))
                {
                    _hoverNode = null;
                    UpdateCodeHighlight(false);
                }
            };

            container.MouseLeftButtonDown += Visual_MouseLeftButtonDown;
            container.MouseMove += Visual_MouseMove;
            container.MouseLeftButtonUp += Visual_MouseLeftButtonUp;

            if (selected && _selection.Count == 1)
                AddResizeHandles(container, node);

            return container;
        }

        private static Brush PlaceholderBrush() => new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 12),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing
            {
                Brush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                Geometry = Geometry.Parse("M0,12 L12,0 M-3,3 L3,-3 M9,15 L15,9")
            }
        };

        private static Brush CheckerBrush() => new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x3A, 0x3B, 0x47)), null, Geometry.Parse("M0,0 H16 V16 H0 Z")),
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x44, 0x45, 0x52)), null, Geometry.Parse("M0,0 H8 V8 H0 Z M8,8 H16 V16 H8 Z"))
                }
            }
        };

        // ==================== Масштаб ====================

        private void Canvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            double zoom = Math.Clamp(ZoomScale.ScaleX * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.25, 4.0);
            ZoomScale.ScaleX = zoom;
            ZoomScale.ScaleY = zoom;
            ZoomLabel.Text = $"{zoom * 100:0}%";
            e.Handled = true;
        }

        // ==================== Прилипание ====================

        private double Snap(double v) => SnapToGridCheck.IsChecked == true ? Math.Round(v / 5.0) * 5.0 : v;

        private double SnapThreshold => 6.0 / Math.Max(ZoomScale.ScaleX, 0.01);

        /// <summary>Линии, к которым можно прилипнуть: края и центр окна + края и центры остальных элементов.</summary>
        private (List<double> Xs, List<double> Ys) Candidates(Func<UiNode, bool> exclude)
        {
            var xs = new List<double>();
            var ys = new List<double>();
            if (_document is null) return (xs, ys);

            double dw = _document.Root.Width > 0 ? _document.Root.Width : 400;
            double dh = _document.Root.Height > 0 ? _document.Root.Height : 300;
            xs.AddRange(new[] { 0, dw / 2, dw });
            ys.AddRange(new[] { 0, dh / 2, dh });

            foreach (var n in _document.Children)
            {
                if (exclude(n)) continue;
                xs.AddRange(new[] { n.X, n.X + n.Width / 2, n.X + n.Width });
                ys.AddRange(new[] { n.Y, n.Y + n.Height / 2, n.Y + n.Height });
            }
            return (xs, ys);
        }

        private static bool NearestLine(double value, List<double> lines, double threshold, out double target)
        {
            target = value;
            double best = double.MaxValue;
            foreach (var l in lines)
            {
                double d = Math.Abs(l - value);
                if (d <= threshold && d < best) { best = d; target = l; }
            }
            return best != double.MaxValue;
        }

        private void ShowGuides()
        {
            foreach (var g in DesignCanvas.Children.OfType<System.Windows.Shapes.Line>().Where(l => "guide".Equals(l.Tag)).ToList())
                DesignCanvas.Children.Remove(g);

            double dw = DialogBackdrop.Width, dh = DialogBackdrop.Height;
            var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4F, 0xD8));
            double thick = 1.0 / Math.Max(ZoomScale.ScaleX, 0.01);

            foreach (var x in _guideV)
                DesignCanvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = x, X2 = x, Y1 = 0, Y2 = dh, Stroke = brush, StrokeThickness = thick,
                    StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false, Tag = "guide"
                });
            foreach (var y in _guideH)
                DesignCanvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = 0, X2 = dw, Y1 = y, Y2 = y, Stroke = brush, StrokeThickness = thick,
                    StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false, Tag = "guide"
                });
        }

        private void ClearGuides()
        {
            _guideV.Clear();
            _guideH.Clear();
            ShowGuides();
        }

        // ==================== Перетаскивание элементов ====================

        private void Visual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid clicked || clicked.Tag is not UiNode node || _document is null) return;
            e.Handled = true;

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool was = IsSel(node);

            if (ctrl)
            {
                if (was) _selection.RemoveAll(s => ReferenceEquals(s.Element, node.Element));
                else _selection.Add(node);
                ApplySelectionChange();
                return;
            }

            bool changed = false;
            if (!was)
            {
                if (!shift) _selection.Clear();
                _selection.Add(node);
                changed = true;
            }
            _collapseOnUp = was && !shift && _selection.Count > 1;

            FrameworkElement visual = clicked;
            if (changed)
            {
                ApplySelectionChange();
                visual = FindVisualForNode(node) ?? clicked;
            }

            _dragging = true;
            _dragMoved = false;
            _dragVisual = visual;
            _dragNode = node;
            _dragStartMouse = e.GetPosition(DesignCanvas);
            _dragStarts = _selection.ToDictionary(n => n, n => (n.X, n.Y));
            _boxX = _selection.Min(n => n.X);
            _boxY = _selection.Min(n => n.Y);
            _boxW = _selection.Max(n => n.X + n.Width) - _boxX;
            _boxH = _selection.Max(n => n.Y + n.Height) - _boxY;
            _dragPreState = _document.ToXmlString();
            visual.CaptureMouse();
        }

        private void Visual_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || _dragVisual == null || _dragNode == null || _document == null) return;
            if (!ReferenceEquals(sender, _dragVisual)) return;

            e.Handled = true;
            var pos = e.GetPosition(DesignCanvas);
            double dx = pos.X - _dragStartMouse.X;
            double dy = pos.Y - _dragStartMouse.Y;

            if (!_dragMoved && Math.Abs(dx) < 3 && Math.Abs(dy) < 3) return; // обычный клик — не сдвиг
            _dragMoved = true;

            (dx, dy) = SnapMove(dx, dy);

            foreach (var kv in _dragStarts)
            {
                var node = kv.Key;
                double nx = kv.Value.X + dx, ny = kv.Value.Y + dy;
                node.SetGeometry(nx, ny, node.Width, node.Height);
                var v = FindVisualForNode(node);
                if (v != null) { Canvas.SetLeft(v, nx); Canvas.SetTop(v, ny); }
            }

            ShowGuides();
            LiveCodeUpdate();

            var p = _dragStarts[_dragNode];
            StatusText.Text = $"X: {p.X + dx:0}  Y: {p.Y + dy:0}   ·   {_selection.Count} эл.";
        }

        private (double dx, double dy) SnapMove(double dx, double dy)
        {
            _guideV.Clear();
            _guideH.Clear();
            bool snapX = false, snapY = false;

            if (SnapToNeighborsCheck.IsChecked == true)
            {
                var (xs, ys) = Candidates(IsSel);
                double t = SnapThreshold;

                double bestX = double.MaxValue, gx = 0;
                foreach (double edge in new[] { _boxX + dx, _boxX + dx + _boxW / 2, _boxX + dx + _boxW })
                    if (NearestLine(edge, xs, t, out double target) && Math.Abs(target - edge) < Math.Abs(bestX))
                    { bestX = target - edge; gx = target; }
                if (bestX != double.MaxValue) { dx += bestX; snapX = true; _guideV.Add(gx); }

                double bestY = double.MaxValue, gy = 0;
                foreach (double edge in new[] { _boxY + dy, _boxY + dy + _boxH / 2, _boxY + dy + _boxH })
                    if (NearestLine(edge, ys, t, out double target) && Math.Abs(target - edge) < Math.Abs(bestY))
                    { bestY = target - edge; gy = target; }
                if (bestY != double.MaxValue) { dy += bestY; snapY = true; _guideH.Add(gy); }
            }

            if (_dragNode != null && _dragStarts.TryGetValue(_dragNode, out var start))
            {
                if (!snapX) dx = Snap(start.X + dx) - start.X;
                if (!snapY) dy = Snap(start.Y + dy) - start.Y;
            }
            return (dx, dy);
        }

        private void Visual_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging || _dragVisual == null) return;
            e.Handled = true;
            _dragVisual.ReleaseMouseCapture();
            ClearGuides();

            bool moved = _dragMoved;
            var pre = _dragPreState;
            var node = _dragNode;
            _dragging = false;
            _dragVisual = null;
            _dragNode = null;
            _dragPreState = null;

            if (moved)
            {
                if (pre != null) PushUndoState(pre);
                _dirty = true;
                RefreshAfterMutation();
            }
            else if (_collapseOnUp && node != null)
            {
                _selection.Clear();
                _selection.Add(node);
                ApplySelectionChange();
            }
            _collapseOnUp = false;
        }

        // ==================== Рамка выделения по пустому месту ====================

        private void DesignCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_document is null) return;

            _marquee = true;
            _marqueeAdd = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            _marqueeStart = e.GetPosition(DesignCanvas);

            _marqueeRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = (Brush)FindResource("Accent"),
                StrokeThickness = 1.0 / Math.Max(ZoomScale.ScaleX, 0.01),
                Fill = new SolidColorBrush(Color.FromArgb(40, 0x7C, 0x6F, 0xF0)),
                IsHitTestVisible = false,
                Width = 0,
                Height = 0
            };
            Canvas.SetLeft(_marqueeRect, _marqueeStart.X);
            Canvas.SetTop(_marqueeRect, _marqueeStart.Y);
            DesignCanvas.Children.Add(_marqueeRect);
            DesignCanvas.CaptureMouse();
        }

        private void DesignCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_marquee || _marqueeRect == null) return;
            var p = e.GetPosition(DesignCanvas);
            Canvas.SetLeft(_marqueeRect, Math.Min(p.X, _marqueeStart.X));
            Canvas.SetTop(_marqueeRect, Math.Min(p.Y, _marqueeStart.Y));
            _marqueeRect.Width = Math.Abs(p.X - _marqueeStart.X);
            _marqueeRect.Height = Math.Abs(p.Y - _marqueeStart.Y);
        }

        private void DesignCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_marquee) return;
            _marquee = false;
            DesignCanvas.ReleaseMouseCapture();

            var p = e.GetPosition(DesignCanvas);
            var rect = new Rect(Math.Min(p.X, _marqueeStart.X), Math.Min(p.Y, _marqueeStart.Y),
                                Math.Abs(p.X - _marqueeStart.X), Math.Abs(p.Y - _marqueeStart.Y));
            bool tiny = rect.Width < 3 && rect.Height < 3;

            if (_document != null)
            {
                if (!_marqueeAdd) _selection.Clear();
                if (!tiny)
                {
                    foreach (var n in _document.Children)
                        if (new Rect(n.X, n.Y, n.Width, n.Height).IntersectsWith(rect) && !IsSel(n))
                            _selection.Add(n);
                }
            }
            ApplySelectionChange();
        }

        // ==================== Изменение размера (8 ручек) ====================

        private void AddResizeHandles(Grid container, UiNode node)
        {
            const HorizontalAlignment L = HorizontalAlignment.Left, HC = HorizontalAlignment.Center, R = HorizontalAlignment.Right;
            const VerticalAlignment T = VerticalAlignment.Top, VC = VerticalAlignment.Center, B = VerticalAlignment.Bottom;

            AddResizeHandle(container, node, L,  T,  Cursors.SizeNWSE, true,  false, true,  false);
            AddResizeHandle(container, node, HC, T,  Cursors.SizeNS,   false, false, true,  false);
            AddResizeHandle(container, node, R,  T,  Cursors.SizeNESW, false, true,  true,  false);
            AddResizeHandle(container, node, L,  VC, Cursors.SizeWE,   true,  false, false, false);
            AddResizeHandle(container, node, R,  VC, Cursors.SizeWE,   false, true,  false, false);
            AddResizeHandle(container, node, L,  B,  Cursors.SizeNESW, true,  false, false, true);
            AddResizeHandle(container, node, HC, B,  Cursors.SizeNS,   false, false, false, true);
            AddResizeHandle(container, node, R,  B,  Cursors.SizeNWSE, false, true,  false, true);
        }

        private void AddResizeHandle(Grid container, UiNode node, HorizontalAlignment hAlign, VerticalAlignment vAlign,
            Cursor cursor, bool left, bool right, bool top, bool bottom)
        {
            const double size = 9, off = -size / 2;

            var handle = new Border
            {
                Width = size,
                Height = size,
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("Accent"),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign,
                Cursor = cursor,
                Margin = new Thickness(
                    hAlign == HorizontalAlignment.Left ? off : 0,
                    vAlign == VerticalAlignment.Top ? off : 0,
                    hAlign == HorizontalAlignment.Right ? off : 0,
                    vAlign == VerticalAlignment.Bottom ? off : 0)
            };

            bool active = false, changed = false;
            Point startMouse = default;
            double sx = 0, sy = 0, sw = 0, sh = 0;
            string? pre = null;

            handle.MouseLeftButtonDown += (_, e) =>
            {
                if (_document is null) return;
                e.Handled = true;
                active = true;
                changed = false;
                startMouse = e.GetPosition(DesignCanvas);
                sx = node.X; sy = node.Y; sw = node.Width; sh = node.Height;
                pre = _document.ToXmlString();
                handle.CaptureMouse();
            };

            handle.MouseMove += (_, e) =>
            {
                if (!active) return;
                var p = e.GetPosition(DesignCanvas);
                double dx = p.X - startMouse.X, dy = p.Y - startMouse.Y;

                double x = sx, y = sy, w = sw, h = sh;
                if (right) w = sw + dx;
                if (bottom) h = sh + dy;
                if (left) { x = sx + dx; w = sw - dx; }
                if (top) { y = sy + dy; h = sh - dy; }

                _guideV.Clear();
                _guideH.Clear();

                var (xs, ys) = SnapToNeighborsCheck.IsChecked == true
                    ? Candidates(n => ReferenceEquals(n.Element, node.Element))
                    : (new List<double>(), new List<double>());
                double t = SnapThreshold;

                if (left)
                {
                    if (NearestLine(x, xs, t, out double tx)) { w += x - tx; x = tx; _guideV.Add(tx); }
                    else { double nx = Snap(x); w += x - nx; x = nx; }
                }
                if (right)
                {
                    if (NearestLine(x + w, xs, t, out double tx)) { w = tx - x; _guideV.Add(tx); }
                    else w = Snap(x + w) - x;
                }
                if (top)
                {
                    if (NearestLine(y, ys, t, out double ty)) { h += y - ty; y = ty; _guideH.Add(ty); }
                    else { double ny = Snap(y); h += y - ny; y = ny; }
                }
                if (bottom)
                {
                    if (NearestLine(y + h, ys, t, out double ty)) { h = ty - y; _guideH.Add(ty); }
                    else h = Snap(y + h) - y;
                }

                if (w < 4) { if (left) x -= 4 - w; w = 4; }
                if (h < 4) { if (top) y -= 4 - h; h = 4; }

                container.Width = w;
                container.Height = h;
                Canvas.SetLeft(container, x);
                Canvas.SetTop(container, y);
                node.SetGeometry(x, y, w, h);
                changed = true;

                ShowGuides();
                LiveCodeUpdate();
                StatusText.Text = $"X: {x:0}  Y: {y:0}   размер: {w:0}×{h:0}";
            };

            handle.MouseLeftButtonUp += (_, e) =>
            {
                if (!active) return;
                active = false;
                handle.ReleaseMouseCapture();
                e.Handled = true;
                ClearGuides();

                if (changed)
                {
                    if (pre != null) PushUndoState(pre);
                    _dirty = true;
                    RefreshAfterMutation();
                }
            };

            container.Children.Add(handle);
        }

        // ==================== Выравнивание ====================

        private void AlignSelected(string mode)
        {
            if (_document is null || _selection.Count == 0) return;
            var nodes = _selection.ToList();

            Mutate(() =>
            {
                double dw = _document.Root.Width > 0 ? _document.Root.Width : 400;
                double dh = _document.Root.Height > 0 ? _document.Root.Height : 300;

                // один элемент выравниваем по окну, несколько — по общей рамке выделения
                double minX = nodes.Count == 1 ? 0 : nodes.Min(n => n.X);
                double maxX = nodes.Count == 1 ? dw : nodes.Max(n => n.X + n.Width);
                double minY = nodes.Count == 1 ? 0 : nodes.Min(n => n.Y);
                double maxY = nodes.Count == 1 ? dh : nodes.Max(n => n.Y + n.Height);

                void Put(UiNode n, double x, double y) => n.SetGeometry(x, y, n.Width, n.Height);

                switch (mode)
                {
                    case "left":   foreach (var n in nodes) Put(n, minX, n.Y); break;
                    case "right":  foreach (var n in nodes) Put(n, maxX - n.Width, n.Y); break;
                    case "hcenter":foreach (var n in nodes) Put(n, (minX + maxX) / 2 - n.Width / 2, n.Y); break;
                    case "top":    foreach (var n in nodes) Put(n, n.X, minY); break;
                    case "bottom": foreach (var n in nodes) Put(n, n.X, maxY - n.Height); break;
                    case "vcenter":foreach (var n in nodes) Put(n, n.X, (minY + maxY) / 2 - n.Height / 2); break;

                    case "hdist" when nodes.Count >= 3:
                    {
                        var s = nodes.OrderBy(n => n.X).ToList();
                        double gap = (maxX - minX - s.Sum(n => n.Width)) / (s.Count - 1);
                        double x = minX;
                        foreach (var n in s) { Put(n, x, n.Y); x += n.Width + gap; }
                        break;
                    }
                    case "vdist" when nodes.Count >= 3:
                    {
                        var s = nodes.OrderBy(n => n.Y).ToList();
                        double gap = (maxY - minY - s.Sum(n => n.Height)) / (s.Count - 1);
                        double y = minY;
                        foreach (var n in s) { Put(n, n.X, y); y += n.Height + gap; }
                        break;
                    }
                }
            });
        }
    }
}
