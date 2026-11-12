using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Tmds.DBus.Protocol;

namespace CursorMeter.App.Linux;

/// <summary>
/// The Linux tray icon. Avalonia's tray cannot set XAyatanaLabel, the text that GNOME's AppIndicator
/// extension and Ayatana indicator panels show next to the icon, so this exports its own StatusNotifierItem.
/// </summary>
internal sealed class StatusNotifierItem : IMethodHandler, IDisposable
{
    private const string ItemPath = "/StatusNotifierItem";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string WatcherName = "org.kde.StatusNotifierWatcher";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const uint DoNotQueue = 4;
    private const string IconFileName = "cursormeter-tray";

    private static readonly string[] PropertyNames =
    [
        "Category", "Id", "Title", "Status", "WindowId", "IconThemePath", "IconName", "IconPixmap",
        "OverlayIconName", "OverlayIconPixmap", "AttentionIconName", "AttentionIconPixmap", "AttentionMovieName",
        "ToolTip", "ItemIsMenu", "Menu", "XAyatanaLabel", "XAyatanaLabelGuide",
    ];

    private static readonly ReadOnlyMemory<byte> IntrospectionXml = """
        <interface name="org.kde.StatusNotifierItem">
          <property name="Category" type="s" access="read"/>
          <property name="Id" type="s" access="read"/>
          <property name="Title" type="s" access="read"/>
          <property name="Status" type="s" access="read"/>
          <property name="WindowId" type="i" access="read"/>
          <property name="IconThemePath" type="s" access="read"/>
          <property name="IconName" type="s" access="read"/>
          <property name="IconPixmap" type="a(iiay)" access="read"/>
          <property name="OverlayIconName" type="s" access="read"/>
          <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
          <property name="AttentionIconName" type="s" access="read"/>
          <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
          <property name="AttentionMovieName" type="s" access="read"/>
          <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
          <property name="ItemIsMenu" type="b" access="read"/>
          <property name="Menu" type="o" access="read"/>
          <property name="XAyatanaLabel" type="s" access="read"/>
          <property name="XAyatanaLabelGuide" type="s" access="read"/>
          <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
          <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
          <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
          <method name="XAyatanaSecondaryActivate"><arg name="timestamp" type="u" direction="in"/></method>
          <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
          <signal name="NewTitle"/>
          <signal name="NewIcon"/>
          <signal name="NewAttentionIcon"/>
          <signal name="NewOverlayIcon"/>
          <signal name="NewToolTip"/>
          <signal name="NewStatus"><arg name="status" type="s"/></signal>
          <signal name="XAyatanaNewLabel"><arg name="label" type="s"/><arg name="guide" type="s"/></signal>
        </interface>
        """u8.ToArray();

    private readonly Connection _connection;
    private readonly DBusMenu _menu;
    private readonly Action _activate;
    private readonly string _busName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
    private readonly byte[] _iconArgb;
    private readonly string _iconThemePath;
    private IDisposable? _watcherSubscription;
    private volatile string _label = "";
    private volatile string _toolTip = "";
    private bool _disposed;

    private StatusNotifierItem(Connection connection, IReadOnlyList<MenuEntry?> menu, Action activate)
    {
        _connection = connection;
        _menu = new DBusMenu(connection, menu);
        _activate = activate;
        using var icon = AppIcon.Render();
        _iconArgb = IconArgb(icon);
        _iconThemePath = WriteIconFile(icon);
    }

    /// <summary>
    /// Returns null when there is no session bus, so the caller can keep Avalonia's tray icon.
    /// </summary>
    public static StatusNotifierItem? TryCreate(IReadOnlyList<MenuEntry?> menu, Action activate)
    {
        var address = Address.Session;
        if (string.IsNullOrEmpty(address))
            return null;

        var connection = new Connection(address);
        try
        {
            Task.Run(() => connection.ConnectAsync().AsTask()).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is ConnectException or NotSupportedException or ArgumentException or IOException)
        {
            connection.Dispose();
            return null;
        }

        var item = new StatusNotifierItem(connection, menu, activate);
        connection.AddMethodHandler(item);
        connection.AddMethodHandler(item._menu);
        _ = item.StartAsync();
        return item;
    }

    public string Path => ItemPath;

