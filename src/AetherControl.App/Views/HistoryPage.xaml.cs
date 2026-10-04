using System.ComponentModel;
using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AetherControl.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        InitializeComponent();
        ViewModel = new HistoryViewModel(App.Services.GetRequiredService<IHistoryService>());
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    // Window-size-dependent type scale — text grows/shrinks with the page width (WindowScale).
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        Controls.ResponsiveScale.Apply(RootContent, e.NewSize.Width, e.NewSize.Height);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.ChartPoints))
        {
            RedrawChart();
        }
    }

    private void OnChartCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RedrawChart();

    private void RedrawChart()
    {
        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ChartLine.Points.Clear();
        ChartArea.Points.Clear();
        foreach (var point in ViewModel.ChartPoints)
        {
            ChartLine.Points.Add(new Point(point.X * width, point.Y * height));
        }

        // Close the wash down to the baseline (and back along it) so the fill sits under the trace
        // rather than being a self-connecting shape whose top edge is the line itself.
        if (ChartLine.Points.Count >= 2)
        {
            foreach (var point in ChartLine.Points)
            {
                ChartArea.Points.Add(point);
            }

            ChartArea.Points.Add(new Point(ChartLine.Points[^1].X, height));
            ChartArea.Points.Add(new Point(ChartLine.Points[0].X, height));
        }
    }
}
