using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using SmartMixer.Core;

namespace SmartMixer.Views;

public abstract class PageBase : Page
{
    protected readonly StackPanel ContentPanel;

    protected PageBase(string title, string subtitle)
    {
        Background = new SolidColorBrush(Colors.Transparent);
        var compact = App.Runtime.Configuration.CompactMode;
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled
        };
        ContentPanel = new StackPanel
        {
            Spacing = compact ? 12 : 18,
            Padding = compact ? new Thickness(20, 18, 20, 20) : new Thickness(32, 28, 32, 32),
            MaxWidth = 1100,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ContentPanel.Children.Add(new TextBlock { Text = title, FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        ContentPanel.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["MutedTextBrush"],
            Margin = new Thickness(0, -12, 0, 4)
        });
        var contentViewport = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        contentViewport.Children.Add(ContentPanel);
        contentViewport.SizeChanged += (_, args) =>
        {
            ContentPanel.Width = Math.Min(args.NewSize.Width, ContentPanel.MaxWidth);
        };
        scroll.Content = contentViewport;
        Content = scroll;
    }

    protected Border Card(UIElement child)
    {
        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(App.Runtime.Configuration.CompactMode ? 14 : 20),
            Child = child
        };
        return border;
    }

    protected StackPanel Section(string title)
    {
        var section = new StackPanel { Spacing = 12 };
        section.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        ContentPanel.Children.Add(section);
        return section;
    }

    protected static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = (Brush)Application.Current.Resources["MutedTextBrush"],
        TextWrapping = TextWrapping.Wrap
    };

    protected static Button Action(string label, RoutedEventHandler handler, bool accent = false)
    {
        var button = new Button { Content = label, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += handler;
        return button;
    }

    protected static string ApplicationName(ChannelConfiguration channel) =>
        channel.Application?.DisplayName ?? "Available";
}