    public void Show(MeterState state)
    {
        var label = state.Quotas is { } quotas ? QuotaText.PanelLabel(quotas) : state.Status ?? state.Detail;
        if (label != _label)
        {
            _label = label;
            _connection.TrySendMessage(CreateSignal("XAyatanaNewLabel", label, QuotaText.PanelLabelGuide));
        }

        if (state.Detail != _toolTip)
        {
            _toolTip = state.Detail;
            _connection.TrySendMessage(CreateSignal("NewToolTip"));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcherSubscription?.Dispose();
        _connection.Dispose();
    }

    public bool RunMethodHandlerSynchronously(Message message) => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([IntrospectionXml]);
            AnnounceLabel();
            return default;
        }

        var request = context.Request;
        switch (request.InterfaceAsString, request.MemberAsString)
        {
            case (ItemInterface or null, "Activate" or "SecondaryActivate" or "XAyatanaSecondaryActivate"):
                Dispatcher.UIThread.Post(_activate);
                ReplyEmpty(context);
                break;
            case (ItemInterface or null, "ContextMenu" or "Scroll"):
                ReplyEmpty(context);
                break;
            case (PropertiesInterface, "Get"):
                GetProperty(context);
                break;
            case (PropertiesInterface, "GetAll"):
                GetAllProperties(context);
                break;
            default:
                context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", $"Unknown method {request.MemberAsString}.");
                break;
        }

