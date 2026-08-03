using System.IO;
using System.Windows;

namespace JustDjvu;

public partial class App : Application
{
    public static ReaderSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = SettingsStore.Load();
        ApplyLanguage(Settings.Language);
        ApplyTheme(Settings.Theme);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        var path = e.Args.FirstOrDefault(File.Exists);
        if (path is null && Settings.ReopenLastDocument &&
            !string.IsNullOrWhiteSpace(Settings.LastDocumentPath) &&
            File.Exists(Settings.LastDocumentPath))
        {
            path = Settings.LastDocumentPath;
        }
        if (path is not null)
        {
            window.OpenFromShell(path);
        }
    }

    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is null)
        {
            return;
        }

        var dark = theme == AppTheme.Dark;
        SetBrush("WindowBrush", dark ? "#181A1F" : "#F4F5F7");
        SetBrush("PanelBrush", dark ? "#202329" : "#FFFFFF");
        SetBrush("PanelAltBrush", dark ? "#262A31" : "#F7F8FA");
        SetBrush("ViewerBrush", dark ? "#111317" : "#292C31");
        SetBrush("TextBrush", dark ? "#F0F2F5" : "#1F2329");
        SetBrush("MutedTextBrush", dark ? "#A6AFBC" : "#677180");
        SetBrush("BorderBrush", dark ? "#3A3F48" : "#D9DDE3");
    }

    public static void ApplyLanguage(AppLanguage language) =>
        LocalizationService.SetLanguage(language);

    private static void SetBrush(string key, string color) =>
        Current.Resources[key] = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
