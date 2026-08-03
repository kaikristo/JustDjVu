using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JustDjvu;

public partial class SettingsWindow : Window
{
    private readonly ReaderSettings _settings;
    private readonly ObservableCollection<HotkeyRow> _hotkeys;

    public SettingsWindow(ReaderSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _hotkeys = new ObservableCollection<HotkeyRow>(
            HotkeyCatalog.Definitions.Select(x => new HotkeyRow(
                x.Id,
                LocalizationService.Translate(x.Title),
                settings.Hotkeys.GetValueOrDefault(x.Id, x.DefaultGesture),
                settings.SecondaryHotkeys.GetValueOrDefault(
                    x.Id, x.DefaultSecondaryGesture))));
        HotkeysGrid.ItemsSource = _hotkeys;

        SelectByTag(LanguageCombo, settings.Language.ToString());
        SelectByTag(ThemeCombo, settings.Theme.ToString());
        SelectByTag(ViewModeCombo, settings.DefaultViewMode.ToString());
        SelectByTag(ZoomModeCombo, settings.DefaultZoomMode.ToString());
        DefaultZoomSlider.Value = settings.DefaultZoom;
        ReopenLastDocumentCheck.IsChecked = settings.ReopenLastDocument;
        ShowSidebarCheck.IsChecked = settings.ShowSidebar;
        ShowToolbarCheck.IsChecked = settings.ShowToolbar;
        UpdateZoomControls();
        Title = LocalizationService.Translate("Настройки JustDjVu");
        LocalizationService.Attach(this);
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), tag, StringComparison.Ordinal));
    }

    private static T GetSelectedEnum<T>(ComboBox combo, T fallback) where T : struct, Enum =>
        combo.SelectedItem is ComboBoxItem item &&
        Enum.TryParse<T>(item.Tag?.ToString(), out var result) ? result : fallback;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var duplicate = _hotkeys
            .SelectMany(x => new[] { x.PrimaryGesture, x.SecondaryGesture })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            MessageBox.Show(
                LocalizationService.Format(
                    "Сочетание «{0}» назначено нескольким действиям.", duplicate.Key),
                LocalizationService.Translate("Повторяющееся сочетание"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings.Language = GetSelectedEnum(LanguageCombo, _settings.Language);
        _settings.Theme = GetSelectedEnum(ThemeCombo, _settings.Theme);
        _settings.DefaultViewMode = GetSelectedEnum(ViewModeCombo, _settings.DefaultViewMode);
        _settings.DefaultZoomMode = GetSelectedEnum(ZoomModeCombo, _settings.DefaultZoomMode);
        _settings.DefaultZoom = DefaultZoomSlider.Value;
        _settings.ReopenLastDocument = ReopenLastDocumentCheck.IsChecked == true;
        _settings.ShowSidebar = ShowSidebarCheck.IsChecked == true;
        _settings.ShowToolbar = ShowToolbarCheck.IsChecked == true;
        _settings.Hotkeys = _hotkeys.ToDictionary(x => x.Id, x => x.PrimaryGesture,
            StringComparer.OrdinalIgnoreCase);
        _settings.SecondaryHotkeys = _hotkeys.ToDictionary(x => x.Id, x => x.SecondaryGesture,
            StringComparer.OrdinalIgnoreCase);
        DialogResult = true;
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var defaults = HotkeyCatalog.CreateDefaults();
        var secondaryDefaults = HotkeyCatalog.CreateSecondaryDefaults();
        foreach (var row in _hotkeys)
        {
            row.PrimaryGesture = defaults[row.Id];
            row.SecondaryGesture = secondaryDefaults[row.Id];
        }
    }

    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyHint.Text = LocalizationService.Translate(
            "Нажмите сочетание клавиш; Backspace — очистить, Esc — отменить");
        if (sender is TextBox box)
        {
            box.SelectAll();
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: HotkeyRow row })
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Back or Key.Delete)
        {
            SetEditedGesture((TextBox)sender, row, "");
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        try
        {
            var gesture = new KeyGesture(key, Keyboard.Modifiers);
            SetEditedGesture(
                (TextBox)sender,
                row,
                new KeyGestureConverter().ConvertToInvariantString(gesture) ?? key.ToString());
            Keyboard.ClearFocus();
        }
        catch
        {
            HotkeyHint.Text = LocalizationService.Translate("Это сочетание нельзя назначить");
        }
        e.Handled = true;
    }

    private void HotkeyBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is TextBox { DataContext: HotkeyRow row } box)
        {
            SetEditedGesture(box, row, e.Delta > 0 ? "WheelUp" : "WheelDown");
            HotkeyHint.Text = LocalizationService.Translate(e.Delta > 0
                ? "Назначено: колесо вверх"
                : "Назначено: колесо вниз");
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private static void SetEditedGesture(TextBox box, HotkeyRow row, string gesture)
    {
        if (string.Equals(box.Tag?.ToString(), "Secondary", StringComparison.Ordinal))
        {
            row.SecondaryGesture = gesture;
        }
        else
        {
            row.PrimaryGesture = gesture;
        }
    }

    private void ZoomModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateZoomControls();

    private void DefaultZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DefaultZoomLabel is not null)
        {
            DefaultZoomLabel.Text = $"{Math.Round(e.NewValue)}%";
        }
    }

    private void UpdateZoomControls()
    {
        if (DefaultZoomSlider is null)
        {
            return;
        }
        DefaultZoomSlider.IsEnabled =
            GetSelectedEnum(ZoomModeCombo, ZoomMode.FitPage) == ZoomMode.Custom;
        DefaultZoomLabel.Text = $"{Math.Round(DefaultZoomSlider.Value)}%";
    }
}

public sealed class HotkeyRow : INotifyPropertyChanged
{
    private string _primaryGesture;
    private string _secondaryGesture;

    public HotkeyRow(string id, string title, string primaryGesture, string secondaryGesture)
    {
        Id = id;
        Title = title;
        _primaryGesture = primaryGesture;
        _secondaryGesture = secondaryGesture;
    }

    public string Id { get; }
    public string Title { get; }

    public string PrimaryGesture
    {
        get => _primaryGesture;
        set
        {
            if (_primaryGesture == value) return;
            _primaryGesture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrimaryGesture)));
        }
    }

    public string SecondaryGesture
    {
        get => _secondaryGesture;
        set
        {
            if (_secondaryGesture == value) return;
            _secondaryGesture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryGesture)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
