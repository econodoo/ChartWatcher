using CommunityToolkit.Mvvm.ComponentModel;

namespace ChartWatcher.Application.Services;

public partial class ThemeService : ObservableObject
{
    private static readonly string[] _availableThemes = ["Colorful", "Stealth", "ColorBlind"];
    private int _currentIndex;

    [ObservableProperty]
    private string _currentTheme = "Colorful";

    public IReadOnlyList<string> AvailableThemes => _availableThemes;

    public void SetTheme(string themeName)
    {
        if (_availableThemes.Contains(themeName))
        {
            CurrentTheme = themeName;
            _currentIndex = Array.IndexOf(_availableThemes, themeName);
        }
    }

    public void CycleTheme()
    {
        _currentIndex = (_currentIndex + 1) % _availableThemes.Length;
        CurrentTheme = _availableThemes[_currentIndex];
    }
}
