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
        foreach (var point in ViewModel.ChartPoints)
        {
            ChartLine.Points.Add(new Point(point.X * width, point.Y * height));
        }
    }
}
