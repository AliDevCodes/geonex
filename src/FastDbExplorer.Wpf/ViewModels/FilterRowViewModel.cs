using CommunityToolkit.Mvvm.ComponentModel;
using FastDbExplorer.Domain;
using FastDbExplorer.Wpf.Localization;

namespace FastDbExplorer.Wpf.ViewModels;

public sealed record OperatorOption(FilterOperator Operator, string Label)
{
    public static IReadOnlyList<OperatorOption> All { get; } =
    [
        new(FilterOperator.EqualTo,       FilterStrings.OpEqual),
        new(FilterOperator.NotEqualTo,    FilterStrings.OpNotEqual),
        new(FilterOperator.GreaterThan,   FilterStrings.OpGreater),
        new(FilterOperator.GreaterOrEqual,FilterStrings.OpGreaterOrEqual),
        new(FilterOperator.LessThan,      FilterStrings.OpLess),
        new(FilterOperator.LessOrEqual,   FilterStrings.OpLessOrEqual),
        new(FilterOperator.Contains,      FilterStrings.OpContains),
        new(FilterOperator.StartsWith,    FilterStrings.OpStartsWith),
        new(FilterOperator.EndsWith,      FilterStrings.OpEndsWith),
        new(FilterOperator.Between,       FilterStrings.OpBetween),
        new(FilterOperator.In,            FilterStrings.OpIn),
        new(FilterOperator.IsNull,        FilterStrings.OpIsNull),
        new(FilterOperator.IsNotNull,     FilterStrings.OpIsNotNull)
    ];
}

public sealed partial class FilterRowViewModel : ObservableObject
{
    private static readonly HashSet<string> DateTimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "date", "datetime", "datetime2", "smalldatetime"
    };

    public IReadOnlyList<ColumnInfo>? ColumnMeta { get; init; }
    public string? ColumnType { get; init; }

    public FilterRowViewModel(string? column, string? columnType = null)
    {
        _column = column;
        ColumnType = columnType;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsValue))]
    [NotifyPropertyChangedFor(nameof(NeedsSecondValue))]
    [NotifyPropertyChangedFor(nameof(IsDateTimeField))]
    private string? _column;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsValue))]
    [NotifyPropertyChangedFor(nameof(NeedsSecondValue))]
    private OperatorOption _selectedOperator = OperatorOption.All[0];

    [ObservableProperty] private string _value  = "";
    [ObservableProperty] private string _value2 = "";

    public bool NeedsValue       => FilterOperatorRules.NeedsValue(SelectedOperator.Operator);
    public bool NeedsSecondValue => FilterOperatorRules.NeedsSecondValue(SelectedOperator.Operator);

    /// <summary>A column is chosen, and every value the operator needs is filled in.</summary>
    public bool IsComplete => FilterOperatorRules.IsComplete(Column, SelectedOperator.Operator, Value, Value2);

    /// <summary>The condition this row describes. Values the operator does not use are left out.</summary>
    public FilterCondition ToCondition() => new(Column!, SelectedOperator.Operator,
        NeedsValue ? Value : null, NeedsSecondValue ? Value2 : null);

    public bool IsDateTimeField
    {
        get
        {
            // Use the generated Column property, not _column, to satisfy MVVMTK0034
            if (Column is null) return false;
            if (ColumnMeta is not null)
            {
                var col = ColumnMeta.FirstOrDefault(c => c.Name == Column);
                if (col is not null) return DateTimeTypes.Contains(col.TypeName);
            }
            if (ColumnType is not null) return DateTimeTypes.Contains(ColumnType);
            return false;
        }
    }
}
