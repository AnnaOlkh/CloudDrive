using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CloudDrive.Client;
using CloudDrive.Core.Contracts;
using CloudDrive.Core.Files;
using Microsoft.Win32;

namespace CloudDrive.Desktop;

public partial class MainWindow : Window
{
    private const int MaxTextPreviewBytes = 256 * 1024;

    private CloudDriveApiClient? _api;
    private List<FolderDto> _folders = new();
    private List<FileDto> _allFiles = new();
    private readonly Dictionary<Guid, FolderNode> _nodes = new();
    private readonly HashSet<Guid> _expanded = new();
    private FolderNode? _selectedNode;

    private bool _busy;
    private bool _loadingFolders;
    private bool _suppressSelection;
    private bool _suppressSearch;
    private bool _reloadRequested;
    private int _previewGeneration;
    private string? _previewKey;

    private Guid? CurrentFolder => _selectedNode?.Id;
    private FileDto? SelectedFile => FilesGrid.SelectedItem as FileDto;

    public MainWindow()
    {
        InitializeComponent();

        var s = UserSettings.Current;
        MenuSize.IsChecked = s.ShowSize;
        MenuCreated.IsChecked = s.ShowCreated;
        MenuModified.IsChecked = s.ShowModified;
        MenuUploaded.IsChecked = s.ShowUploadedBy;
        MenuEdited.IsChecked = s.ShowEditedBy;
        ApplyColumnVisibility();

        ShowNoSelection();
        UpdateUi();
        Loaded += MainWindow_Loaded;
    }