        return default;
    }

    // GNOME's AppIndicator extension (v58) ignores the label it reads while setting up the icon and
    // only draws one that arrives in XAyatanaNewLabel and differs from what it cached. It introspects
    // the item once that setup is done, so blank the label and send it again at that point.
    private void AnnounceLabel()
    {
        var label = _label;
        if (label.Length == 0)
            return;
        _connection.TrySendMessage(CreateSignal("XAyatanaNewLabel", "", QuotaText.PanelLabelGuide));
        _connection.TrySendMessage(CreateSignal("XAyatanaNewLabel", label, QuotaText.PanelLabelGuide));
    }

    private async Task StartAsync()
    {
        try
        {
            await _connection.CallMethodAsync(CreateRequestName()).ConfigureAwait(false);
            // GNOME Shell hosts the watcher, so it goes away and comes back whenever the shell or the extension restarts.
            _watcherSubscription = await _connection.AddMatchAsync(
                new MatchRule
                {
                    Type = MessageType.Signal,
                    Sender = "org.freedesktop.DBus",
                    Interface = "org.freedesktop.DBus",
                    Member = "NameOwnerChanged",
                    Arg0 = WatcherName,
                },
                static (message, _) => ReadNewOwner(message),
                (exception, owner, _, _) =>
                {
                    if (exception is null && owner.Length > 0)
                        _ = RegisterAsync();
                },
                ObserverFlags.None,
                readerState: null,
                handlerState: null,
                emitOnCapturedContext: false).ConfigureAwait(false);
            await RegisterAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DBusException or DisconnectedException or ObjectDisposedException)
        {
        }
    }

    private async Task RegisterAsync()
    {
        try
        {
            await _connection.CallMethodAsync(CreateRegister()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DBusException or DisconnectedException or ObjectDisposedException)
        {
            // No watcher yet: NameOwnerChanged registers the item once one appears.
        }
    }

    private static string ReadNewOwner(Message message)
    {
        var reader = message.GetBodyReader();
        reader.ReadString();
        reader.ReadString();
        return reader.ReadString();
    }

    private MessageBuffer CreateRequestName()
    {
        var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", "su", MessageFlags.None);
        writer.WriteString(_busName);
        writer.WriteUInt32(DoNotQueue);
        return writer.CreateMessage();
    }

    private MessageBuffer CreateRegister()
    {
        var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(WatcherName, "/StatusNotifierWatcher", WatcherName, "RegisterStatusNotifierItem", "s", MessageFlags.NoAutoStart);
        writer.WriteString(_busName);
        return writer.CreateMessage();
    }

    private MessageBuffer CreateSignal(string member, params string[] args)
    {
        var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(null, ItemPath, ItemInterface, member, args.Length == 0 ? null : new string('s', args.Length));
        foreach (var arg in args)
            writer.WriteString(arg);
        return writer.CreateMessage();
    }

    private void GetProperty(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        reader.ReadString();
        var name = reader.ReadString();
        var writer = context.CreateReplyWriter("v");
        if (!WriteProperty(ref writer, name))
        {
            writer.Dispose();
            context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"Unknown property {name}.");
            return;
        }

        context.Reply(writer.CreateMessage());
    }

    private void GetAllProperties(MethodContext context)
    {
        var writer = context.CreateReplyWriter("a{sv}");
        var dict = writer.WriteDictionaryStart();
        foreach (var name in PropertyNames)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            WriteProperty(ref writer, name);
        }

        writer.WriteDictionaryEnd(dict);
        context.Reply(writer.CreateMessage());
    }

    private bool WriteProperty(ref MessageWriter writer, string name)
    {
        switch (name)
        {
            case "Category":
                writer.WriteVariantString("ApplicationStatus");
                return true;
            case "Id":
                writer.WriteVariantString("cursormeter");
                return true;
            case "Title":
                writer.WriteVariantString("CursorMeter");
                return true;
            case "Status":
                writer.WriteVariantString("Active");
                return true;
            case "WindowId":
                writer.WriteVariantInt32(0);
                return true;
            case "IconThemePath":
                writer.WriteVariantString(_iconThemePath);
                return true;
            case "IconName":
                writer.WriteVariantString(_iconThemePath.Length > 0 ? IconFileName : "");
                return true;
            case "OverlayIconName" or "AttentionIconName" or "AttentionMovieName":
                writer.WriteVariantString("");
                return true;
            case "IconPixmap":
                writer.WriteSignature("a(iiay)");
                WritePixmaps(ref writer, _iconArgb);
                return true;
            case "OverlayIconPixmap" or "AttentionIconPixmap":
                writer.WriteSignature("a(iiay)");
                WritePixmaps(ref writer, null);
                return true;
            case "ToolTip":
                writer.WriteSignature("(sa(iiay)ss)");
                writer.WriteStructureStart();
                writer.WriteString("");
                WritePixmaps(ref writer, null);
                writer.WriteString("CursorMeter");
                writer.WriteString(_toolTip);
                return true;
            case "ItemIsMenu":
                writer.WriteVariantBool(false);
                return true;
            case "Menu":
                writer.WriteVariantObjectPath(DBusMenu.ObjectPath);
                return true;
            case "XAyatanaLabel":
                writer.WriteVariantString(_label);
                return true;
            case "XAyatanaLabelGuide":
                writer.WriteVariantString(QuotaText.PanelLabelGuide);
                return true;
            default:
                return false;
        }
    }

    private static void WritePixmaps(ref MessageWriter writer, byte[]? argb)
    {
        var array = writer.WriteArrayStart(DBusType.Struct);
        if (argb is not null)
        {
            writer.WriteStructureStart();
            writer.WriteInt32(AppIcon.Size);
            writer.WriteInt32(AppIcon.Size);
            writer.WriteArray(argb);
        }

        writer.WriteArrayEnd(array);
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (!context.NoReplyExpected)
            context.Reply(context.CreateReplyWriter(null).CreateMessage());
    }

    // Ayatana's indicator service (the Xfce and MATE indicator plugins) only loads icons by name, so the
    // pixmap is also written where IconThemePath points. Hosts that prefer the name draw the same image.
    private static string WriteIconFile(RenderTargetBitmap bitmap)
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtimeDir))
            return "";
        try
        {
            var dir = System.IO.Path.Combine(runtimeDir, "cursormeter");
            Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, IconFileName + ".png");
            bitmap.Save(file + ".tmp");
            File.Move(file + ".tmp", file, overwrite: true);
            return dir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    // StatusNotifierItem pixmaps are ARGB32 in network byte order. The icon is opaque, so premultiplied alpha changes nothing.
    private static byte[] IconArgb(RenderTargetBitmap bitmap)
    {
        var pixels = new byte[AppIcon.Size * AppIcon.Size * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, AppIcon.Size, AppIcon.Size), handle.AddrOfPinnedObject(), pixels.Length, AppIcon.Size * 4);
        }
        finally
        {
            handle.Free();
        }

        var rgba = bitmap.Format == PixelFormat.Rgba8888;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var (red, blue) = rgba ? (pixels[i], pixels[i + 2]) : (pixels[i + 2], pixels[i]);
            var green = pixels[i + 1];
            pixels[i] = pixels[i + 3];
            pixels[i + 1] = red;
            pixels[i + 2] = green;
            pixels[i + 3] = blue;
        }

        return pixels;
    }
}
