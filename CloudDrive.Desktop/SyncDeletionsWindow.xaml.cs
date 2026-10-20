using System.ComponentModel;
using System.Windows;

namespace CloudDrive.Desktop;

public partial class SyncDeletionsWindow : Window
{
    public sealed class DeletionItem : INotifyPropertyChanged
    {
        public string Path { get; init; } = "";
        public string Action { get; init; } = "";

        private bool _selected = true;
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public IReadOnlyList<DeletionItem> Items { get; }
    public IReadOnlyCollection<string> ApprovedPaths { get; private set; } = Array.Empty<string>();

    public SyncDeletionsWindow(IEnumerable<(string Path, string Action)> deletions)
    {
        Items = deletions.Select(x => new DeletionItem { Path = x.Path, Action = x.Action }).ToList();
        InitializeComponent();
        DataContext = this;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var i in Items) i.Selected = true; }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var i in Items) i.Selected = false; }

    private void KeepAll_Click(object sender, RoutedEventArgs e)
    {
        ApprovedPaths = Array.Empty<string>();
        DialogResult = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ApprovedPaths = Items.Where(x => x.Selected).Select(x => x.Path).ToArray();
        DialogResult = true;
    }
}
