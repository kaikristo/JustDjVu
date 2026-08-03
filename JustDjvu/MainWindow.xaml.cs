using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JustDjvu;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty PageDisplayWidthProperty =
        DependencyProperty.Register(nameof(PageDisplayWidth), typeof(double), typeof(MainWindow),
            new PropertyMetadata(760d));
    public static readonly DependencyProperty PageRotationProperty =
        DependencyProperty.Register(nameof(PageRotation), typeof(double), typeof(MainWindow),
            new PropertyMetadata(0d));

    private readonly DjVuEngine _engine = new();
    private readonly ObservableCollection<PageViewModel> _pages = [];
    private readonly ObservableCollection<int> _bookmarks = [];
    private readonly SemaphoreSlim _prefetchWorkerGate = new(1, 1);
    private readonly DispatcherTimer _readingPositionTimer;
    private CancellationTokenSource? _openCancellation;
    private CancellationTokenSource? _searchCancellation;
    private int _prefetchVersion;
    private int _currentPage;
    private int _rotation;
    private double _zoom = 100;
    private DateTime _lastWheelPageTurnUtc = DateTime.MinValue;
    private ZoomMode _zoomMode;
    private ViewMode _viewMode;
    private bool _ignoreUiEvents;
    private bool _isFullScreen;
    private WindowStyle _savedWindowStyle;
    private WindowState _savedWindowState;
    private ResizeMode _savedResizeMode;

    public MainWindow()
    {
        InitializeComponent();

        _readingPositionTimer = new DispatcherTimer(
            DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _readingPositionTimer.Tick += ReadingPositionTimer_Tick;

        ThumbnailList.ItemsSource = _pages;
        ContinuousPagesList.ItemsSource = _pages;
        BookmarksList.ItemsSource = _bookmarks;
        ContinuousPagesList.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(ContinuousPagesList_ScrollChanged));

        _viewMode = App.Settings.DefaultViewMode;
        _zoomMode = App.Settings.DefaultZoomMode;
        _zoom = App.Settings.DefaultZoom;
        ZoomSlider.Value = _zoom;
        SidebarColumn.Width = new GridLength(App.Settings.SidebarWidth);

        ApplySettingsToUi();
        UpdateViewModeUi();
        UpdateDocumentUi();
        LocalizationService.Attach(this);
    }

    public double PageDisplayWidth
    {
        get => (double)GetValue(PageDisplayWidthProperty);
        set => SetValue(PageDisplayWidthProperty, value);
    }

    public double PageRotation
    {
        get => (double)GetValue(PageRotationProperty);
        set => SetValue(PageRotationProperty, value);
    }

    public void OpenFromShell(string path) =>
        Dispatcher.BeginInvoke(async () => await OpenDocumentAsync(path));

    private async Task OpenDocumentAsync(string path)
    {
        _openCancellation?.Cancel();
        _openCancellation?.Dispose();
        _openCancellation = new CancellationTokenSource();
        SetBusy(true, T("Открытие документа…"));

        try
        {
            await _engine.OpenAsync(path, _openCancellation.Token);

            _pages.Clear();
            for (var i = 1; i <= _engine.PageCount; i++)
            {
                _pages.Add(new PageViewModel(i));
            }

            _rotation = 0;
            PageRotation = 0;
            SingleRotateTransform.Angle = 0;
            FacingLeftRotateTransform.Angle = 0;
            FacingRightRotateTransform.Angle = 0;
            var openedPath = _engine.DocumentPath!;
            _currentPage = App.Settings.LastPages.TryGetValue(openedPath, out var savedPage)
                ? Math.Clamp(savedPage, 1, _engine.PageCount)
                : 1;
            App.Settings.LastDocumentPath = openedPath;
            App.Settings.LastPages[openedPath] = _currentPage;
            Title = $"{Path.GetFileName(path)} — JustDjVu";
            AddRecentFile(openedPath);
            LoadBookmarks();
            UpdateDocumentUi();
            await DisplayCurrentPageAsync();
            _ = WarmThumbnailsAsync();
            StatusText.Text = T("Документ открыт");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = T("Операция отменена");
        }
        catch (Exception ex)
        {
            _engine.Reset();
            _pages.Clear();
            _currentPage = 0;
            UpdateDocumentUi();
            ShowError(T("Не удалось открыть документ"), ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DisplayCurrentPageAsync()
    {
        if (_currentPage < 1 || _engine.DocumentPath is null)
        {
            return;
        }

        var model = _pages[_currentPage - 1];
        PageNumberBox.Text = _currentPage.ToString();

        try
        {
            if (_viewMode == ViewMode.Single)
            {
                model.IsLoading = true;
                BusyText.Text = TF("Отрисовка страницы {0}…", _currentPage);
                var image = model.Image ?? await _engine.RenderPageAsync(_currentPage, false);
                model.Image = image;
                SinglePageImage.Source = image;
                ApplyZoom();
            }
            else if (_viewMode == ViewMode.Facing)
            {
                model.IsLoading = true;
                if (_currentPage < _engine.PageCount)
                {
                    var rightModel = _pages[_currentPage];
                    rightModel.IsLoading = true;
                    var leftTask = model.Image is not null
                        ? Task.FromResult(model.Image)
                        : _engine.RenderPageAsync(_currentPage, false);
                    var rightTask = rightModel.Image is not null
                        ? Task.FromResult(rightModel.Image)
                        : _engine.RenderPageAsync(_currentPage + 1, false);
                    await Task.WhenAll(leftTask, rightTask);

                    var left = await leftTask;
                    var right = await rightTask;
                    model.Image = left;
                    rightModel.Image = right;
                    rightModel.IsLoading = false;
                    FacingLeftImage.Source = left;
                    FacingRightImage.Source = right;
                    FacingRightBorder.Visibility = Visibility.Visible;
                }
                else
                {
                    var left = model.Image ?? await _engine.RenderPageAsync(_currentPage, false);
                    model.Image = left;
                    FacingLeftImage.Source = left;
                    FacingRightImage.Source = null;
                    FacingRightBorder.Visibility = Visibility.Collapsed;
                }
                ApplyZoom();
            }
            else
            {
                ContinuousPagesList.SelectedIndex = _currentPage - 1;
                ContinuousPagesList.ScrollIntoView(model);
                await LoadFullPageAsync(model);
                ApplyZoom();
            }
        }
        catch (OperationCanceledException)
        {
            // Документ был заменён или окно закрывается.
        }
        catch (Exception ex)
        {
            model.Error = ex.Message;
            ShowError(TF("Не удалось отобразить страницу {0}", _currentPage), ex);
        }
        finally
        {
            model.IsLoading = false;
            UpdateNavigationUi();
            if (model.Image is not null)
            {
                ScheduleReadingPositionSave();
                StartAdjacentPrefetch();
            }
        }
    }

    private async Task LoadFullPageAsync(PageViewModel model)
    {
        if (model.Image is not null || _engine.DocumentPath is null)
        {
            return;
        }

        model.IsLoading = true;
        model.Error = null;
        try
        {
            model.Image = await _engine.RenderPageAsync(model.Number, false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            model.Error = ex.Message;
        }
        finally
        {
            model.IsLoading = false;
        }
    }

    private async Task LoadThumbnailAsync(PageViewModel model)
    {
        if (model.Thumbnail is not null || _engine.DocumentPath is null)
        {
            return;
        }

        model.IsLoading = true;
        try
        {
            model.Thumbnail = await _engine.RenderPageAsync(model.Number, true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            model.Error = ex.Message;
        }
        finally
        {
            model.IsLoading = false;
        }
    }

    private async Task WarmThumbnailsAsync()
    {
        foreach (var model in _pages.Take(Math.Min(3, _pages.Count)))
        {
            if (_engine.DocumentPath is null)
            {
                break;
            }
            await LoadThumbnailAsync(model);
        }
    }

    private async Task NavigateToAsync(int page)
    {
        if (_engine.PageCount == 0)
        {
            return;
        }

        var target = Math.Clamp(page, 1, _engine.PageCount);
        if (_viewMode == ViewMode.Facing && target > 1 && target % 2 == 0)
        {
            target--;
        }
        _currentPage = target;
        await DisplayCurrentPageAsync();

        _ignoreUiEvents = true;
        ThumbnailList.SelectedIndex = _currentPage - 1;
        ThumbnailList.ScrollIntoView(_pages[_currentPage - 1]);
        _ignoreUiEvents = false;
    }

    private void StartAdjacentPrefetch()
    {
        if (_currentPage < 1 || _engine.DocumentPath is null)
        {
            return;
        }

        var version = Interlocked.Increment(ref _prefetchVersion);
        var documentPath = _engine.DocumentPath;
        var page = _currentPage;
        var mode = _viewMode;
        _ = PrefetchAdjacentPagesAsync(version, documentPath, page, mode);
    }

    private async Task PrefetchAdjacentPagesAsync(
        int version, string documentPath, int currentPage, ViewMode mode)
    {
        await _prefetchWorkerGate.WaitAsync();
        try
        {
            if (version != _prefetchVersion ||
                !string.Equals(documentPath, _engine.DocumentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var step = mode == ViewMode.Facing ? 2 : 1;
            var candidates = new[]
            {
                currentPage + step,
                currentPage - step,
                currentPage + step * 2
            };

            foreach (var page in candidates.Where(x => x >= 1 && x <= _engine.PageCount).Distinct())
            {
                if (version != _prefetchVersion ||
                    !string.Equals(documentPath, _engine.DocumentPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var model = _pages[page - 1];
                if (model.Image is null)
                {
                    model.Image = await _engine.RenderPageAsync(page, false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Предзагрузка не должна показывать ошибку для ещё не открытой страницы.
        }
        finally
        {
            _prefetchWorkerGate.Release();
        }
    }

    private void ScheduleReadingPositionSave()
    {
        if (_engine.DocumentPath is null || _currentPage < 1)
        {
            return;
        }

        App.Settings.LastDocumentPath = _engine.DocumentPath;
        App.Settings.LastPages[_engine.DocumentPath] = _currentPage;
        _readingPositionTimer.Stop();
        _readingPositionTimer.Start();
    }

    private void ReadingPositionTimer_Tick(object? sender, EventArgs e)
    {
        _readingPositionTimer.Stop();
        SaveReadingPositionNow();
    }

    private void SaveReadingPositionNow()
    {
        _readingPositionTimer.Stop();
        if (_engine.DocumentPath is not null && _currentPage >= 1)
        {
            App.Settings.LastDocumentPath = _engine.DocumentPath;
            App.Settings.LastPages[_engine.DocumentPath] = _currentPage;
        }
        SaveSettingsSafely();
    }

    private void ApplyZoom()
    {
        if (_engine.PageCount == 0)
        {
            return;
        }

        var source = SinglePageImage.Source as BitmapSource
                     ?? FacingLeftImage.Source as BitmapSource
                     ?? _pages.ElementAtOrDefault(_currentPage - 1)?.Image;
        if (source is null)
        {
            return;
        }

        var availableWidth = Math.Max(200, ViewerHost.ActualWidth - 70);
        var availableHeight = Math.Max(200, ViewerHost.ActualHeight - 70);
        var pixelWidth = source.PixelWidth;
        var pixelHeight = source.PixelHeight;
        if (_rotation % 180 != 0)
        {
            (pixelWidth, pixelHeight) = (pixelHeight, pixelWidth);
        }

        if (_viewMode == ViewMode.Facing)
        {
            availableWidth = Math.Max(200, (availableWidth - 25) / 2);
        }

        _zoom = _zoomMode switch
        {
            ZoomMode.FitPage => Math.Min(availableWidth / pixelWidth, availableHeight / pixelHeight) * 100,
            ZoomMode.FitWidth => availableWidth / pixelWidth * 100,
            ZoomMode.ActualSize => 100,
            _ => _zoom
        };
        _zoom = Math.Clamp(_zoom, 10, 400);
        var scale = _zoom / 100;

        if (SinglePageImage.Source is BitmapSource single)
        {
            SinglePageImage.Width = single.PixelWidth * scale;
        }
        if (FacingLeftImage.Source is BitmapSource left)
        {
            FacingLeftImage.Width = left.PixelWidth * scale;
        }
        if (FacingRightImage.Source is BitmapSource right)
        {
            FacingRightImage.Width = right.PixelWidth * scale;
        }
        PageDisplayWidth = Math.Max(180, source.PixelWidth * scale);

        _ignoreUiEvents = true;
        ZoomSlider.Value = _zoom;
        _ignoreUiEvents = false;
        var label = $"{Math.Round(_zoom)}%";
        ZoomText.Text = label;
        ZoomStatusText.Text = label;
    }

    private void SetZoom(double zoom)
    {
        _zoomMode = ZoomMode.Custom;
        _zoom = Math.Clamp(zoom, 10, 400);
        ApplyZoom();
    }

    private async Task ChangeViewModeAsync(ViewMode mode)
    {
        _viewMode = mode;
        if (_viewMode == ViewMode.Facing && _currentPage > 1 && _currentPage % 2 == 0)
        {
            _currentPage--;
        }
        UpdateViewModeUi();
        await DisplayCurrentPageAsync();
    }

    private void UpdateViewModeUi()
    {
        EmptyState.Visibility = _engine.PageCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        SinglePageViewer.Visibility = _engine.PageCount > 0 && _viewMode == ViewMode.Single
            ? Visibility.Visible : Visibility.Collapsed;
        ContinuousPagesList.Visibility = _engine.PageCount > 0 && _viewMode == ViewMode.Continuous
            ? Visibility.Visible : Visibility.Collapsed;
        FacingPageViewer.Visibility = _engine.PageCount > 0 && _viewMode == ViewMode.Facing
            ? Visibility.Visible : Visibility.Collapsed;

        SingleModeMenuItem.IsChecked = _viewMode == ViewMode.Single;
        ContinuousModeMenuItem.IsChecked = _viewMode == ViewMode.Continuous;
        FacingModeMenuItem.IsChecked = _viewMode == ViewMode.Facing;

        _ignoreUiEvents = true;
        ViewModeCombo.SelectedIndex = (int)_viewMode;
        _ignoreUiEvents = false;
        ModeStatusText.Text = _viewMode switch
        {
            ViewMode.Single => T("Одна страница"),
            ViewMode.Continuous => T("Непрерывно"),
            _ => T("Разворот")
        };
    }

    private void UpdateDocumentUi()
    {
        UpdateViewModeUi();
        PageCountText.Text = $"/ {_engine.PageCount}";
        PageNumberBox.Text = _currentPage.ToString();
        FileStatusText.Text = _engine.DocumentPath is null
            ? T("Документ не открыт")
            : TF("{0}  •  {1} стр.", Path.GetFileName(_engine.DocumentPath), _engine.PageCount);
        if (_engine.DocumentPath is null)
        {
            Title = "JustDjVu";
            SinglePageImage.Source = null;
            FacingLeftImage.Source = null;
            FacingRightImage.Source = null;
        }
        UpdateNavigationUi();
    }

    private void UpdateNavigationUi()
    {
        PageNumberBox.IsEnabled = _engine.PageCount > 0;
        PageNumberBox.Text = _currentPage.ToString();
        PageCountText.Text = $"/ {_engine.PageCount}";
    }

    private void ApplySettingsToUi()
    {
        SidebarMenuItem.IsChecked = App.Settings.ShowSidebar;
        ToolbarMenuItem.IsChecked = App.Settings.ShowToolbar;
        DarkThemeMenuItem.IsChecked = App.Settings.Theme == AppTheme.Dark;
        SetSidebarVisible(App.Settings.ShowSidebar);
        SetToolbarVisible(App.Settings.ShowToolbar);
    }

    private void SetSidebarVisible(bool visible)
    {
        Sidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = visible
            ? new GridLength(Math.Max(190, App.Settings.SidebarWidth))
            : new GridLength(0);
        SidebarSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
        SidebarMenuItem.IsChecked = visible;
    }

    private void SetToolbarVisible(bool visible)
    {
        ToolbarHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ToolbarMenuItem.IsChecked = visible;
    }

    private void SetBusy(bool busy, string? text = null)
    {
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (text is not null)
        {
            BusyText.Text = text;
        }
    }

    private void AddRecentFile(string path)
    {
        App.Settings.RecentFiles.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase));
        App.Settings.RecentFiles.Insert(0, path);
        App.Settings.RecentFiles = App.Settings.RecentFiles.Take(12).ToList();
        SaveSettingsSafely();
    }

    private void LoadBookmarks()
    {
        _bookmarks.Clear();
        if (_engine.DocumentPath is not null &&
            App.Settings.Bookmarks.TryGetValue(_engine.DocumentPath, out var bookmarks))
        {
            foreach (var page in bookmarks.Where(x => x >= 1 && x <= _engine.PageCount).Distinct().Order())
            {
                _bookmarks.Add(page);
            }
        }
    }

    private void SaveBookmarks()
    {
        if (_engine.DocumentPath is null)
        {
            return;
        }
        App.Settings.Bookmarks[_engine.DocumentPath] = _bookmarks.Order().ToList();
        SaveSettingsSafely();
    }

    private void SaveSettingsSafely()
    {
        try
        {
            SettingsStore.Save(App.Settings);
        }
        catch
        {
            StatusText.Text = T("Не удалось сохранить настройки");
        }
    }

    private static string T(string source) => LocalizationService.Translate(source);
    private static string TF(string source, params object[] arguments) =>
        LocalizationService.Format(source, arguments);

    private static void ShowError(string title, Exception ex) =>
        MessageBox.Show($"{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Error);

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("Открыть документ DjVu"),
            Filter = T("Документы DjVu (*.djvu;*.djv)|*.djvu;*.djv|Все файлы (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _ = OpenDocumentAsync(dialog.FileName);
        }
    }

    private void RecentMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        RecentMenu.Items.Clear();
        App.Settings.Normalize();
        if (App.Settings.RecentFiles.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = T("Список пуст"), IsEnabled = false });
            return;
        }

        foreach (var file in App.Settings.RecentFiles)
        {
            var item = new MenuItem
            {
                Header = Path.GetFileName(file),
                ToolTip = file,
                Tag = file
            };
            item.Click += (_, _) => _ = OpenDocumentAsync((string)item.Tag);
            RecentMenu.Items.Add(item);
        }
        RecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = T("Очистить список") };
        clear.Click += (_, _) =>
        {
            App.Settings.RecentFiles.Clear();
            SaveSettingsSafely();
        };
        RecentMenu.Items.Add(clear);
    }

    private void CloseDocument_Click(object sender, RoutedEventArgs e)
    {
        SaveReadingPositionNow();
        Interlocked.Increment(ref _prefetchVersion);
        _openCancellation?.Cancel();
        _searchCancellation?.Cancel();
        _engine.Reset();
        _pages.Clear();
        _bookmarks.Clear();
        _currentPage = 0;
        UpdateDocumentUi();
        StatusText.Text = T("Документ закрыт");
    }

    private void SaveCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.DocumentPath is null) return;
        var dialog = new SaveFileDialog
        {
            Title = T("Сохранить копию"),
            Filter = T("Документ DjVu (*.djvu)|*.djvu|Все файлы (*.*)|*.*"),
            FileName = Path.GetFileName(_engine.DocumentPath),
            AddExtension = true,
            DefaultExt = ".djvu"
        };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                File.Copy(_engine.DocumentPath, dialog.FileName, true);
                StatusText.Text = T("Копия сохранена");
            }
            catch (Exception ex)
            {
                ShowError(T("Не удалось сохранить копию"), ex);
            }
        }
    }

    private async void ExportPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < 1) return;
        var dialog = new SaveFileDialog
        {
            Title = T("Экспорт страницы"),
            Filter = T("Изображение PNG (*.png)|*.png"),
            FileName = $"{Path.GetFileNameWithoutExtension(_engine.DocumentPath)}-{_currentPage}.png",
            AddExtension = true,
            DefaultExt = ".png"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var bitmap = await _engine.RenderPageAsync(_currentPage, false);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            StatusText.Text = T("Страница экспортирована");
        }
        catch (Exception ex)
        {
            ShowError(T("Не удалось экспортировать страницу"), ex);
        }
    }

    private async void CopyPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < 1) return;
        try
        {
            Clipboard.SetImage(await _engine.RenderPageAsync(_currentPage, false));
            StatusText.Text = T("Страница скопирована");
        }
        catch (Exception ex)
        {
            ShowError(T("Не удалось скопировать страницу"), ex);
        }
    }

    private async void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < 1) return;
        try
        {
            var bitmap = await _engine.RenderPageAsync(_currentPage, false);
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            var printableWidth = dialog.PrintableAreaWidth;
            var printableHeight = dialog.PrintableAreaHeight;
            image.Measure(new Size(printableWidth, printableHeight));
            image.Arrange(new Rect(0, 0, printableWidth, printableHeight));
            dialog.PrintVisual(image, $"{Path.GetFileName(_engine.DocumentPath)}, стр. {_currentPage}");
            StatusText.Text = T("Страница отправлена на печать");
        }
        catch (Exception ex)
        {
            ShowError(T("Ошибка печати"), ex);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private async void PreviousPage_Click(object sender, RoutedEventArgs e) =>
        await NavigateToAsync(_currentPage - (_viewMode == ViewMode.Facing ? 2 : 1));
    private async void NextPage_Click(object sender, RoutedEventArgs e) =>
        await NavigateToAsync(_currentPage + (_viewMode == ViewMode.Facing ? 2 : 1));
    private async void FirstPage_Click(object sender, RoutedEventArgs e) => await NavigateToAsync(1);
    private async void LastPage_Click(object sender, RoutedEventArgs e) => await NavigateToAsync(_engine.PageCount);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom + (_zoom < 100 ? 10 : 25));
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom - (_zoom <= 100 ? 10 : 25));
    private void FitPage_Click(object sender, RoutedEventArgs e) { _zoomMode = ZoomMode.FitPage; ApplyZoom(); }
    private void FitWidth_Click(object sender, RoutedEventArgs e) { _zoomMode = ZoomMode.FitWidth; ApplyZoom(); }
    private void ActualSize_Click(object sender, RoutedEventArgs e) { _zoomMode = ZoomMode.ActualSize; ApplyZoom(); }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        _rotation = (_rotation + 90) % 360;
        PageRotation = _rotation;
        SingleRotateTransform.Angle = _rotation;
        FacingLeftRotateTransform.Angle = _rotation;
        FacingRightRotateTransform.Angle = _rotation;
        ApplyZoom();
    }

    private async void SingleMode_Click(object sender, RoutedEventArgs e) =>
        await ChangeViewModeAsync(ViewMode.Single);
    private async void ContinuousMode_Click(object sender, RoutedEventArgs e) =>
        await ChangeViewModeAsync(ViewMode.Continuous);
    private async void FacingMode_Click(object sender, RoutedEventArgs e) =>
        await ChangeViewModeAsync(ViewMode.Facing);

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ShowSidebar = !App.Settings.ShowSidebar;
        SetSidebarVisible(App.Settings.ShowSidebar);
        SaveSettingsSafely();
    }

    private void ToggleToolbar_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ShowToolbar = !App.Settings.ShowToolbar;
        SetToolbarVisible(App.Settings.ShowToolbar);
        SaveSettingsSafely();
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Theme = App.Settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        App.ApplyTheme(App.Settings.Theme);
        DarkThemeMenuItem.IsChecked = App.Settings.Theme == AppTheme.Dark;
        SaveSettingsSafely();
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            _savedWindowStyle = WindowStyle;
            _savedWindowState = WindowState;
            _savedResizeMode = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            MainMenu.Visibility = Visibility.Collapsed;
            ToolbarHost.Visibility = Visibility.Collapsed;
            MainStatusBar.Visibility = Visibility.Collapsed;
            _isFullScreen = true;
        }
        else
        {
            WindowStyle = _savedWindowStyle;
            ResizeMode = _savedResizeMode;
            WindowState = _savedWindowState;
            MainMenu.Visibility = Visibility.Visible;
            ToolbarHost.Visibility = App.Settings.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
            MainStatusBar.Visibility = Visibility.Visible;
            _isFullScreen = false;
        }
        ApplyZoom();
    }

    private void ToggleBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < 1) return;
        if (_bookmarks.Contains(_currentPage))
        {
            _bookmarks.Remove(_currentPage);
            StatusText.Text = T("Закладка удалена");
        }
        else
        {
            var insertAt = 0;
            while (insertAt < _bookmarks.Count && _bookmarks[insertAt] < _currentPage) insertAt++;
            _bookmarks.Insert(insertAt, _currentPage);
            StatusText.Text = T("Закладка добавлена");
        }
        SaveBookmarks();
    }

    private void FocusSearch_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Settings.ShowSidebar)
        {
            App.Settings.ShowSidebar = true;
            SetSidebarVisible(true);
        }
        SearchTab.IsSelected = true;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async Task RunSearchAsync()
    {
        if (_engine.PageCount == 0 || string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            SearchStatusText.Text = _engine.PageCount == 0
                ? T("Сначала откройте документ")
                : T("Введите текст для поиска");
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        SearchResultsList.ItemsSource = null;
        CancelSearchButton.Visibility = Visibility.Visible;
        SearchStatusText.Text = T("Поиск…");
        var progress = new Progress<double>(value =>
            SearchStatusText.Text = TF("Поиск… {0}%", Math.Round(value * 100)));

        try
        {
            var results = await _engine.SearchAsync(
                SearchBox.Text.Trim(), progress, _searchCancellation.Token);
            SearchResultsList.ItemsSource = results;
            SearchStatusText.Text = results.Count == 0
                ? T("Совпадений не найдено")
                : TF("Найдено: {0}", results.Count);
        }
        catch (OperationCanceledException)
        {
            SearchStatusText.Text = T("Поиск отменён");
        }
        catch (Exception ex)
        {
            SearchStatusText.Text = T("Ошибка поиска");
            ShowError(T("Не удалось выполнить поиск"), ex);
        }
        finally
        {
            CancelSearchButton.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelSearch_Click(object sender, RoutedEventArgs e) => _searchCancellation?.Cancel();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(App.Settings) { Owner = this };
        if (window.ShowDialog() == true)
        {
            App.ApplyLanguage(App.Settings.Language);
            ApplySettingsToUi();
            _viewMode = App.Settings.DefaultViewMode;
            App.ApplyTheme(App.Settings.Theme);
            SaveSettingsSafely();
            UpdateViewModeUi();
            UpdateDocumentUi();
            SearchResultsList.Items.Refresh();
            BookmarksList.Items.Refresh();
            _ = DisplayCurrentPageAsync();
        }
    }

    private void RegisterAssociation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.Register();
            MessageBox.Show(
                T("JustDjVu зарегистрирован для файлов .djvu и .djv текущего пользователя.\n\n" +
                  "Теперь приложение появится в меню «Открыть с помощью»."),
                T("Регистрация завершена"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError(T("Не удалось зарегистрировать приложение"), ex);
        }
    }

    private void ShortcutsHelp_Click(object sender, RoutedEventArgs e)
    {
        var lines = HotkeyCatalog.Definitions.Select(x =>
        {
            var primary = App.Settings.Hotkeys.GetValueOrDefault(x.Id, x.DefaultGesture);
            var secondary = App.Settings.SecondaryHotkeys.GetValueOrDefault(
                x.Id, x.DefaultSecondaryGesture);
            var gestures = string.IsNullOrWhiteSpace(secondary)
                ? FormatGesture(primary)
                : $"{FormatGesture(primary)} / {FormatGesture(secondary)}";
            return $"{T(x.Title),-38}  {gestures}";
        });
        MessageBox.Show(string.Join(Environment.NewLine, lines), T("Горячие клавиши"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(
            T("JustDjVu 1.0\n\nСовременный DjVu-ридер для Windows.\n" +
              "Рендеринг документов: DjVuLibre 3.5.29 (GPL v2+).\n\n" +
              "Поддерживает масштабирование, режимы просмотра, поиск OCR-текста, " +
              "закладки, печать, drag & drop и «Открыть с помощью»."),
            T("О программе"), MessageBoxButton.OK, MessageBoxImage.Information);

    private async void PageNumberBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && int.TryParse(PageNumberBox.Text, out var page))
        {
            await NavigateToAsync(page);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await RunSearchAsync();
            e.Handled = true;
        }
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ignoreUiEvents && IsLoaded)
        {
            SetZoom(e.NewValue);
        }
    }

    private async void ViewModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreUiEvents || !IsLoaded || ViewModeCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        if (Enum.TryParse<ViewMode>(item.Tag?.ToString(), out var mode))
        {
            await ChangeViewModeAsync(mode);
        }
    }

    private async void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ignoreUiEvents && ThumbnailList.SelectedItem is PageViewModel page)
        {
            await NavigateToAsync(page.Number);
        }
    }

    private async void ContinuousPagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewMode == ViewMode.Continuous && ContinuousPagesList.SelectedItem is PageViewModel page &&
            page.Number != _currentPage)
        {
            _currentPage = page.Number;
            PageNumberBox.Text = _currentPage.ToString();
            UpdateNavigationUi();
            await LoadFullPageAsync(page);
            ScheduleReadingPositionSave();
            StartAdjacentPrefetch();
        }
    }

    private void ContinuousPagesList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_viewMode != ViewMode.Continuous || _pages.Count == 0 || _ignoreUiEvents)
        {
            return;
        }

        var viewportCenter = ContinuousPagesList.ActualHeight / 2;
        var bestIndex = -1;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < _pages.Count; index++)
        {
            if (ContinuousPagesList.ItemContainerGenerator.ContainerFromIndex(index)
                is not ListBoxItem container || !container.IsVisible)
            {
                continue;
            }

            try
            {
                var y = container.TransformToAncestor(ContinuousPagesList)
                    .Transform(new Point(0, 0)).Y;
                var distance = Math.Abs(y + container.ActualHeight / 2 - viewportCenter);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }
            catch (InvalidOperationException)
            {
                // Контейнер мог быть перевиртуализирован во время прокрутки.
            }
        }

        if (bestIndex >= 0 && _currentPage != bestIndex + 1)
        {
            _currentPage = bestIndex + 1;
            UpdateNavigationUi();
            _ignoreUiEvents = true;
            ThumbnailList.SelectedIndex = bestIndex;
            _ignoreUiEvents = false;
            ScheduleReadingPositionSave();
            StartAdjacentPrefetch();
        }
    }

    private async void ThumbnailItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PageViewModel page })
        {
            await LoadThumbnailAsync(page);
        }
    }

    private async void ContinuousPageItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PageViewModel page })
        {
            await LoadFullPageAsync(page);
        }
    }

    private async void SearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchResultsList.SelectedItem is SearchResult result)
        {
            await NavigateToAsync(result.Page);
        }
    }

    private async void BookmarksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BookmarksList.SelectedItem is int page)
        {
            await NavigateToAsync(page);
        }
    }

    private void ViewerHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetZoom(_zoom + (e.Delta > 0 ? 10 : -10));
            e.Handled = true;
            return;
        }

        if (_viewMode == ViewMode.Continuous || _engine.PageCount == 0)
        {
            return;
        }

        var gesture = e.Delta > 0 ? "WheelUp" : "WheelDown";
        var action = FindActionByGesture(gesture);
        if (action is null)
        {
            return;
        }

        if (action is "PreviousPage" or "NextPage")
        {
            var viewer = _viewMode == ViewMode.Facing ? FacingPageViewer : SinglePageViewer;
            var atEdge = action == "PreviousPage"
                ? viewer.VerticalOffset <= 1
                : viewer.VerticalOffset >= viewer.ScrollableHeight - 1;
            if (!atEdge)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastWheelPageTurnUtc).TotalMilliseconds < 220)
            {
                e.Handled = true;
                return;
            }
            _lastWheelPageTurnUtc = now;
        }

        ExecuteHotkey(action);
        e.Handled = true;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_zoomMode is ZoomMode.FitPage or ZoomMode.FitWidth)
        {
            ApplyZoom();
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        var valid = TryGetDroppedDjVu(e.Data, out _);
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (TryGetDroppedDjVu(e.Data, out var path))
        {
            _ = OpenDocumentAsync(path!);
        }
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    private static bool TryGetDroppedDjVu(IDataObject data, out string? path)
    {
        path = null;
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return false;
        }
        path = files[0];
        var extension = Path.GetExtension(path);
        return extension.Equals(".djvu", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".djv", StringComparison.OrdinalIgnoreCase);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox &&
            Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return;
        }

        foreach (var definition in HotkeyCatalog.Definitions)
        {
            var gestures = new[]
            {
                App.Settings.Hotkeys.GetValueOrDefault(
                    definition.Id, definition.DefaultGesture),
                App.Settings.SecondaryHotkeys.GetValueOrDefault(
                    definition.Id, definition.DefaultSecondaryGesture)
            };

            foreach (var text in gestures.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (IsMouseWheelGesture(text))
                {
                    continue;
                }

                try
                {
                    if (new KeyGestureConverter().ConvertFromInvariantString(text) is KeyGesture gesture &&
                        gesture.Matches(null, e))
                    {
                        ExecuteHotkey(definition.Id);
                        e.Handled = true;
                        return;
                    }
                }
                catch
                {
                    // Некорректное пользовательское сочетание игнорируется.
                }
            }
        }
    }

    private static bool IsMouseWheelGesture(string gesture) =>
        gesture.Equals("WheelUp", StringComparison.OrdinalIgnoreCase) ||
        gesture.Equals("WheelDown", StringComparison.OrdinalIgnoreCase);

    private static string FormatGesture(string gesture) => gesture switch
    {
        "WheelUp" => T("Колесо вверх"),
        "WheelDown" => T("Колесо вниз"),
        _ => gesture
    };

    private static string? FindActionByGesture(string gesture)
    {
        foreach (var definition in HotkeyCatalog.Definitions)
        {
            var primary = App.Settings.Hotkeys.GetValueOrDefault(
                definition.Id, definition.DefaultGesture);
            var secondary = App.Settings.SecondaryHotkeys.GetValueOrDefault(
                definition.Id, definition.DefaultSecondaryGesture);
            if (gesture.Equals(primary, StringComparison.OrdinalIgnoreCase) ||
                gesture.Equals(secondary, StringComparison.OrdinalIgnoreCase))
            {
                return definition.Id;
            }
        }
        return null;
    }

    private void ExecuteHotkey(string id)
    {
        switch (id)
        {
            case "Open": Open_Click(this, new RoutedEventArgs()); break;
            case "Close": CloseDocument_Click(this, new RoutedEventArgs()); break;
            case "Print": Print_Click(this, new RoutedEventArgs()); break;
            case "Search": FocusSearch_Click(this, new RoutedEventArgs()); break;
            case "PreviousPage": _ = NavigateToAsync(_currentPage - (_viewMode == ViewMode.Facing ? 2 : 1)); break;
            case "NextPage": _ = NavigateToAsync(_currentPage + (_viewMode == ViewMode.Facing ? 2 : 1)); break;
            case "FirstPage": _ = NavigateToAsync(1); break;
            case "LastPage": _ = NavigateToAsync(_engine.PageCount); break;
            case "ZoomIn": SetZoom(_zoom + 10); break;
            case "ZoomOut": SetZoom(_zoom - 10); break;
            case "ActualSize": _zoomMode = ZoomMode.ActualSize; ApplyZoom(); break;
            case "FitPage": _zoomMode = ZoomMode.FitPage; ApplyZoom(); break;
            case "FitWidth": _zoomMode = ZoomMode.FitWidth; ApplyZoom(); break;
            case "Rotate": Rotate_Click(this, new RoutedEventArgs()); break;
            case "ToggleSidebar": ToggleSidebar_Click(this, new RoutedEventArgs()); break;
            case "FullScreen": ToggleFullScreen(); break;
            case "Bookmark": ToggleBookmark_Click(this, new RoutedEventArgs()); break;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (SidebarColumn.ActualWidth > 0)
        {
            App.Settings.SidebarWidth = SidebarColumn.ActualWidth;
        }
        SaveReadingPositionNow();
        SaveSettingsSafely();
        Interlocked.Increment(ref _prefetchVersion);
        _openCancellation?.Cancel();
        _searchCancellation?.Cancel();
        _engine.Dispose();
    }
}

internal static class FileAssociationService
{
    private const string ProgId = "JustDjVu.Document";

    public static void Register()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                LocalizationService.Translate("Не удалось определить путь приложения."));

        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        using (var extension = classes.CreateSubKey(@".djvu\OpenWithProgids"))
        {
            extension.SetValue(ProgId, "");
        }
        using (var extension = classes.CreateSubKey(@".djv\OpenWithProgids"))
        {
            extension.SetValue(ProgId, "");
        }
        using (var progId = classes.CreateSubKey(ProgId))
        {
            progId.SetValue("", LocalizationService.Translate("Документ DjVu"));
            progId.SetValue("FriendlyTypeName", LocalizationService.Translate("Документ DjVu"));
        }
        using (var icon = classes.CreateSubKey($@"{ProgId}\DefaultIcon"))
        {
            icon.SetValue("", $"\"{executable}\",0");
        }
        using (var command = classes.CreateSubKey($@"{ProgId}\shell\open\command"))
        {
            command.SetValue("", $"\"{executable}\" \"%1\"");
        }
        using (var applications = classes.CreateSubKey(@"Applications\JustDjVu.exe\SupportedTypes"))
        {
            applications.SetValue(".djvu", "");
            applications.SetValue(".djv", "");
        }

        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
