using AetherControl.Core.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AetherControl.App.Controls;

/// <summary>
/// Ctrl+K quick-jump: fuzzy page/action search over a caller-supplied catalog. Rows are plain
/// StackPanels (title + subtitle) rebuilt on each keystroke through the pure
/// <see cref="PaletteFilter"/> — no DataTemplates, no extra controls. Enter runs the top hit,
/// click runs the clicked row. Best-effort like the other dialogs: it must never take down the caller.
/// </summary>
public static class CommandPaletteDialog
{
    public sealed record ActionEntry(PaletteEntry Entry, Action Run);

    public static async Task ShowAsync(XamlRoot xamlRoot, IReadOnlyList<ActionEntry> actions)
    {
        if (actions.Count == 0)
        {
            return;
        }

        var search = new TextBox
        {
            PlaceholderText = "Type a page or action…",
            Width = 400
        };

        var list = new ListView
        {
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 320
        };

        ContentDialog? dialog = null;
        void Refresh(string query)
        {
            var matches = PaletteFilter.Filter(actions.Select(a => a.Entry), query);
            list.Items.Clear();
            foreach (var match in matches)
            {
                var action = actions.First(a => a.Entry.Id == match.Id);
                list.Items.Add(Row(action));
            }

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        void RunAndClose(ActionEntry action)
        {
            dialog?.Hide();
            action.Run();
        }

        search.TextChanged += (_, _) => Refresh(search.Text);
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var target = (list.SelectedItem as FrameworkElement)?.Tag as ActionEntry
                    ?? (list.Items.Count > 0 ? ((FrameworkElement)list.Items[0]).Tag as ActionEntry : null);
                if (target is not null)
                {
                    RunAndClose(target);
                }
            }
        };
        list.ItemClick += (_, e) =>
        {
            if ((e.ClickedItem as FrameworkElement)?.Tag is ActionEntry target)
            {
                RunAndClose(target);
            }
        };

        dialog = new ContentDialog
        {
            Title = "Jump to",
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { search, list }
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.None,
            XamlRoot = xamlRoot
        };

        Refresh(string.Empty);
        dialog.Loaded += (_, _) => _ = search.Focus(FocusState.Programmatic);
        await dialog.ShowAsync();
    }

    private static StackPanel Row(ActionEntry action)
    {
        var panel = new StackPanel { Spacing = 1, Tag = action, Padding = new Thickness(4, 6, 4, 6) };
        panel.Children.Add(new TextBlock { Text = action.Entry.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = action.Entry.Subtitle, FontSize = 12, Opacity = 0.7 });
        return panel;
    }
}
