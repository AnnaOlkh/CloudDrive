using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CloudDrive.Desktop;

public sealed class FolderNode(Guid id, Guid? parentId, string name) : INotifyPropertyChanged
{
    public Guid Id { get; } = id;
    public Guid? ParentId { get; } = parentId;
    public string Name { get; } = name;

    public string FullPath { get; set; } = name;

    public ObservableCollection<FolderNode> Children { get; } = new();
    public bool IsRoot => ParentId is null;
    public string Glyph => IsRoot ? "\uE80F" : "\uE8B7";

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
