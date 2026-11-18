using CommunityToolkit.Mvvm.ComponentModel;

namespace FastDbExplorer.Wpf.ViewModels.Search;

/// <summary>One table column offered as a detail on the map card; <paramref name="onChanged"/> enforces the detail limit.</summary>
public sealed partial class MapDetailColumnItem(string name, bool isSelected, Action<MapDetailColumnItem, bool> onChanged)
    : ObservableObject
{
    public string Name => name;

    [ObservableProperty] private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => onChanged(this, value);
}
