// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Input;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (_viewModel is not null)
                _viewModel.RequestClose -= Close;
            _viewModel = DataContext as SettingsViewModel;
            if (_viewModel is not null)
                _viewModel.RequestClose += Close;
        };

        Closed += (s, e) =>
        {
            _viewModel?.Dispose();
            DataContext = null;
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

        Loaded += async (s, e) =>
        {
            await viewModel.LoadSettingsAsync();
        };
    }
}
