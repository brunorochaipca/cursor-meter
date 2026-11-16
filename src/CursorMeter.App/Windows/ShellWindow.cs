using System.Runtime.InteropServices;

namespace CursorMeter.App.Windows;

/// <summary>
/// A hidden top-level window. It hears Explorer's TaskbarCreated broadcast, which the widget
/// stops receiving once it is a child of the taskbar, and owns the widget's right-click menu.
/// </summary>
internal sealed class ShellWindow : IDisposable
{
    private const string ClassName = "CursorMeter.ShellWindow";

    private readonly NativeMethods.WndProc _wndProc;
    private readonly uint _taskbarCreated;
    private readonly IntPtr _instance;
    private const uint TrayId = 1;

    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _ownsIcon;
    private string _tip = "CursorMeter";

    public ShellWindow()
    {
        _wndProc = WndProc;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new NativeMethods.WndClassEx
        {
            Size = Marshal.SizeOf<NativeMethods.WndClassEx>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            Instance = _instance,
            ClassName = ClassName,
        };
        NativeMethods.RegisterClassEx(ref windowClass);
        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WsExToolWindow,
            ClassName,
            "CursorMeter.TaskbarWatcher",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            _instance,
            IntPtr.Zero);
    }

    public event EventHandler? TaskbarCreated;

    /// <summary>The notification area icon was clicked, or chosen with the keyboard.</summary>
    public event EventHandler? TrayClicked;

    /// <summary>The notification area icon asked for its menu.</summary>
    public event EventHandler? TrayMenuRequested;

    /// <summary>
    /// Adds the app's icon to the notification area. Avalonia's own tray icon draws its menu with a
    /// flyout that can throw while it closes, which ends the process; a native icon and menu cannot.
    /// </summary>
    public void AddTrayIcon(string tip)
    {
        _tip = tip;
        LoadTrayIcon();
        AddIcon();
    }

    public void SetTooltip(string tip)
    {
        _tip = tip;
        if (_hwnd == IntPtr.Zero || _icon == IntPtr.Zero)
            return;
        var data = TrayData();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimModify, ref data);
    }

    public void ShowMenu(IReadOnlyList<MenuEntry?> entries)
    {
        if (_hwnd == IntPtr.Zero || !NativeMethods.GetCursorPos(out var cursor))
            return;

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;
        try
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is not { } entry)
                {
                    NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, UIntPtr.Zero, null);
                    continue;
                }

                uint flags = NativeMethods.MfString;
                if (entry.IsChecked?.Invoke() == true)
                    flags |= NativeMethods.MfChecked;
                NativeMethods.AppendMenu(menu, flags, (UIntPtr)(uint)(i + 1), entry.Label);
            }

            NativeMethods.SetForegroundWindow(_hwnd);
            int chosen = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmReturnCmd | NativeMethods.TpmRightButton,
                cursor.X,
                cursor.Y,
                _hwnd,
                IntPtr.Zero);
            NativeMethods.PostMessage(_hwnd, NativeMethods.WmNull, IntPtr.Zero, IntPtr.Zero);
            if (chosen > 0 && entries[chosen - 1] is { } picked)
                picked.Run();
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero)
            return;
        if (_icon != IntPtr.Zero)
        {
            var data = TrayData();
            NativeMethods.Shell_NotifyIcon(NativeMethods.NimDelete, ref data);
            if (_ownsIcon)
                NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        NativeMethods.DestroyWindow(_hwnd);
        NativeMethods.UnregisterClass(ClassName, _instance);
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (_taskbarCreated != 0 && message == _taskbarCreated)
        {
            // Explorer starts with an empty notification area, so the icon is added again.
            AppLog.Info("Notification area icon added again after Explorer restarted");
            AddIcon();
            TaskbarCreated?.Invoke(this, EventArgs.Empty);
        }
        else if (message == NativeMethods.WmTray)
        {
            // With NOTIFYICON_VERSION_4 the low word of lParam is the event.
            uint trayEvent = (uint)(lParam.ToInt64() & 0xFFFF);
            if (trayEvent is NativeMethods.NinSelect or NativeMethods.NinKeySelect)
                TrayClicked?.Invoke(this, EventArgs.Empty);
            else if (trayEvent == NativeMethods.WmContextMenu)
                TrayMenuRequested?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void AddIcon()
    {
        if (_hwnd == IntPtr.Zero || _icon == IntPtr.Zero)
            return;
        var data = TrayData();
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NimAdd, ref data))
        {
            AppLog.Error("Could not add the notification area icon");
            return;
        }

        data.Version = 4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimSetVersion, ref data);
    }

    private NativeMethods.NotifyIconData TrayData() => new()
    {
        Size = Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        Window = _hwnd,
        Id = TrayId,
        Flags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip,
        CallbackMessage = NativeMethods.WmTray,
        Icon = _icon,
        Tip = _tip.Length > 127 ? _tip[..127] : _tip,
        Info = "",
        InfoTitle = "",
    };

    /// <summary>The icon embedded in CursorMeter.exe at the notification area's size, or the system's default.</summary>
    private void LoadTrayIcon()
    {
        var path = Environment.ProcessPath;
        int size = Math.Max(16, NativeMethods.GetSystemMetrics(NativeMethods.SmCxSmIcon));
        var icons = new IntPtr[1];
        if (path is not null
            && NativeMethods.PrivateExtractIcons(path, 0, size, size, icons, null, 1, 0) == 1
            && icons[0] != IntPtr.Zero)
        {
            _icon = icons[0];
            _ownsIcon = true;
            return;
        }

        AppLog.Error("Could not read the app icon, using the system's default");
        _icon = NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)NativeMethods.IdiApplication);
        _ownsIcon = false;
    }
}
