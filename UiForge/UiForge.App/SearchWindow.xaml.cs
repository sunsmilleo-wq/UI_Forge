using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace UiForge.App
{
    public sealed class SearchHit
    {
        public string File { get; init; } = "";
        public string ElementLabel { get; init; } = "";
        public int Ordinal { get; init; } = -1;
        public string Attr { get; init; } = "";
        public string Snippet { get; init; } = "";
        public string AttrBadge => string.IsNullOrEmpty(Attr) ? "" : $"   [{Attr}]";
    }

    public partial class SearchWindow : Window
    {
        private enum Cat { File, Name, Text, Image, Other }

        private const int MaxHits = 1000;
        private static readonly Regex TagRx = new(@"<(/?)([A-Za-z_][\w.\-]*)((?:[^<>""']|""[^""]*""|'[^']*')*?)(/?)>", RegexOptions.Compiled);
        private static readonly Regex AttrRx = new(@"([A-Za-z_][\w.\-]*)\s*=\s*""([^""]*)""", RegexOptions.Compiled);

        private readonly MainWindow _main;
        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private CancellationTokenSource? _cts;
        private Dictionary<string, string>? _index;

        public SearchWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            _debounce.Tick += (_, _) => { _debounce.Stop(); RunSearch(); };
            Loaded += async (_, _) => { QueryBox.Focus(); await BuildIndexAsync(); };
            Closed += (_, _) => _cts?.Cancel();
        }

        public void FocusQuery()
        {
            QueryBox.Focus();
            QueryBox.SelectAll();
        }

        private async System.Threading.Tasks.Task BuildIndexAsync()
        {
            _cts = new CancellationTokenSource();
            StatusText.Text = "Индексирую файлы…";

            var progress = new Progress<(int Done, int Total)>(p =>
            {
                Bar.Value = p.Total == 0 ? 0 : (double)p.Done / p.Total;
                StatusText.Text = $"Индексирую файлы… {p.Done} из {p.Total}";
            });

            _index = await _main.EnsureSearchIndexAsync(progress, _cts.Token);
            Bar.Value = 0;

            if (_index is null) { StatusText.Text = "Индексация прервана."; return; }
            StatusText.Text = $"Готово: в поиске {_index.Count} файлов. Двойной щелчок или Enter — открыть найденное.";
            RunSearch();
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        private void Filter_Click(object sender, RoutedEventArgs e) => RunSearch();

        // ---------- поиск ----------

        private bool Enabled(Cat c) => c switch
        {
            Cat.File => CatFiles.IsChecked == true,
            Cat.Name => CatNames.IsChecked == true,
            Cat.Text => CatText.IsChecked == true,
            Cat.Image => CatImages.IsChecked == true,
            _ => CatOther.IsChecked == true
        };

        private static Cat Classify(string attr) => attr.ToLowerInvariant() switch
        {
            "name" => Cat.Name,
            "filename" => Cat.Image,
            "string" or "hint" or "dialogfile" => Cat.Text,
            _ => Cat.Other
        };

        private void RunSearch()
        {
            string q = QueryBox.Text.Trim();
            if (_index is null || q.Length == 0)
            {
                ResultsList.ItemsSource = null;
                return;
            }

            var hits = new List<SearchHit>();
            bool truncated = false;

            foreach (var kv in _index.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (Enabled(Cat.File) && kv.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
                    hits.Add(new SearchHit { File = kv.Key, ElementLabel = "(имя файла)", Snippet = kv.Key });

                SearchInText(kv.Key, kv.Value, q, hits);
                if (hits.Count >= MaxHits) { truncated = true; break; }
            }

            ResultsList.ItemsSource = hits;
            StatusText.Text = hits.Count == 0
                ? $"«{q}» — ничего не найдено."
                : $"Найдено: {hits.Count}{(truncated ? " (показаны первые " + MaxHits + " — уточните запрос)" : "")}. Двойной щелчок или Enter — открыть.";
        }

        /// <summary>Ищет по строкам и запоминает, к какому элементу (ребёнку корня) строка относится.</summary>
        private void SearchInText(string file, string text, string q, List<SearchHit> hits)
        {
            int depth = 0, ordinal = -1;
            string elTag = "окно", elName = "";

            var lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (var rawLine in lines)
            {
                foreach (Match m in TagRx.Matches(rawLine))
                {
                    if (m.Groups[1].Value == "/") { depth--; continue; }

                    if (depth == 1)
                    {
                        ordinal++;
                        elTag = m.Groups[2].Value;
                        var nm = AttrRx.Matches(m.Groups[3].Value).Cast<Match>()
                            .FirstOrDefault(a => a.Groups[1].Value.Equals("Name", StringComparison.OrdinalIgnoreCase));
                        elName = nm?.Groups[2].Value ?? "";
                    }
                    if (depth == 0) { elTag = "окно"; elName = AttrName(m.Groups[3].Value); }
                    if (m.Groups[4].Value != "/") depth++;
                }

                if (rawLine.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;

                string attr = "";
                Cat cat = Cat.Other;
                foreach (Match a in AttrRx.Matches(rawLine))
                {
                    if (a.Groups[2].Value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        attr = a.Groups[1].Value;
                        cat = Classify(attr);
                        break;
                    }
                }
                if (!Enabled(cat)) continue;

                string label = depth == 0 && ordinal < 0 ? "окно" : (elName.Length > 0 ? $"{elName} ({elTag})" : elTag);
                hits.Add(new SearchHit
                {
                    File = file,
                    ElementLabel = label,
                    Ordinal = ordinal,
                    Attr = attr,
                    Snippet = MakeSnippet(rawLine.Trim(), q)
                });
                if (hits.Count >= MaxHits) return;
            }
        }

        private static string AttrName(string attrs)
        {
            var nm = AttrRx.Matches(attrs).Cast<Match>()
                .FirstOrDefault(a => a.Groups[1].Value.Equals("Name", StringComparison.OrdinalIgnoreCase));
            return nm?.Groups[2].Value ?? "";
        }

        private static string MakeSnippet(string line, string q)
        {
            if (line.Length <= 150) return line;
            int i = Math.Max(0, line.IndexOf(q, StringComparison.OrdinalIgnoreCase) - 50);
            string part = line.Substring(i, Math.Min(150, line.Length - i));
            return (i > 0 ? "… " : "") + part + (i + part.Length < line.Length ? " …" : "");
        }

        // ---------- открыть найденное ----------

        private void OpenSelected()
        {
            if (ResultsList.SelectedItem is SearchHit hit) _main.NavigateTo(hit.File, hit.Ordinal);
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

        private void ResultsList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        }
    }
}