    // ------------------------------------------------------------------ startup / session

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        if (!ShowLogin()) { Close(); return; }
        await RunAsync("Loading folders", () => RefreshFoldersAsync());
    }

    private bool ShowLogin()
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() != true || login.Api is null) return false;

        _api = login.Api;
        UserNameText.Text = login.UserName;
        UserInitial.Text = login.UserName.Length > 0 ? login.UserName[..1].ToUpperInvariant() : "?";
        return true;
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e) => await SignOutAsync();

    private async Task SignOutAsync()
    {
        _api?.Logout();
        _api = null;

        FolderTree.ItemsSource = null;
        _nodes.Clear();
        _selectedNode = null;
        _allFiles = new List<FileDto>();
        FilesGrid.ItemsSource = null;
        _suppressSearch = true;
        SearchBox.Text = "";
        _suppressSearch = false;
        EmptyState.Visibility = Visibility.Collapsed;
        FolderSummary.Text = "";
        FolderTitle.Text = "My Drive";
        FolderPathText.Text = "";
        ShowNoSelection();
        UpdateUi();

        if (!ShowLogin()) { Close(); return; }
        await RunAsync("Loading folders", () => RefreshFoldersAsync());
    }

    // ------------------------------------------------------------------ busy handling

    private void SetBusy(bool busy, string? operation = null)
    {
        _busy = busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        if (operation is not null) StatusText.Text = operation + "...";
        UpdateUi();
    }

    private async Task RunAsync(string operation, Func<Task> action)
    {
        if (_busy || _api is null) return;

        var sessionExpired = false;
        SetBusy(true, operation);
        try
        {
            await action();
            if (StatusText.Text == operation + "...") StatusText.Text = "Ready";
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            sessionExpired = true;
            StatusText.Text = "Session expired";
        }
        catch (Exception ex)
        {
            StatusText.Text = operation + " failed";
            MessageBox.Show(this, ex.Message, "CloudDrive", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }

        if (sessionExpired)
        {
            MessageBox.Show(this, "Your session has expired. Please sign in again.", "CloudDrive",
                MessageBoxButton.OK, MessageBoxImage.Information);
            await SignOutAsync();
            return;
        }

        // Користувач змінив папку/сортування/фільтр під час виконання іншої операції.
        if (_reloadRequested && _api is not null)
        {
            _reloadRequested = false;
            await RunAsync("Loading files", RefreshFilesAsync);
        }
    }

    private void UpdateUi()
    {
        var idle = !_busy && _api is not null;
        var hasFolder = _selectedNode is not null;
        var hasFile = SelectedFile is not null;
        var canDeleteFolder = idle && hasFolder && !_selectedNode!.IsRoot;

        UploadButton.IsEnabled = idle && hasFolder;
        SyncButton.IsEnabled = idle && hasFolder;
        DownloadButton.IsEnabled = idle && hasFile;
        DeleteButton.IsEnabled = idle && hasFile;
        RefreshButton.IsEnabled = idle;
        NewFolderButton.IsEnabled = idle && hasFolder;
        DeleteFolderButton.IsEnabled = canDeleteFolder;
        DeleteFolderMenuItem.IsEnabled = canDeleteFolder;
    }

    // ------------------------------------------------------------------ folders (tree)

    private FolderNode BuildTree(List<FolderDto> folders)
    {
        _nodes.Clear();
        var root = folders.FirstOrDefault(f => f.ParentId is null)
                   ?? throw new InvalidOperationException("Server returned no root folder.");

        FolderNode Make(FolderDto folder, string parentPath)
        {
            var name = folder.ParentId is null ? "My Drive" : folder.Name;
            var node = new FolderNode(folder.Id, folder.ParentId, name)
            {
                FullPath = parentPath.Length == 0 ? name : parentPath + " / " + name
            };
            _nodes[node.Id] = node;
            foreach (var child in folders.Where(f => f.ParentId == folder.Id)
                         .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                node.Children.Add(Make(child, node.FullPath));
            return node;
        }

        return Make(root, "");
    }

    private static void CollectExpanded(IEnumerable<FolderNode> nodes, HashSet<Guid> into)
    {
        foreach (var n in nodes)
        {
            if (n.IsExpanded) into.Add(n.Id);
            CollectExpanded(n.Children, into);
        }
    }

    private async Task RefreshFoldersAsync(Guid? selectId = null)
    {
        var wanted = selectId ?? CurrentFolder;
        if (FolderTree.ItemsSource is IEnumerable<FolderNode> old) CollectExpanded(old, _expanded);

        _folders = await _api!.GetFoldersAsync();
        var root = BuildTree(_folders);

        foreach (var n in _nodes.Values)
            if (n.IsRoot || _expanded.Contains(n.Id)) n.IsExpanded = true;

        var target = wanted is Guid id && _nodes.TryGetValue(id, out var found) ? found : root;
        var cursor = target;
        while (cursor.ParentId is Guid parentId && _nodes.TryGetValue(parentId, out var parent))
        {
            parent.IsExpanded = true;
            cursor = parent;
        }

        _loadingFolders = true;
        try
        {
            target.IsSelected = true;
            _selectedNode = target;
            FolderTree.ItemsSource = new[] { root };
        }
        finally { _loadingFolders = false; }

        UpdateFolderHeader();
        await RefreshFilesAsync();
    }

    private void UpdateFolderHeader()
    {
        var node = _selectedNode;
        FolderTitle.Text = node?.Name ?? "My Drive";
        FolderPathText.Text = node is null ? "" : node.IsRoot ? "Your personal storage" : node.FullPath;
        DropTargetText.Text = node is null ? "" : "to " + node.FullPath;
    }

    private async void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_loadingFolders || e.NewValue is not FolderNode node || ReferenceEquals(node, _selectedNode)) return;

        _selectedNode = node;
        UpdateFolderHeader();
        UpdateUi();
        _suppressSearch = true;
        SearchBox.Text = "";
        _suppressSearch = false;

        if (_busy) { _reloadRequested = true; return; }
        await RunAsync("Loading files", RefreshFilesAsync);
    }

    private void FolderTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        item.IsSelected = true;
        item.Focus();
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is not FolderNode parent) return;
        var dialog = new NameDialog(parent.FullPath) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await RunAsync("Creating folder", async () =>
        {
            await _api!.CreateFolderAsync(dialog.FolderName, parent.Id);
            _expanded.Add(parent.Id);
            await RefreshFoldersAsync(parent.Id);
            StatusText.Text = $"Created folder “{dialog.FolderName}”";
        });
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode is not FolderNode node || node.IsRoot) return;

        var answer = MessageBox.Show(this,
            $"Permanently delete “{node.FullPath}” and ALL of its files and subfolders?\n\nThis cannot be undone.",
            "Delete folder", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        await RunAsync("Deleting folder", async () =>
        {
            await _api!.DeleteFolderAsync(node.Id);
            await RefreshFoldersAsync(node.ParentId);
            StatusText.Text = $"Deleted folder “{node.Name}”";
        });
    }

    // ------------------------------------------------------------------ file list

    private SortOrder SelectedSort() =>
        Enum.TryParse<SortOrder>((SortCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var s) ? s : SortOrder.NameAsc;

    private FileFilter SelectedFilter() =>
        Enum.TryParse<FileFilter>((FilterCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var f) ? f : FileFilter.All;

    private async Task RefreshFilesAsync()
    {
        if (CurrentFolder is not Guid folderId) return;
        _allFiles = await _api!.GetFilesAsync(folderId, SelectedSort(), SelectedFilter());
        ShowFiles();
    }

    /// <summary>Показує завантажений список з урахуванням пошуку (пошук працює локально, без запитів).</summary>
    private void ShowFiles()
    {
        var query = SearchBox.Text.Trim();
        var list = query.Length == 0
            ? _allFiles
            : _allFiles.Where(f => f.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var keep = SelectedFile?.Id;
        _suppressSelection = true;
        try
        {
            FilesGrid.ItemsSource = list;
            if (keep is Guid id) FilesGrid.SelectedItem = list.FirstOrDefault(f => f.Id == id);
        }
        finally { _suppressSelection = false; }

        ApplySortIndicator();
        UpdateEmptyState(list.Count, query);
        FolderSummary.Text = list.Count == 0 ? ""
            : query.Length > 0 ? $"{list.Count} of {_allFiles.Count} files"
            : $"{list.Count} file{(list.Count == 1 ? "" : "s")} · {FileSizeConverter.Format(list.Sum(f => f.Size))}";

        UpdateUi();
        _ = UpdatePreviewAsync();
    }

    private void ApplySortIndicator()
    {
        foreach (var column in FilesGrid.Columns) column.SortDirection = null;
        NameColumn.SortDirection = SelectedSort() == SortOrder.NameAsc
            ? ListSortDirection.Ascending : ListSortDirection.Descending;
    }

    private void UpdateEmptyState(int visibleCount, string query)
    {
        if (visibleCount > 0) { EmptyState.Visibility = Visibility.Collapsed; return; }

        if (query.Length > 0)
        {
            EmptyTitle.Text = "No matches";
            EmptyHint.Text = $"Nothing in this folder matches “{query}”.";
        }
        else if (SelectedFilter() != FileFilter.All)
        {
            EmptyTitle.Text = "No files of this type";
            EmptyHint.Text = "Change the filter to see everything in this folder.";
        }
        else
        {
            EmptyTitle.Text = "This folder is empty";
            EmptyHint.Text = "Drop files anywhere in this window, or click Upload.";
        }
        EmptyState.Visibility = Visibility.Visible;
    }

    private async void Query_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _api is null || _selectedNode is null) return;
        if (_busy) { _reloadRequested = true; return; }
        await RunAsync("Loading files", RefreshFilesAsync);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RunAsync("Refreshing", () => RefreshFoldersAsync());

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressSearch && IsLoaded) ShowFiles();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        SearchBox.Clear();
        FilesGrid.Focus();
        e.Handled = true;
    }

    /// <summary>Клік по заголовку "Name" перемикає серверне сортування A→Z / Z→A; інші колонки сортуються локально.</summary>
    private void FilesGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (!ReferenceEquals(e.Column, NameColumn)) return;
        e.Handled = true;
        SortCombo.SelectedIndex = SortCombo.SelectedIndex == 0 ? 1 : 0; // викликає Query_Changed
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        UpdateUi();
        _ = UpdatePreviewAsync();
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is not null && SelectedFile is not null)
            Download_Click(sender, e);
    }

    private void FilesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || SelectedFile is null) return;
        Delete_Click(sender, e);
        e.Handled = true;
    }

    private void FilesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is not null) row.IsSelected = true;
    }

    private void FilesGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SelectedFile is null || _busy) e.Handled = true;
    }

    // ------------------------------------------------------------------ columns

    private void ApplyColumnVisibility()
    {
        static Visibility V(MenuItem item) => item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        SizeColumn.Visibility = V(MenuSize);
        CreatedColumn.Visibility = V(MenuCreated);
        ModifiedColumn.Visibility = V(MenuModified);
        UploadedColumn.Visibility = V(MenuUploaded);
        EditedColumn.Visibility = V(MenuEdited);
    }

    private void Columns_Click(object sender, RoutedEventArgs e)
    {
        ColumnsMenu.PlacementTarget = ColumnsButton;
        ColumnsMenu.Placement = PlacementMode.Bottom;
        ColumnsMenu.IsOpen = true;
    }

    private void ColumnMenu_Click(object sender, RoutedEventArgs e)
    {
        ApplyColumnVisibility();
        var s = UserSettings.Current;
        s.ShowSize = MenuSize.IsChecked;
        s.ShowCreated = MenuCreated.IsChecked;
        s.ShowModified = MenuModified.IsChecked;
        s.ShowUploadedBy = MenuUploaded.IsChecked;
        s.ShowEditedBy = MenuEdited.IsChecked;
        s.Save();
    }

    // ------------------------------------------------------------------ upload / download / delete

    private async Task UploadPathsAsync(IEnumerable<string> paths)
    {
        if (CurrentFolder is not Guid folderId) return;

        var dropped = paths.ToList();
        var files = dropped.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            StatusText.Text = dropped.Any(Directory.Exists)
                ? "Folders can't be dropped – use “Sync folder” to transfer whole folders."
                : "Nothing to upload.";
            return;
        }

        await RunAsync("Uploading", async () =>
        {
            // Повний список без фільтра, щоб виявити файли з тим самим імʼям навіть якщо їх приховано фільтром.
            var existing = (await _api!.GetFilesAsync(folderId)).ToDictionary(f => f.Name, StringComparer.Ordinal);
            int uploaded = 0, replaced = 0, skipped = 0;
            var failures = new List<string>();

            for (var i = 0; i < files.Count; i++)
            {
                var path = files[i];
                var name = Path.GetFileName(path);
                StatusText.Text = $"Uploading {i + 1} of {files.Count}: {name}";

                try
                {
                    if (existing.TryGetValue(name, out var current))
                    {
                        var answer = MessageBox.Show(this,
                            $"“{name}” already exists in this folder.\n\nReplace it with the file you are uploading?\n\n" +
                            "Yes – replace      No – skip      Cancel – stop uploading",
                            "File already exists", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                        if (answer == MessageBoxResult.Cancel) break;
                        if (answer == MessageBoxResult.No) { skipped++; continue; }

                        await using var replacement = File.OpenRead(path);
                        await _api.ReplaceAsync(current.Id, name, replacement, current.Version);
                        replaced++;
                    }
                    else
                    {
                        await using var stream = File.OpenRead(path);
                        await _api.UploadAsync(name, stream, folderId);
                        uploaded++;
                    }
                }
                catch (Exception ex) when (ex is ApiException or IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{name}: {ex.Message}");
                }
            }

            await RefreshFilesAsync();

            var parts = new List<string>();
            if (uploaded > 0) parts.Add($"{uploaded} uploaded");
            if (replaced > 0) parts.Add($"{replaced} replaced");
            if (skipped > 0) parts.Add($"{skipped} skipped");
            if (failures.Count > 0) parts.Add($"{failures.Count} failed");
            StatusText.Text = parts.Count == 0 ? "Upload cancelled" : "Upload finished: " + string.Join(", ", parts);

            if (failures.Count > 0)
                MessageBox.Show(this, "Some files could not be uploaded:\n\n" + string.Join("\n", failures.Take(10)),
                    "Upload", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selectedNode is null) return;
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose files to upload" };
        if (dialog.ShowDialog(this) == true) await UploadPathsAsync(dialog.FileNames);
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || SelectedFile is not FileDto file) return;
        var dialog = new SaveFileDialog { FileName = file.Name, Title = "Save file" };
        if (dialog.ShowDialog(this) != true) return;

        await RunAsync("Downloading", async () =>
        {
            var temp = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var target = File.Create(temp))
                    await _api!.DownloadAsync(file.Id, target);
                File.Move(temp, dialog.FileName, overwrite: true);
                StatusText.Text = $"Saved “{file.Name}” ({FileSizeConverter.Format(file.Size)})";
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || SelectedFile is not FileDto file) return;
        var answer = MessageBox.Show(this, $"Delete “{file.Name}” from your drive?\n\nThis cannot be undone.",
            "Delete file", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        await RunAsync("Deleting", async () =>
        {
            await _api!.DeleteAsync(file.Id);
            await RefreshFilesAsync();
            StatusText.Text = $"Deleted “{file.Name}”";
        });
    }

    // ------------------------------------------------------------------ drag & drop

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var accept = !_busy && _selectedNode is not null && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = accept ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    // DragOver надходить безперервно, тому короткий "blink" між дочірніми елементами непомітний.
    private void Window_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await UploadPathsAsync(paths);
    }

    // ------------------------------------------------------------------ keyboard

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_api is null) return;

        if (e.Key == Key.F5)
        {
            Refresh_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.U)
        {
            Upload_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ synchronization

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selectedNode is not FolderNode node) return;

        var picker = new OpenFolderDialog { Title = "Choose the local folder to synchronize" };
        if (picker.ShowDialog(this) != true) return;

        var confirm = MessageBox.Show(this,
            $"Synchronize\n{picker.FolderName}\n\nwith\n{node.FullPath} ?\n\n" +
            "New and changed files are copied in both directions. Deletions are only applied after you confirm them, " +
            "and files changed on both sides are never overwritten.",
            "Synchronize folders", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        await RunAsync("Synchronizing", async () =>
        {
            var service = new SyncService(_api!, new SyncManifestStore());
            var result = await service.RunAsync(picker.FolderName, node.Id, plan =>
            {
                var deletions = plan.ToDeleteLocal.Select(p => (Path: p, Action: "Delete locally"))
                    .Concat(plan.ToDeleteRemote.Select(p => (Path: p, Action: "Delete on CloudDrive"))).ToList();
                if (deletions.Count == 0)
                    return Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());

                var dialog = new SyncDeletionsWindow(deletions) { Owner = this };
                return Task.FromResult<IReadOnlyCollection<string>>(
                    dialog.ShowDialog() == true ? dialog.ApprovedPaths : Array.Empty<string>());
            });

            await RefreshFoldersAsync(node.Id);
            StatusText.Text = $"Sync finished: {result.Completed.Count} done, {result.Plan.Conflicts.Count} conflicts, {result.Failures.Count} failed";
            new SyncResultWindow(result, picker.FolderName, node.FullPath) { Owner = this }.ShowDialog();
        });
    }

    // ------------------------------------------------------------------ details & preview

    private void ShowNoSelection()
    {
        _previewGeneration++;
        _previewKey = null;
        PreviewContent.Visibility = Visibility.Collapsed;
        PreviewEmpty.Visibility = Visibility.Visible;
    }

    private void ShowPreviewMessage(string message)
    {
        TextPreview.Visibility = Visibility.Collapsed;
        ImagePreview.Visibility = Visibility.Collapsed;
        ImagePreview.Source = null;
        PreviewHint.Text = message;
        PreviewHint.Visibility = Visibility.Visible;
    }

    private async Task UpdatePreviewAsync()
    {
        var file = SelectedFile;
        if (file is null) { ShowNoSelection(); return; }

        // Деталі
        var icons = (FileIconConverter)FindResource("FileIcon");
        PreviewIcon.Text = (string)icons.Convert(file.Name, typeof(string), "glyph", CultureInfo.CurrentCulture);
        PreviewIcon.Foreground = (Brush)icons.Convert(file.Name, typeof(Brush), "brush", CultureInfo.CurrentCulture);
        PreviewTitle.Text = file.Name;
        DetailSize.Text = FileSizeConverter.Format(file.Size);
        DetailCreated.Text = LocalTimeConverter.Format(file.CreatedAt);
        DetailModified.Text = LocalTimeConverter.Format(file.ModifiedAt);
        DetailUploaded.Text = file.UploadedBy;
        DetailEdited.Text = file.LastEditedBy;
        PreviewEmpty.Visibility = Visibility.Collapsed;
        PreviewContent.Visibility = Visibility.Visible;

        // Превʼю: той самий файл тієї ж версії повторно не завантажуємо
        var key = $"{file.Id}:{file.Version}";
        if (key == _previewKey) return;
        _previewKey = key;
        var generation = ++_previewGeneration;

        var kind = FileTypeHelper.GetPreviewKind(file.Name);
        if (kind == PreviewKind.None)
        {
            ShowPreviewMessage("No preview for this file type.\nPreview is available for .c and .jpg files.");
            return;
        }

        ShowPreviewMessage("Loading preview...");
        try
        {
            var bytes = await _api!.GetContentAsync(file.Id);
            if (generation != _previewGeneration) return;

            if (kind == PreviewKind.Text)
            {
                var length = Math.Min(bytes.Length, MaxTextPreviewBytes);
                TextPreview.Text = Encoding.UTF8.GetString(bytes, 0, length) +
                                   (bytes.Length > length ? "\n\n… preview truncated" : "");
                TextPreview.Visibility = Visibility.Visible;
                ImagePreview.Visibility = Visibility.Collapsed;
            }
            else
            {
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                ImagePreview.Source = bitmap;
                ImagePreview.Visibility = Visibility.Visible;
                TextPreview.Visibility = Visibility.Collapsed;
            }
            PreviewHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (generation != _previewGeneration) return;
            _previewKey = null; // дозволяє повторити спробу при наступному виборі
            ShowPreviewMessage("Preview unavailable: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
