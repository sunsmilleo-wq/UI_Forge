using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UiForge.UiFormat;

namespace UiForge.App
{
    /// <summary>Панель свойств справа: строится из схемы выбранного элемента (или окна, если ничего не выбрано).</summary>
    public partial class MainWindow
    {
        private void BuildPropertiesPanel()
        {
            PropertiesPanel.Children.Clear();

            if (_document is null)
            {
                if (_currentFile != null)
                    AddHint($"'{_currentFile}' не распознан как окно интерфейса (корень <DIALOG>), поэтому открыт как обычный текст — " +
                            "его видно и можно править в редакторе кода ниже, и сохранить. Если поправить XML так, чтобы он стал " +
                            "распознаваемым, холст и свойства появятся сами. Первые байты файла показаны в строке состояния — " +
                            "пришлите их, если нужна помощь с этим форматом.");
                else
                    AddHint("Откройте архив или папку с интерфейсами, выберите файл слева — и он появится на холсте.");
                return;
            }

            if (_selection.Count == 0)
            {
                AddHint("Ничего не выбрано — ниже свойства самого окна. Кликните элемент на холсте, " +
                        "или обведите несколько рамкой. Ctrl+клик — добавить к выделению.");
                BuildPropertiesFor(_document.Root, isRoot: true);
                return;
            }

            if (_selection.Count > 1)
            {
                PropertiesPanel.Children.Add(new TextBlock
                {
                    Text = $"Выбрано элементов: {_selection.Count}", FontWeight = FontWeights.Bold, FontSize = 15
                });
                AddHint("Тяните любой из них — сдвинутся все. Delete — удалить, Ctrl+D — дублировать, стрелки — сдвиг.");
                AddAlignBar();
                return;
            }

            BuildPropertiesFor(_selection[0], isRoot: false);
        }

        private void AddHint(string text) =>
            PropertiesPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = (Brush)FindResource("TextMuted"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

        private void BuildPropertiesFor(UiNode node, bool isRoot)
        {
            PropertiesPanel.Children.Add(new TextBlock { Text = node.DisplayName, FontWeight = FontWeights.Bold, FontSize = 15 });
            PropertiesPanel.Children.Add(new TextBlock
            {
                Text = node.Tag, Foreground = (Brush)FindResource("TextMuted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 4)
            });

            if (!isRoot) AddAlignBar();

            AddGroupHeader("Геометрия");
            if (node.Schema.HasPosition)
            {
                AddNumberRow("X", node.X, v => Mutate(() => node.SetGeometry(v, node.Y, node.Width, node.Height)));
                AddNumberRow("Y", node.Y, v => Mutate(() => node.SetGeometry(node.X, v, node.Width, node.Height)));
            }
            AddNumberRow("Ширина", node.Width, v => Mutate(() => node.SetGeometry(node.X, node.Y, v, node.Height)));
            AddNumberRow("Высота", node.Height, v => Mutate(() => node.SetGeometry(node.X, node.Y, node.Width, v)));

            string? group = null;
            foreach (var prop in node.Schema.Properties)
            {
                if (prop.Group != group)
                {
                    AddGroupHeader(prop.Group);
                    group = prop.Group;
                }

                switch (prop.Kind)
                {
                    case UiPropertyKind.Number:
                        double.TryParse(node.GetValue(prop), NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
                        AddNumberRow(prop.Label, num,
                            v => Mutate(() => node.SetValue(prop, v.ToString("0.###", CultureInfo.InvariantCulture))));
                        break;
                    case UiPropertyKind.Text:
                        AddTextRow(prop.Label, node.GetValue(prop) ?? "", v => Mutate(() => node.SetValue(prop, v)));
                        break;
                    case UiPropertyKind.Color:
                        AddColorRow(prop.Label, node.GetValue(prop), v => Mutate(() => node.SetValue(prop, v)));
                        break;
                    case UiPropertyKind.ImageFile:
                        AddImageRow(prop.Label, node.GetValue(prop), v => Mutate(() => node.SetValue(prop, v)));
                        break;
                }
            }
        }

        // ---------- выравнивание ----------

        private void AddAlignBar()
        {
            AddGroupHeader(_selection.Count == 1 ? "Выровнять по окну" : "Выровнять выбранные");

            var panel = new WrapPanel();
            void Btn(string text, string mode, string tip)
            {
                var b = new Button
                {
                    Content = text, ToolTip = tip, Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 0, 5, 5), Background = (Brush)FindResource("PanelBgAlt")
                };
                b.Click += (_, _) => AlignSelected(mode);
                panel.Children.Add(b);
            }

            Btn("Слева", "left", "По левому краю");
            Btn("По центру", "hcenter", "По центру по горизонтали");
            Btn("Справа", "right", "По правому краю");
            Btn("Сверху", "top", "По верхнему краю");
            Btn("По середине", "vcenter", "По центру по вертикали");
            Btn("Снизу", "bottom", "По нижнему краю");
            if (_selection.Count >= 3)
            {
                Btn("Распределить ↔", "hdist", "Равные промежутки по горизонтали");
                Btn("Распределить ↕", "vdist", "Равные промежутки по вертикали");
            }
            PropertiesPanel.Children.Add(panel);
        }

        // ---------- строки свойств ----------

        private void AddGroupHeader(string text) =>
            PropertiesPanel.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("GroupHeader") });

