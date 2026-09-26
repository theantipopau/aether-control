using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Controls;

/// <summary>
/// The header band every page opens with: an area icon, an eyebrow label naming the product area,
/// the page title and a one-line purpose, plus optional page actions (right) and a stat strip (below).
/// Pages used to open on a bare title over a wall of rows; this gives each area an identity and puts
/// its headline numbers first without each page re-implementing the layout.
/// </summary>
public sealed partial class PageHero : UserControl
{
    public static readonly DependencyProperty GlyphProperty = Register(nameof(Glyph), (h, v) => h.IconGlyph.Glyph = v);
    public static readonly DependencyProperty EyebrowProperty = Register(nameof(Eyebrow), (h, v) => h.EyebrowText.Text = v.ToUpperInvariant());
    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), (h, v) => h.TitleText.Text = v);
    public static readonly DependencyProperty SubtitleProperty = Register(nameof(Subtitle), (h, v) => h.SubtitleText.Text = v);

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(PageHero),
            new PropertyMetadata(null, (d, e) => ((PageHero)d).ActionsPresenter.Content = e.NewValue));

    public static readonly DependencyProperty StatsProperty =
        DependencyProperty.Register(nameof(Stats), typeof(object), typeof(PageHero),
            new PropertyMetadata(null, (d, e) =>
            {
                var hero = (PageHero)d;
                hero.StatsPresenter.Content = e.NewValue;
                hero.StatsPresenter.Visibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
            }));

    public PageHero()
    {
        InitializeComponent();
    }

    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Eyebrow { get => (string)GetValue(EyebrowProperty); set => SetValue(EyebrowProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public object? Stats { get => GetValue(StatsProperty); set => SetValue(StatsProperty, value); }

    private static DependencyProperty Register(string name, Action<PageHero, string> apply) =>
        DependencyProperty.Register(name, typeof(string), typeof(PageHero),
            new PropertyMetadata(string.Empty, (d, e) => apply((PageHero)d, e.NewValue as string ?? string.Empty)));
}
