namespace FastDbExplorer.Wpf.Localization;

/// <summary>Status texts of the saved-filter chips and the save line in the explorer.</summary>
public static class SavedFilterStrings
{
    public const string NameRequired = "برای ذخیره، یک نام وارد کنید.";
    public const string NoFilters = "ابتدا حداقل یک فیلتر تعریف کنید.";

    public static string Saved(string name) => $"فیلتر «{name}» ذخیره شد.";
    public static string Applied(string name) => $"فیلتر «{name}» اعمال شد.";
    public static string Deleted(string name) => $"فیلتر «{name}» حذف شد.";
    public static string LoadFailed(string detail) => "خواندن فیلترهای ذخیره‌شده ممکن نشد: " + detail;
    public static string SaveFailed(string detail) => "ذخیره فیلتر ممکن نشد: " + detail;
    public static string DeleteFailed(string detail) => "حذف فیلتر ممکن نشد: " + detail;
}
