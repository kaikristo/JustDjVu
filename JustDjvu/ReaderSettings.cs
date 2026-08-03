using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDjvu;

public enum ViewMode
{
    Single,
    Continuous,
    Facing
}

public enum ZoomMode
{
    Custom,
    FitPage,
    FitWidth,
    ActualSize
}

public enum AppTheme
{
    Light,
    Dark
}

public enum AppLanguage
{
    Russian,
    English,
    German,
    French,
    Spanish
}

public sealed class ReaderSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Russian;
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public ViewMode DefaultViewMode { get; set; } = ViewMode.Single;
    public ZoomMode DefaultZoomMode { get; set; } = ZoomMode.FitPage;
    public double DefaultZoom { get; set; } = 100;
    public bool ShowSidebar { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public bool ReopenLastDocument { get; set; } = true;
    public double SidebarWidth { get; set; } = 250;
    public string? LastDocumentPath { get; set; }
    public Dictionary<string, int> LastPages { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<string> RecentFiles { get; set; } = [];
    public Dictionary<string, string> Hotkeys { get; set; } = HotkeyCatalog.CreateDefaults();
    public Dictionary<string, string> SecondaryHotkeys { get; set; } =
        HotkeyCatalog.CreateSecondaryDefaults();
    public Dictionary<string, List<int>> Bookmarks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        if (!Enum.IsDefined(Language))
        {
            Language = AppLanguage.Russian;
        }
        DefaultZoom = Math.Clamp(DefaultZoom, 10, 800);
        SidebarWidth = Math.Clamp(SidebarWidth, 190, 500);
        RecentFiles = RecentFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        LastPages ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Bookmarks ??= new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        Hotkeys ??= HotkeyCatalog.CreateDefaults();
        SecondaryHotkeys ??= HotkeyCatalog.CreateSecondaryDefaults();

        LastPages = new Dictionary<string, int>(
            LastPages.Where(x => x.Value > 0)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        Bookmarks = new Dictionary<string, List<int>>(
            Bookmarks, StringComparer.OrdinalIgnoreCase);
        Hotkeys = new Dictionary<string, string>(
            Hotkeys, StringComparer.OrdinalIgnoreCase);
        SecondaryHotkeys = new Dictionary<string, string>(
            SecondaryHotkeys, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in HotkeyCatalog.Definitions)
        {
            if (!Hotkeys.ContainsKey(definition.Id))
            {
                Hotkeys[definition.Id] = definition.DefaultGesture;
            }
            if (!SecondaryHotkeys.ContainsKey(definition.Id))
            {
                SecondaryHotkeys[definition.Id] = definition.DefaultSecondaryGesture;
            }
        }
    }
}

public sealed record HotkeyDefinition(
    string Id,
    string Title,
    string DefaultGesture,
    string DefaultSecondaryGesture = "");

public static class HotkeyCatalog
{
    public static IReadOnlyList<HotkeyDefinition> Definitions { get; } =
    [
        new("Open", "Открыть документ", "Ctrl+O"),
        new("Close", "Закрыть документ", "Ctrl+W"),
        new("Print", "Печать текущей страницы", "Ctrl+P"),
        new("Search", "Поиск по тексту", "Ctrl+F"),
        new("PreviousPage", "Предыдущая страница", "Left", "WheelUp"),
        new("NextPage", "Следующая страница", "Right", "WheelDown"),
        new("FirstPage", "Первая страница", "Home"),
        new("LastPage", "Последняя страница", "End"),
        new("ZoomIn", "Увеличить", "Ctrl+OemPlus"),
        new("ZoomOut", "Уменьшить", "Ctrl+OemMinus"),
        new("ActualSize", "Масштаб 100%", "Ctrl+D0"),
        new("FitPage", "Вместить страницу", "F"),
        new("FitWidth", "По ширине", "W"),
        new("Rotate", "Повернуть страницу", "Ctrl+R"),
        new("ToggleSidebar", "Показать/скрыть боковую панель", "F4"),
        new("FullScreen", "Полноэкранный режим", "F11"),
        new("Bookmark", "Добавить/удалить закладку", "Ctrl+B")
    ];

    public static Dictionary<string, string> CreateDefaults() =>
        Definitions.ToDictionary(x => x.Id, x => x.DefaultGesture, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, string> CreateSecondaryDefaults() =>
        Definitions.ToDictionary(
            x => x.Id, x => x.DefaultSecondaryGesture, StringComparer.OrdinalIgnoreCase);
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JustDjVu", "settings.json");

    public static ReaderSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<ReaderSettings>(
                    File.ReadAllText(SettingsPath), JsonOptions) ?? new ReaderSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch
        {
            // Повреждённые настройки не должны мешать запуску ридера.
        }

        return new ReaderSettings();
    }

    public static void Save(ReaderSettings settings)
    {
        settings.Normalize();
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }
}
