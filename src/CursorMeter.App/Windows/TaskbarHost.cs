using Avalonia;
using Avalonia.Threading;

namespace CursorMeter.App.Windows;

/// <summary>
/// Keeps the bars inside the taskbar. When Explorer restarts it destroys its child windows,
/// the bars included, so the host builds a new widget and embeds it again.
/// </summary>
internal sealed class TaskbarHost : IDisposable
{
    private readonly IReadOnlyList<MenuEntry?> _menu;
    private readonly ShellWindow _shell = new();
    private readonly DispatcherTimer _placeTimer;
    private WidgetWindow? _widget;
    private TaskbarEmbedder? _embedder;
    private MeterState? _state;
    private bool _disposed;

    public TaskbarHost(IReadOnlyList<MenuEntry?> menu)
    {
        _menu = menu;
        _shell.TaskbarCreated += (_, _) => KeepOnTaskbar();
        _placeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _placeTimer.Tick += (_, _) => KeepOnTaskbar();
        CreateWidget();
        _placeTimer.Start();
    }

    public event EventHandler? Clicked;

    /// <summary>Where the flyout should open from, in screen pixels.</summary>
    public PixelRect? Anchor => _widget is { IsAlive: true } widget ? widget.ScreenBounds : null;

    public void Show(MeterState state)
    {
        _state = state;
        _widget?.ShowState(state);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _placeTimer.Stop();
        _embedder?.Dispose();
        _widget?.Close();
        _shell.Dispose();
    }

    private void CreateWidget()
    {
        var widget = new WidgetWindow();
        var embedder = new TaskbarEmbedder(widget);
        widget.Dragged += (_, point) => embedder.DragTo(point.X, point.Y);
        widget.Clicked += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        widget.MenuRequested += (_, _) => _shell.ShowMenu(_menu);
        widget.Closed += (_, _) => OnWidgetClosed(widget, embedder);
        if (_state is not null)
            widget.ShowState(_state);

        _widget = widget;
        _embedder = embedder;
        widget.Show();
        embedder.Attach();
    }

    private void OnWidgetClosed(WidgetWindow widget, TaskbarEmbedder embedder)
    {
        embedder.Dispose();
        if (_widget != widget)
            return;
        _widget = null;
        _embedder = null;
        if (!_disposed)
            Dispatcher.UIThread.Post(KeepOnTaskbar);
    }

    private void KeepOnTaskbar()
    {
        if (_disposed)
            return;
        if (_widget is not { IsAlive: true } || _embedder is null)
        {
            _embedder?.Dispose();
            CreateWidget();
            return;
        }

        if (_widget.IsDragging)
            return;

        _embedder.Attach();
    }
}
