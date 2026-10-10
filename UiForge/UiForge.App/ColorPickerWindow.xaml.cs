using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UiForge.App
{
    /// <summary>Выбор цвета: квадрат насыщенность/яркость, полоса оттенка, прозрачность, числа R,G,B,A и HEX.</summary>
    public partial class ColorPickerWindow : Window
    {
        private const double SvW = 300, SvH = 180, HueW = 300;

        private double _h, _s, _v;
        private byte _a = 255;
        private bool _updating, _dragSv, _dragHue;

        /// <summary>Строка в формате игры: "R,G,B,A".</summary>
        public string PickedString { get; private set; } = "255,255,255,255";

        public ColorPickerWindow(Color initial)
        {
            InitializeComponent();

            OldBox.Background = NewBox.Background = CheckerBrush();
            OldInner.Background = new SolidColorBrush(initial);

            BuildPalette();
            SetFromColor(initial, keepHue: false);
        }

        // ---------- состояние ----------

        private Color Current() => FromHsv(_h, _s, _v, _a);

        private void SetFromColor(Color c, bool keepHue)
        {
            var (h, s, v) = ToHsv(c);
            if (!(keepHue && (s == 0 || v == 0))) _h = h;
            _s = s;
            _v = v;
            _a = c.A;
            UpdateUi();
        }

        private void UpdateUi()
        {
            _updating = true;
            var c = Current();

            NewInner.Background = new SolidColorBrush(c);
            SvBase.Background = new SolidColorBrush(FromHsv(_h, 1, 1, 255));
            Canvas.SetLeft(SvMarker, _s * SvW - 7);
            Canvas.SetTop(SvMarker, (1 - _v) * SvH - 7);
            Canvas.SetLeft(HueMarker, _h / 360.0 * HueW - 2);
            AlphaSlider.Value = _a;

            if (!RBox.IsKeyboardFocused) RBox.Text = c.R.ToString();
            if (!GBox.IsKeyboardFocused) GBox.Text = c.G.ToString();
            if (!BBox.IsKeyboardFocused) BBox.Text = c.B.ToString();
            if (!ABox.IsKeyboardFocused) ABox.Text = c.A.ToString();
            if (!HexBox.IsKeyboardFocused) HexBox.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";

            _updating = false;
        }

        // ---------- мышь ----------

        private void Sv_Down(object sender, MouseButtonEventArgs e)
        {
            _dragSv = true;
            SvCanvas.CaptureMouse();
            SvPick(e.GetPosition(SvCanvas));
        }

        private void Sv_Move(object sender, MouseEventArgs e)
        {
            if (_dragSv) SvPick(e.GetPosition(SvCanvas));
        }

        private void Sv_Up(object sender, MouseButtonEventArgs e)
        {
            _dragSv = false;
            SvCanvas.ReleaseMouseCapture();
        }

        private void SvPick(Point p)
        {
            _s = Math.Clamp(p.X / SvW, 0, 1);
            _v = 1 - Math.Clamp(p.Y / SvH, 0, 1);
            UpdateUi();
        }

        private void Hue_Down(object sender, MouseButtonEventArgs e)
        {
            _dragHue = true;
            HueCanvas.CaptureMouse();
            HuePick(e.GetPosition(HueCanvas));
        }

        private void Hue_Move(object sender, MouseEventArgs e)
        {
            if (_dragHue) HuePick(e.GetPosition(HueCanvas));
        }

        private void Hue_Up(object sender, MouseButtonEventArgs e)
        {
            _dragHue = false;
            HueCanvas.ReleaseMouseCapture();
        }

        private void HuePick(Point p)
        {
            _h = Math.Clamp(p.X / HueW, 0, 1) * 359.999;
            UpdateUi();
        }

        // ---------- поля ввода ----------

        private void AlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating) return;
            _a = (byte)Math.Round(AlphaSlider.Value);
            UpdateUi();
        }

        private void RgbaBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            if (byte.TryParse(RBox.Text, out byte r) && byte.TryParse(GBox.Text, out byte g) &&
                byte.TryParse(BBox.Text, out byte b) && byte.TryParse(ABox.Text, out byte a))
                SetFromColor(Color.FromArgb(a, r, g, b), keepHue: true);
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            string t = HexBox.Text.Trim().TrimStart('#');
            if (t.Length != 6) return;
            try
            {
                byte r = Convert.ToByte(t[..2], 16), g = Convert.ToByte(t[2..4], 16), b = Convert.ToByte(t[4..6], 16);
                SetFromColor(Color.FromArgb(_a, r, g, b), keepHue: true);
            }
            catch { /* пока вводят — игнорируем */ }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var c = Current();
            PickedString = $"{c.R},{c.G},{c.B},{c.A}";
            DialogResult = true;
        }

        // ---------- быстрые цвета ----------

        private void BuildPalette()
        {
            var colors = new (byte R, byte G, byte B)[]
            {
                (255, 255, 255), (0, 0, 0), (128, 128, 128), (255, 0, 0), (255, 140, 0), (255, 255, 0),
                (255, 203, 74),  (252, 234, 204), (0, 200, 0), (198, 255, 255), (40, 90, 255), (160, 60, 220),
                (255, 120, 180), (139, 90, 43), (10, 35, 50)
            };

            foreach (var (r, g, b) in colors)
            {
                var swatch = new Border
                {
                    Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x45, 0x52)), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, ToolTip = $"{r},{g},{b}"
                };
                swatch.MouseLeftButtonUp += (_, _) => SetFromColor(Color.FromArgb(_a, r, g, b), keepHue: false);
                Palette.Children.Add(swatch);
            }
        }

        // ---------- HSV ----------

        private static (double H, double S, double V) ToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            double h = 0;
            if (d > 0)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * ((b - r) / d + 2);
                else h = 60 * ((r - g) / d + 4);
            }
            if (h < 0) h += 360;
            return (h, max == 0 ? 0 : d / max, max);
        }

        private static Color FromHsv(double h, double s, double v, byte a)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r, g, b;

            switch ((int)(h / 60.0) % 6)
            {
                case 0: r = c; g = x; b = 0; break;
                case 1: r = x; g = c; b = 0; break;
                case 2: r = 0; g = c; b = x; break;
                case 3: r = 0; g = x; b = c; break;
                case 4: r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }

            return Color.FromArgb(a,
                (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        private static Brush CheckerBrush() => new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 10, 10),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x60)), null, Geometry.Parse("M0,0 H10 V10 H0 Z")),
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x84)), null, Geometry.Parse("M0,0 H5 V5 H0 Z M5,5 H10 V10 H5 Z"))
                }
            }
        };
    }
}