        private void AddLabeledRow(string label, UIElement editor)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lbl = new TextBlock
            {
                Text = label, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextMuted"), TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(editor, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(editor);
            PropertiesPanel.Children.Add(grid);
        }

        private void AddNumberRow(string label, double value, Action<double> commit)
        {
            var box = new TextBox { Text = value.ToString("0.###", CultureInfo.InvariantCulture) };
            box.LostFocus += (_, _) =>
            {
                if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    && Math.Abs(v - value) > 0.0001)
                    commit(v);
            };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            AddLabeledRow(label, box);
        }

        private void AddTextRow(string label, string value, Action<string> commit)
        {
            var box = new TextBox { Text = value };
            box.LostFocus += (_, _) => { if (box.Text != value) commit(box.Text); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            AddLabeledRow(label, box);
        }

        private void AddColorRow(string label, string? value, Action<string> commit)
        {
            var panel = new DockPanel();

            var inner = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(PwColor.Parse(value, Colors.Gray))
            };
            var swatch = new Border
            {
                Width = 30, Height = 24, Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(4),
                BorderBrush = (Brush)FindResource("TextMuted"), BorderThickness = new Thickness(1),
                Background = SmallChecker(), Child = inner, Cursor = Cursors.Hand,
                ToolTip = "Нажмите, чтобы выбрать цвет"
            };
            DockPanel.SetDock(swatch, Dock.Left);

            var box = new TextBox
            {
                Text = value ?? "",
                ToolTip = "R,G,B,A — красный, зелёный, синий (0–255) и непрозрачность (255 — полностью виден, 0 — невидим)"
            };
            bool committed = false;

            box.TextChanged += (_, _) => inner.Background = new SolidColorBrush(PwColor.Parse(box.Text, Colors.Gray));
            box.LostFocus += (_, _) =>
            {
                if (!committed && box.Text != (value ?? "")) { committed = true; commit(box.Text); }
            };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };

            swatch.MouseLeftButtonUp += (_, _) =>
            {
                var picker = new ColorPickerWindow(PwColor.Parse(box.Text, Colors.White)) { Owner = this };
                if (picker.ShowDialog() == true && !committed)
                {
                    committed = true;
                    box.Text = picker.PickedString;
                    commit(picker.PickedString);
                }
            };

            panel.Children.Add(swatch);
            panel.Children.Add(box);
            AddLabeledRow(label, panel);
        }

        private void AddImageRow(string label, string? value, Action<string> commit)
        {
            // Подпись сверху, а под ней — картинка, полный путь и кнопка: так путь не обрезается
            // и хорошо читается (раньше он делил узкую строку с кнопкой).
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var thumb = new Border
            {
                Width = 44, Height = 44, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(4),
                Background = SmallChecker(), BorderBrush = (Brush)FindResource("BorderBrush1"), BorderThickness = new Thickness(1),
                ClipToBounds = true
            };
            var img = _imageResolver?.Resolve(value);
            if (img != null) thumb.Child = new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(2) };
            Grid.SetColumn(thumb, 0);

            var path = new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? "(не задана)" : value,
                ToolTip = value,
                Foreground = string.IsNullOrEmpty(value) ? (Brush)FindResource("TextMuted") : (Brush)FindResource("TextPrimary"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 44,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(path, 1);

            var browse = new Button { Content = "Обзор…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            browse.Click += (_, _) =>
            {
                if (_imageResolver is null)
                {
                    MessageBox.Show(this, "Сначала подключите текстуры (архив или папку).", "Картинки недоступны",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var picker = new ImagePickerWindow(_imageResolver.FindImageCandidates()) { Owner = this };
                if (picker.ShowDialog() == true && picker.SelectedFileName != null)
                    commit(picker.SelectedFileName);
            };
            Grid.SetColumn(browse, 2);

            row.Children.Add(thumb);
            row.Children.Add(path);
            row.Children.Add(browse);

            var holder = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            holder.Children.Add(new TextBlock
            {
                Text = label, Foreground = (Brush)FindResource("TextMuted"), Margin = new Thickness(0, 0, 0, 3), TextWrapping = TextWrapping.Wrap
            });
            holder.Children.Add(row);
            PropertiesPanel.Children.Add(holder);
        }

        private static Brush SmallChecker() => new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 8, 8),
            ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x3A, 0x3B, 0x47)), null, Geometry.Parse("M0,0 H8 V8 H0 Z")),
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x4D, 0x4E, 0x5C)), null, Geometry.Parse("M0,0 H4 V4 H0 Z M4,4 H8 V8 H4 Z"))
                }
            }
        };
    }
}
