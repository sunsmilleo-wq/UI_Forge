using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UiForge.Core;

namespace UiForge.App
{
    /// <summary>Поиск по всем xml открытого источника интерфейсов: индекс (файл → текст) + переход к найденному элементу.</summary>
    public partial class MainWindow
    {
        private Dictionary<string, string>? _searchIndex;
        private SearchWindow? _searchWindow;

        private void InvalidateSearchIndex() => _searchIndex = null;

        private void SearchButton_Click(object sender, RoutedEventArgs e) => OpenSearch();

        private void OpenSearch()
        {
            if (_ifaceSource is null) return;
            if (_searchWindow != null)
            {
                _searchWindow.Activate();
                _searchWindow.FocusQuery();
                return;
            }
            _searchWindow = new SearchWindow(this) { Owner = this };
            _searchWindow.Closed += (_, _) => _searchWindow = null;
            _searchWindow.Show();
        }

        /// <summary>
        /// Читает и раскодирует все xml один раз. Между пачками файлов отдаёт управление интерфейсу
        /// (приоритет Background — ниже ввода и отрисовки), поэтому окно не «замерзает».
        /// </summary>
        public async Task<Dictionary<string, string>?> EnsureSearchIndexAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct)
        {
            if (_searchIndex != null) return _searchIndex;
            var src = _ifaceSource;
            if (src is null) return null;

            var files = _allXml.ToList();
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Count; i++)
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(src, _ifaceSource)) return null;
                try
                {
                    var d = TextCodec.Decode(src.Read(files[i]));
                    if (d.Encoding != null) index[files[i]] = d.Text;
                }
                catch { /* нечитаемый файл просто не попадёт в поиск */ }

                if (i % 8 == 0)
                {
                    progress.Report((i + 1, files.Count));
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
            }
            progress.Report((files.Count, files.Count));

            if (!ReferenceEquals(src, _ifaceSource)) return null;
            _searchIndex = index;
            return index;
        }

        /// <summary>Открывает файл (если нужно) и выделяет элемент с номером ordinal среди детей корня.</summary>
        public void NavigateTo(string file, int ordinal)
        {
            if (_ifaceSource is null) return;

            if (!string.Equals(file, _currentFile, StringComparison.Ordinal))
            {
                if (!ConfirmDiscard()) return;
                FileFilterBox.Text = "";
                LoadFile(file);
                _syncingFileList = true;
                InterfaceFilesList.SelectedItem = file;
                InterfaceFilesList.ScrollIntoView(file);
                _syncingFileList = false;
            }

            if (_document?.Document.Root != null && ordinal >= 0)
            {
                var el = _document.Document.Root.Elements().ElementAtOrDefault(ordinal);
                var node = el == null ? null : _document.Children.FirstOrDefault(c => ReferenceEquals(c.Element, el));
                _selection.Clear();
                if (node != null) _selection.Add(node);
                ApplySelectionChange(scrollCode: true);
            }

            Activate();
        }
    }
}
