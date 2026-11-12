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
    private IntPtr _hwnd;

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
        NativeMethods.DestroyWindow(_hwnd);
        NativeMethods.UnregisterClass(ClassName, _instance);
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (_taskbarCreated != 0 && message == _taskbarCreated)
            TaskbarCreated?.Invoke(this, EventArgs.Empty);
        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
