using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using AetherControl.Services.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = new DashboardViewModel(
            App.Services.GetRequiredService<IHardwareMonitorService>(),
            App.Services.GetRequiredService<ProcessRankerService>());

        // The Frame creates a fresh DashboardPage (and DashboardViewModel) on every navigation to
        // this page — without unsubscribing here, each one leaks a permanent subscriber on the
        // singleton IHardwareMonitorService, since nothing else ever calls Dispose().
        Unloaded += (_, _) => ViewModel.Dispose();
    }
}
