using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace UiForge.App
{
    public partial class ImagePickerWindow : Window
    {
        private readonly List<string> _all;

        public string? SelectedFileName { get; private set; }

        public ImagePickerWindow(List<string> candidates)
        {
            InitializeComponent();
            _all = candidates.OrderBy(c => c, System.StringComparer.OrdinalIgnoreCase).ToList();
            FilesList.ItemsSource = _all;
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string f = FilterBox.Text.Trim();
            FilesList.ItemsSource = string.IsNullOrEmpty(f)
                ? _all
                : _all.Where(c => c.Contains(f, System.StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void ChooseButton_Click(object sender, RoutedEventArgs e) => Confirm();
        private void FilesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Confirm();

        private void Confirm()
        {
            if (FilesList.SelectedItem is string path)
            {
                SelectedFileName = path;
                DialogResult = true;
            }
        }
    }
}
