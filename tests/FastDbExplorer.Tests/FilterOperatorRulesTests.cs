using FastDbExplorer.Domain;

namespace FastDbExplorer.Tests;

public sealed class FilterOperatorRulesTests
{
    [Fact]
    public void Null_checks_need_no_value()
    {
        Assert.False(FilterOperatorRules.NeedsValue(FilterOperator.IsNull));
        Assert.True(FilterOperatorRules.IsComplete("c", FilterOperator.IsNotNull, null, null));
    }

    [Fact]
    public void Between_needs_both_values()
    {
        Assert.False(FilterOperatorRules.IsComplete("c", FilterOperator.Between, "1", " "));
        Assert.True(FilterOperatorRules.IsComplete("c", FilterOperator.Between, "1", "2"));
    }

    [Fact]
    public void Ordinary_operators_need_one_value_and_no_second()
    {
        Assert.True(FilterOperatorRules.NeedsValue(FilterOperator.Contains));
        Assert.False(FilterOperatorRules.NeedsSecondValue(FilterOperator.Contains));
    }

    [Fact]
    public void A_row_without_a_column_is_never_complete()
    {
        Assert.False(FilterOperatorRules.IsComplete(null, FilterOperator.EqualTo, "1", null));
    }
}
