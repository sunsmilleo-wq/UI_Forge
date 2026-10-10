using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using UiForge.Core;
using UiForge.Engine.WinPck;
using UiForge.UiFormat;

namespace UiForge.App
{
    /// <summary>Главное окно: источники файлов, список xml, undo/redo, сохранение/экспорт, горячие клавиши.</summary>
    public partial class MainWindow : Window
    {
        private IResourceSource? _ifaceSource;
        private IResourceSource? _texSource;
        private ImageResolver? _imageResolver;

        private readonly List<string> _allXml = new();
        private string? _currentFile;
        private Encoding _enc = Encoding.Unicode;
        private bool _hasBom;
        private int _lengthPrefix;
        private int _extraBoms;
        private UiDialogDocument? _document;

        /// <summary>Файл открыт, но не распознан как интерфейс (DIALOG) — виден только как текст в
        /// редакторе кода, без холста/свойств, пока текст не станет валидным и разбираемым XML.</summary>
        private bool _rawMode;

        private bool _dirty;
        private bool _syncingFileList;

        private readonly Stack<string> _undo = new();
        private readonly Stack<string> _redo = new();
        private const int MaxUndo = 80;

        public MainWindow()
        {
            InitializeComponent();

            AddTypeCombo.ItemsSource = UiControlSchema.ByTag.Values
                .Where(s => s.HasPosition).OrderBy(s => s.DisplayName).ToList();
            AddTypeCombo.SelectedIndex = 0;

            InitCodeEditor();
            BuildPropertiesPanel();

            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        // ==================== Запуск / закрытие ====================

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Ничего не открываем сами: редактор стартует чистым. Прошлые пути используются
            // только как стартовая папка в диалогах выбора.
            _lastIfaceSpec = AppSettings.Load().InterfacePath;
            _lastTexSpec = AppSettings.Load().TexturePath;
        }

        private string? _lastIfaceSpec, _lastTexSpec;

        /// <summary>Стартовая папка диалога по сохранённому "kind|path".</summary>
        private static string? StartDir(string? spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            int bar = spec.IndexOf('|');
            if (bar <= 0) return null;
            string kind = spec[..bar], path = spec[(bar + 1)..];
            string? dir = kind == "dir" ? path : Path.GetDirectoryName(path);
            return dir != null && Directory.Exists(dir) ? dir : null;
        }

        private void SaveSettings()
        {
            _lastIfaceSpec = _ifaceSource is null ? _lastIfaceSpec : $"{_ifaceSource.Kind}|{_ifaceSource.Location}";
            _lastTexSpec = _texSource is null ? _lastTexSpec : $"{_texSource.Kind}|{_texSource.Location}";
            SaveSettingsCore();
        }

        private void SaveSettingsCore() =>
            AppSettings.Save(_lastIfaceSpec, _lastTexSpec);

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_currentFile != null && _dirty)
            {
                var r = MessageBox.Show(this, "Есть несохранённые изменения. Закрыть без сохранения?",
                    "UiForge", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            }
            _ifaceSource?.Dispose();
            _texSource?.Dispose();
        }

        private bool ConfirmDiscard()
        {
            if (_currentFile == null || !_dirty) return true;
            return MessageBox.Show(this,
                $"В '{_currentFile}' есть несохранённые изменения. Продолжить без сохранения?",
                "Несохранённые изменения", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private const string AppVersion = "v1.0";

        private void UpdateTitle() =>
            Title = _currentFile is null ? $"UiForge {AppVersion} — редактор интерфейсов Perfect World"
                                         : $"UiForge {AppVersion} — {_currentFile}{(_dirty ? "  •" : "")}";

        // ==================== Открытие источников ====================

        private void OpenInterfacePck_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "PCK-архивы (*.pck)|*.pck|Все файлы (*.*)|*.*", Title = "Архив интерфейсов", InitialDirectory = StartDir(_lastIfaceSpec) };
            if (d.ShowDialog(this) == true) OpenInterface("pck", d.FileName, silent: false);
        }

