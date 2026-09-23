using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>
/// Shows an element only when the bound collection is empty — pairs with a
/// "no data yet" placeholder next to an <c>ItemsControl</c> that would
/// otherwise just render nothing when a sensor group isn't available (e.g.
/// motherboard fan RPM on a board with no Super I/O chip LibreHardwareMonitor
/// recognises). Pass ConverterParameter "Invert" to get the opposite
/// (visible only when non-empty).
/// <para>
/// Accepts either the collection itself or a plain <c>int</c> count. The persistent
/// <c>ObservableCollection&lt;T&gt;</c> properties (see ObservableCollectionMergeExtensions) never
/// change reference, so a binding on the collection itself only ever evaluates once — bind
/// <c>ViewModel.X.Count</c> instead for those, which re-evaluates on every
/// <c>ObservableCollection</c> mutation since it raises PropertyChanged("Count") internally.
/// </para>
/// </summary>
public sealed class CollectionEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isEmpty = value switch
        {
            int count => count == 0,
            ICollection { Count: > 0 } => false,
            _ => true
        };
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var show = invert ? !isEmpty : isEmpty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
