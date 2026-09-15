using Avalonia.Controls;
using Avalonia.Input;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.RequestClose -= Close;
                vm.RequestClose += Close;
            }
        };

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.RequestClose += Close;

        Loaded += async (s, e) =>
        {
            await viewModel.LoadSettingsAsync();
        };
    }
}
