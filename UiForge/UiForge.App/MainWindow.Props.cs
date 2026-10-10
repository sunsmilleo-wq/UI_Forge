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
            var swatch = new Border
            {
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(3),
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(PwColor.Parse(value, Colors.Gray))
            };
            DockPanel.SetDock(swatch, Dock.Left);

            var box = new TextBox { Text = value ?? "" };
            box.TextChanged += (_, _) => swatch.Background = new SolidColorBrush(PwColor.Parse(box.Text, Colors.Gray));
            box.LostFocus += (_, _) => { if (box.Text != (value ?? "")) commit(box.Text); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };

            panel.Children.Add(swatch);
            panel.Children.Add(box);
            AddLabeledRow(label, panel);
        }

        private void AddImageRow(string label, string? value, Action<string> commit)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };

            var thumb = new Border
            {
                Width = 32, Height = 32, Margin = new Thickness(0, 0, 6, 0), Background = Brushes.Black,
                BorderBrush = (Brush)FindResource("BorderBrush1"), BorderThickness = new Thickness(1)
            };
            var img = _imageResolver?.Resolve(value);
            if (img != null) thumb.Child = new Image { Source = img, Stretch = Stretch.Uniform };

            var name = new TextBlock
            {
                Text = string.IsNullOrEmpty(value) ? "(нет)" : value, ToolTip = value,
                VerticalAlignment = VerticalAlignment.Center, Width = 100,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0)
            };

            var browse = new Button { Content = "Обзор…", Padding = new Thickness(8, 3, 8, 3) };
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

            stack.Children.Add(thumb);
            stack.Children.Add(name);
            stack.Children.Add(browse);
            AddLabeledRow(label, stack);
        }
    }
}
