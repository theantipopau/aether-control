using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Views;

public sealed partial class DiagnosticsPage : Page
{
    public DiagnosticsViewModel ViewModel { get; }

    public DiagnosticsPage()
    {
        InitializeComponent();
        ViewModel = new DiagnosticsViewModel(
            App.Services.GetRequiredService<IHardwareMonitorService>(),
            App.Services.GetRequiredService<IFanControlService>());
    }

    // Window-size-dependent type scale — text grows/shrinks with the page width (WindowScale).
    private void OnPageSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
        Controls.ResponsiveScale.Apply(RootContent, e.NewSize.Width, e.NewSize.Height);
}
