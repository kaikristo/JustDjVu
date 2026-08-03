using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace JustDjvu;

public sealed class PageViewModel : INotifyPropertyChanged
{
    private BitmapSource? _image;
    private BitmapSource? _thumbnail;
    private bool _isLoading;
    private string? _error;

    public PageViewModel(int number) => Number = number;

    public int Number { get; }
    public string Label => Number.ToString();

    public BitmapSource? Image
    {
        get => _image;
        set => SetField(ref _image, value);
    }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetField(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record SearchResult(int Page, string Snippet)
{
    public string PageLabel => LocalizationService.Format("Страница {0}", Page);
}
