using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PodpivasEngine.Localization;

namespace PodpivasEngine.Views;

public sealed class PromptDialog : Window
{
    private readonly TextBox _input;

    private PromptDialog(string title, string initial)
    {
        Title = title;
        Width = 380;
        CanResize = false;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#0A0A0A"));

        _input = new TextBox
        {
            Text = initial,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var ok = new Button
        {
            Content = Localizer.Get("Common.Ok"),
            Classes = { "accent" },
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var cancel = new Button
        {
            Content = Localizer.Get("Common.Cancel"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        ok.Click += (_, _) => Close(_input.Text);
        cancel.Click += (_, _) => Close(null);

        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Close(_input.Text);
            else if (e.Key == Key.Escape)
                Close(null);
        };

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 8,
            Children =
            {
                _input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
    }

    public static Task<string?> ShowAsync(Window owner, string title, string initial) =>
        new PromptDialog(title, initial).ShowDialog<string>(owner)!;
}
