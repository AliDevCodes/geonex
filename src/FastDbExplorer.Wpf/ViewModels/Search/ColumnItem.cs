using CommunityToolkit.Mvvm.ComponentModel;
using FastDbExplorer.Domain;
using FastDbExplorer.Domain.Search;

namespace FastDbExplorer.Wpf.ViewModels.Search;

/// <summary>A table column in the column picker; the selected ones are the grid's display columns.</summary>
public sealed partial class ColumnItem(ColumnInfo info) : ObservableObject
{
    public string Name => info.Name;
    public string TypeName => info.TypeName;
    public bool IsKey => info.IsPrimaryKey;

    [ObservableProperty] private bool _isSelected;
}
