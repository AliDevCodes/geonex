namespace FastDbExplorer.Wpf.Localization;

/// <summary>Text of the app shell: navigation rail, top bar, home dashboard, "connect first" prompts.</summary>
public static class ShellStrings
{
    public const string NavHome = "خانه";
    public const string NavConnections = "اتصال‌ها";
    public const string NavExplorer = "کاوشگر";
    public const string NavMap = "نقشه";
    public const string NavMonitoring = "مانیتورینگ";

    public const string NotConnected = "متصل نیستید";
    public const string ActiveDatabase = "پایگاه‌دادهٔ فعال";

    public const string ExplorerPromptTitle = "برای کاوش در جدول‌ها ابتدا به سرور وصل شوید";
    public const string ExplorerPromptBody = "پس از اتصال، پایگاه‌داده‌ها و جدول‌ها همین‌جا نمایش داده می‌شوند. نقشه بدون اتصال هم کار می‌کند.";
    public const string MonitoringPromptTitle = "برای مانیتورینگ داده‌های جدید ابتدا به سرور وصل شوید";
    public const string MonitoringPromptBody = "مانیتورها تا وقتی به سرور وصل هستید و برنامه باز است، در پس‌زمینه اجرا می‌شوند.";
    public const string GoToConnect = "رفتن به اتصال‌ها";
    public const string HomePromptTitle = "محیط دادهٔ شما آماده است";
    public const string HomePromptBody = "برای شروع یک پروفایل اتصال بسازید یا از پروفایل‌های ذخیره‌شده انتخاب کنید. رمز پایگاه‌داده ذخیره نمی‌شود.";

    public const string ConnectionsTitle = "پروفایل‌های اتصال";
    public const string ConnectionsSubtitle = "نام‌های دلخواه برای سرورها ذخیره کنید و هر زمان بین آن‌ها جابه‌جا شوید.";
    public const string SavedProfiles = "ذخیره‌شده‌ها";
    public const string NoSavedProfiles = "هنوز اتصالی ذخیره نشده است.\nبرای ساخت اولین پروفایل روی + بزنید.";
    public const string ProfilePasswordStorageNote = "رمز SQL ذخیره نمی‌شود؛ هنگام اتصال دوباره از شما پرسیده خواهد شد.";
    public const string ProfileDetails = "جزئیات پروفایل";
    public const string ProfileName = "نام دلخواه";
    public const string NewProfile = "پروفایل تازه";
    public const string SaveProfile = "ذخیره‌ی پروفایل";
    public const string DeleteProfile = "حذف";
    public const string ConnectProfile = "اتصال";
    public const string ProfileSaved = "پروفایل ذخیره شد.";
    public const string ProfileDeleted = "پروفایل حذف شد.";
    public const string ProfileFieldsRequired = "نام پروفایل و آدرس سرور را وارد کنید؛ برای احراز هویت SQL نام کاربری هم لازم است.";
    public const string ProfileNameTaken = "این نام قبلاً استفاده شده است؛ نام دیگری انتخاب کنید.";
    public const string ProfileUnavailable = "این پروفایل دیگر در فهرست اتصال‌ها نیست.";
    public const string SqlPasswordTitle = "رمز عبور پایگاه‌داده";
    public const string SqlPasswordPromptNote = "رمز فقط برای این نشست استفاده می‌شود و در پروفایل یا فایل تنظیمات ذخیره نخواهد شد.";
    public const string PasswordPromptUnavailable = "پنجره‌ی امن ورود رمز در دسترس نیست.";
    public const string ActiveProfile = "پروفایل فعال";
    public const string WriteAccessWarning =
        "کاربر این اتصال دسترسی نوشتن در پایگاه داده دارد (نقش نوشتن، مالک دیتابیس، sysadmin یا مجوز تغییر).\n\n"
        + "برنامه فقط خواندنی است و چیزی نمی‌نویسد، اما برای امنیت بهتر است از یک کاربر فقط‑خواندنی استفاده کنید.";

    public const string HomeWelcome = "میز کار کاوش";
    public const string HomeSubtitle = "از جدول‌ها شروع کنید؛ نقشه و مانیتورینگ در کنار شماست.";
    public const string ExplorerTitle = "کاوش داده‌ها";
    public const string ExplorerTileHint = "جدول‌ها را ببینید و داده‌ها را فیلتر کنید.";
    public const string ExplorerWorkflow = "انتخاب پایگاه‌داده، انتخاب جدول، اعمال فیلتر";
    public const string MapTileHint = "نقشه‌ها و لایه‌های مختصات را ببینید.";
    public const string MonitoringTileHint = "ردیف‌های جدید مطابق شرط را زیر نظر بگیرید.";
    public const string OpenExplorer = "باز کردن کاوشگر";
    public const string OpenMap = "باز کردن نقشه";
    public const string OpenMonitoring = "باز کردن مانیتورینگ";
    public const string LatestAlerts = "آخرین هشدارهای نشست";
    public const string NoAlertsYet = "در این نشست هنوز هشداری ثبت نشده است.";
    public const string NoMonitorsHint = "برای شروع، در بخش مانیتورینگ یک مانیتور تعریف کنید.";
    public const string ExistingMonitorsHint = "وضعیت بررسی‌ها را در بخش مانیتورینگ ببینید.";
    public const string AlertMatch = "تطبیق شرط";
    public const string AlertCheckFailed = "خطای بررسی";

    public const string Brand1 = "کاوش جدول‌ها و فیلتر روی داده‌ها؛ فقط‌خواندنی و امن.";
    public const string Brand2 = "نقشه‌ی تعاملی با لایه‌های مختصات؛ بدون نیاز به اتصال.";
    public const string Brand3 = "مانیتورینگ ردیف‌های جدید و هشدار لحظه‌ای.";

    public const string SearchButton = "جستجو در جدول‌ها، مانیتورها و بخش‌ها…";
    public const string PaletteHint = "جستجوی جدول، مانیتور یا بخش…";
    public const string PaletteEmpty = "نتیجه‌ای یافت نشد.";
    public const string KindSection = "بخش";
    public const string KindTable = "جدول";
    public const string KindMonitor = "مانیتور";
    public const string KindAction = "دستور";

    public static string RowsApprox(object rows) => $"حدود {rows:N0} ردیف";

    public static string WithShortcut(string title, int number) => $"{title} (Ctrl+{number})";

    public static string DatabasesCount(int count) => $"{count:N0} پایگاه‌داده";

    public static string MonitorsCount(int count) =>
        count == 0 ? "هنوز مانیتوری تعریف نشده است." : $"{count:N0} مانیتور تعریف شده است.";
}
