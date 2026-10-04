using System.Windows;
using PalTunes.App.Services;
using PalTunes.App.ViewModels;

namespace PalTunes.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(new DialogService(), new SettingsService(Environment.GetEnvironmentVariable("PALTUNES_SETTINGS")));
    }
}