        private void OpenInterfaceDir_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFolderDialog { Title = "Папка с xml-файлами интерфейсов", InitialDirectory = StartDir(_lastIfaceSpec) };
            if (d.ShowDialog(this) == true) OpenInterface("dir", d.FolderName, silent: false);
        }

        private void OpenTexturePck_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "PCK-архивы (*.pck)|*.pck|Все файлы (*.*)|*.*", Title = "Архив текстур (surfaces)", InitialDirectory = StartDir(_lastTexSpec) };
            if (d.ShowDialog(this) == true) OpenTexture("pck", d.FileName, silent: false);
        }

        private void OpenTextureDir_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFolderDialog { Title = "Папка с текстурами (surfaces)", InitialDirectory = StartDir(_lastTexSpec) };
            if (d.ShowDialog(this) == true) OpenTexture("dir", d.FolderName, silent: false);
        }

        /// <summary>Архив интерфейсов читаем через слот A, архив текстур — через слот B
        /// (отдельная копия DLL), иначе один заменял бы другой.</summary>
        private static IResourceSource CreateSource(bool isInterface, string kind, string path)
        {
            if (kind == "dir") return new FolderSource(path);

            IPckEngine engine = isInterface ? new WinPckEngine() : new WinPckEngineB();
            try { engine.Open(path); }
            catch { engine.Dispose(); throw; }
            return new PckSource(engine, path);
        }

        private void OpenInterface(string kind, string path, bool silent)
        {
            if (!ConfirmDiscard()) return;
            try
            {
                var newSource = CreateSource(true, kind, path); // если не откроется — старый источник остаётся рабочим

                CloseCurrentDocument();
                _ifaceSource?.Dispose();
                _ifaceSource = newSource;

                _allXml.Clear();
                _allXml.AddRange(_ifaceSource.Files.Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
                FileFilterBox.Text = "";
                ApplyFileFilter();

                InterfaceLabel.Text = $"{(kind == "dir" ? "папка" : "архив")} {_ifaceSource.DisplayName} · {_allXml.Count} xml";
                StatusText.Text = _allXml.Count > 0 ? "Выберите файл интерфейса слева." : "В источнике нет .xml файлов.";
                SaveSettings();
            }
            catch (Exception ex)
            {
                if (silent) StatusText.Text = "Не удалось восстановить интерфейсы: " + ex.Message;
                else MessageBox.Show(this, ex.ToString(), "Не удалось открыть интерфейсы", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenTexture(string kind, string path, bool silent)
        {
            try
            {
                var newSource = CreateSource(false, kind, path); // если не откроется — старые текстуры остаются рабочими

                _texSource?.Dispose();
                _texSource = newSource;
                _imageResolver = new ImageResolver(_texSource);

                TextureLabel.Text = $"{(kind == "dir" ? "папка" : "архив")} {_texSource.DisplayName} · {_texSource.Files.Count} файлов";
                StatusText.Text = "Текстуры подключены.";
                SaveSettings();

                if (_document != null) { RenderCanvas(); OnSelectionChanged(false); }
            }
            catch (Exception ex)
            {
                if (silent) StatusText.Text = "Не удалось восстановить текстуры: " + ex.Message;
                else MessageBox.Show(this, ex.ToString(), "Не удалось открыть текстуры", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== Список xml-файлов ====================

        private void FileFilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFileFilter();

        private void ApplyFileFilter()
        {
            string f = FileFilterBox.Text.Trim();
            var list = string.IsNullOrEmpty(f)
                ? _allXml.ToList()
                : _allXml.Where(x => x.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();

            _syncingFileList = true;
            InterfaceFilesList.ItemsSource = list;
            if (_currentFile != null && list.Contains(_currentFile)) InterfaceFilesList.SelectedItem = _currentFile;
            _syncingFileList = false;
        }

        private void InterfaceFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingFileList) return;
            if (InterfaceFilesList.SelectedItem is not string path || _ifaceSource is null) return;
            if (path == _currentFile) return;

            if (!ConfirmDiscard())
            {
                _syncingFileList = true;
                InterfaceFilesList.SelectedItem = _currentFile;
                _syncingFileList = false;
                return;
            }
            LoadFile(path);
        }

        private void LoadFile(string path)
        {
            if (_ifaceSource is null) return;

            byte[] bytes;
            try { bytes = _ifaceSource.Read(path); }
            catch (Exception ex) { FailToOpen(path, ex.Message); return; }

            var decoded = TextCodec.Decode(bytes);
            if (decoded.Encoding is null)
            {
                FailToOpen(path, "Файл не похож на текст — скорее всего, это не XML, а бинарные/сжатые данные.\n\n" +
                                 "Первые байты (hex):\n" + TextCodec.HexPreview(bytes, 48));
                return;
            }
            string text = decoded.Text;
            var encoding = decoded.Encoding;
            bool hasBom = decoded.HasBom;

            UiDialogDocument? doc = null;
            string? parseError = null;
            try { doc = UiDialogDocument.Parse(text); }
            catch (Exception ex) { parseError = ex.Message; }

            _currentFile = path;
            _enc = encoding;
            _hasBom = hasBom;
            _lengthPrefix = decoded.LengthPrefixBytes;
            _extraBoms = decoded.ExtraBoms;
            _document = doc;
            _rawMode = doc is null;
            _selection.Clear();
            _hoverNode = null;
            _undo.Clear();
            _redo.Clear();
            _dirty = false;
            UpdateUndoRedoButtons();

            SetEditorText(text, keepScroll: false);
            RenderCanvas();
            OnSelectionChanged(false);
            UpdateButtons();
            UpdateTitle();

            if (_rawMode)
            {
                CodeStatus.Text = "⚠ " + FirstLine(parseError ?? "не похоже на интерфейс");
                CodeStatus.Foreground = (Brush)FindResource("Warn");
                StatusText.Text = $"'{path}' не распознан как интерфейс — открыт как обычный текст. Первые байты: " +
                                   TextCodec.HexPreview(bytes, 24) +
                                   (_lengthPrefix > 0 ? $"  (заголовок длины {_lengthPrefix} байта учтён)" : "");
            }
            else
            {
                StatusText.Text = $"Открыт: {path}" +
                                  (_lengthPrefix > 0 ? $"  (с заголовком длины {_lengthPrefix} байта — пересчитывается при сохранении)" : "");
            }
        }

        private void FailToOpen(string path, string message)
        {
            MessageBox.Show(this, $"Не удалось открыть '{path}':\n{message}",
                "Нельзя открыть", MessageBoxButton.OK, MessageBoxImage.Warning);
            _syncingFileList = true;
            InterfaceFilesList.SelectedItem = _currentFile;
            _syncingFileList = false;
        }

        private void CloseCurrentDocument()
        {
            _currentFile = null;
            _document = null;
            _rawMode = false;
            _selection.Clear();
            _hoverNode = null;
            _undo.Clear();
            _redo.Clear();
            _dirty = false;
            UpdateUndoRedoButtons();

            DesignCanvas.Children.Clear();
            DialogBackdrop.Width = 0;
            DialogBackdrop.Height = 0;
            ElementsList.ItemsSource = null;
            SetEditorText("", keepScroll: false);
            _lineMap.Clear();
            MissingImagesBanner.Visibility = Visibility.Collapsed;
            CodeStatus.Text = "";
            OnSelectionChanged(false);
            UpdateTitle();
        }

        private void UpdateButtons()
        {
            bool fileOpen = _currentFile != null;   // можно сохранять/экспортировать и в raw-режиме
            bool has = _document != null;            // а вот добавлять/дублировать/удалять — только когда есть разобранная модель
            SaveButton.IsEnabled = fileOpen;
            ExportButton.IsEnabled = fileOpen;
            AddTypeCombo.IsEnabled = has;
            AddButton.IsEnabled = has;
            DeleteButton.IsEnabled = has && _selection.Count > 0;
            DuplicateButton.IsEnabled = has && _selection.Count > 0;
        }

        // ==================== Undo / Redo / Mutate ====================

        private void PushUndoState(string state)
        {
            _undo.Push(state);
            _redo.Clear();
            if (_undo.Count > MaxUndo)
            {
                var keep = _undo.Take(MaxUndo).ToList();
                _undo.Clear();
                for (int i = keep.Count - 1; i >= 0; i--) _undo.Push(keep[i]);
            }
            UpdateUndoRedoButtons();
        }

        /// <summary>Одно изменение = один шаг отмены: снимок до, действие, перерисовка всего.</summary>
        private void Mutate(Action action)
        {
            if (_document is null) return;
            string before = _document.ToXmlString();
            bool wasDirty = _dirty;
            PushUndoState(before);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Действие сломалось на полпути — откатываем документ к состоянию до
                // него (снимок уже в _undo) и сообщаем, вместо падения всего окна.
                _undo.Pop();
                ReplaceDocument(before);
                _dirty = wasDirty;
                UpdateTitle();
                MessageBox.Show(this, ex.Message, "Не удалось применить изменение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _dirty = true;
            RefreshAfterMutation();
        }

        private void RefreshAfterMutation()
        {
            if (_document is null) return;
            SetEditorText(_document.ToXmlString());
            RenderCanvas();
            OnSelectionChanged(false);
            UpdateTitle();
            StatusText.Text = "Изменено (не сохранено).";
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();
        private void RedoButton_Click(object sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            if (_document is null || _undo.Count == 0) return;
            _redo.Push(_document.ToXmlString());
            ReplaceDocument(_undo.Pop());
        }

        private void Redo()
        {
            if (_document is null || _redo.Count == 0) return;
            _undo.Push(_document.ToXmlString());
            ReplaceDocument(_redo.Pop());
        }

        private void ReplaceDocument(string xml)
        {
            try { _document = UiDialogDocument.Parse(xml); }
            catch { return; }

            _selection.Clear();
            _hoverNode = null;
            _dirty = true;
            SetEditorText(xml);
            RenderCanvas();
            OnSelectionChanged(false);
            UpdateUndoRedoButtons();
            UpdateTitle();
            StatusText.Text = "Изменено (не сохранено).";
        }

        private void UpdateUndoRedoButtons()
        {
            UndoButton.IsEnabled = _undo.Count > 0;
            RedoButton.IsEnabled = _redo.Count > 0;
        }

        // ==================== Горячие клавиши ====================

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool typing = e.OriginalSource is TextBox || XmlEditor.TextArea.IsKeyboardFocusWithin;

            if (ctrl && e.Key == Key.S) { SaveCurrent(); e.Handled = true; return; }
            if (typing) return; // в полях ввода и в коде — обычное поведение клавиш (Ctrl+Z там свой)

            if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D) { DuplicateSelected(); e.Handled = true; }
            else if (ctrl && e.Key == Key.A && _document != null)
            {
                _selection.Clear();
                _selection.AddRange(_document.Children);
                ApplySelectionChange();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.Escape && _selection.Count > 0) { _selection.Clear(); ApplySelectionChange(); e.Handled = true; }
            else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
                double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                NudgeSelected(dx, dy);
                e.Handled = true;
            }
        }

        // ==================== Сохранение / экспорт ====================

        private void SaveButton_Click(object sender, RoutedEventArgs e) => SaveCurrent();

        private bool SaveCurrent()
        {
            if (_currentFile is null || _ifaceSource is null) return false;
            try
            {
                string text = _document?.ToXmlString() ?? XmlEditor.Text; // raw-режим сохраняет как есть
                byte[] bytes = TextCodec.Encode(text, _enc, _hasBom, _lengthPrefix, _extraBoms);
                _ifaceSource.Write(_currentFile, bytes);
                _dirty = false;
                UpdateTitle();
                StatusText.Text = $"Сохранено: {_currentFile}";
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "Не удалось сохранить", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFile is null) return;
            var d = new SaveFileDialog
            {
                Title = "Экспорт XML на диск",
                Filter = "XML (*.xml)|*.xml|Все файлы (*.*)|*.*",
                FileName = Path.GetFileName(_currentFile)
            };
            if (d.ShowDialog(this) != true) return;

            try
            {
                string text = _document?.ToXmlString() ?? XmlEditor.Text;
                File.WriteAllBytes(d.FileName, TextCodec.Encode(text, _enc, _hasBom, _lengthPrefix, _extraBoms));
                StatusText.Text = $"Экспортировано: {d.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Не удалось экспортировать", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== Добавление / удаление / дублирование ====================

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (_document is null || AddTypeCombo.SelectedItem is not UiControlSchema schema) return;
            double w = Math.Min(120, Math.Max(40, _document.Root.Width - 40));
            double h = Math.Min(30, Math.Max(20, _document.Root.Height - 40));

            Mutate(() =>
            {
                var node = _document.AddChild(schema.Tag, 20, 20, w, h);
                _selection.Clear();
                _selection.Add(node);
            });
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelected();
        private void DuplicateButton_Click(object sender, RoutedEventArgs e) => DuplicateSelected();

        private void DeleteSelected()
        {
            if (_document is null || _selection.Count == 0) return;
            var victims = _selection.ToList();
            Mutate(() =>
            {
                foreach (var n in victims) _document.RemoveChild(n);
                _selection.Clear();
            });
        }

        private void DuplicateSelected()
        {
            if (_document is null || _selection.Count == 0) return;
            var sources = _selection.ToList();
            Mutate(() =>
            {
                _selection.Clear();
                foreach (var s in sources)
                {
                    var clone = _document.Duplicate(s);
                    clone.SetGeometry(s.X + 14, s.Y + 14, s.Width, s.Height);
                    _selection.Add(clone);
                }
            });
        }

        private void NudgeSelected(double dx, double dy)
        {
            if (_document is null || _selection.Count == 0) return;
            var nodes = _selection.ToList();
            Mutate(() =>
            {
                foreach (var n in nodes) n.SetGeometry(n.X + dx, n.Y + dy, n.Width, n.Height);
            });
        }
    }
}
