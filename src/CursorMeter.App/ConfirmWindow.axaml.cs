using Avalonia.Controls;
using Avalonia.Input;

namespace CursorMeter.App;

/// <summary>A small modal question with two answers. <see cref="Window.Close()"/> returns true for the accept button.</summary>
internal sealed partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string heading, string message, string accept, string cancel)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        AcceptButton.Content = accept;
        CancelButton.Content = cancel;
        AcceptButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
        Opened += (_, _) => AcceptButton.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Close(false);
    }
}
