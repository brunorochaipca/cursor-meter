using Avalonia.Threading;
using Tmds.DBus.Protocol;

namespace CursorMeter.App.Linux;

/// <summary>
/// Exports the tray menu as com.canonical.dbusmenu. Item ids are the entry index plus one; 0 is the root.
/// </summary>
internal sealed class DBusMenu : IMethodHandler
{
    public const string ObjectPath = "/MenuBar";
    private const string Interface = "com.canonical.dbusmenu";
    private const string LayoutSignature = "(ia{sv}av)";

    private static readonly ReadOnlyMemory<byte> IntrospectionXml = """
        <interface name="com.canonical.dbusmenu">
          <property name="Version" type="u" access="read"/>
          <property name="TextDirection" type="s" access="read"/>
          <property name="Status" type="s" access="read"/>
          <property name="IconThemePath" type="as" access="read"/>
          <method name="GetLayout">
            <arg type="i" direction="in"/><arg type="i" direction="in"/><arg type="as" direction="in"/>
            <arg type="u" direction="out"/><arg type="(ia{sv}av)" direction="out"/>
          </method>
          <method name="GetGroupProperties">
            <arg type="ai" direction="in"/><arg type="as" direction="in"/><arg type="a(ia{sv})" direction="out"/>
          </method>
          <method name="GetProperty">
            <arg type="i" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="out"/>
          </method>
          <method name="Event">
            <arg type="i" direction="in"/><arg type="s" direction="in"/><arg type="v" direction="in"/><arg type="u" direction="in"/>
          </method>
          <method name="EventGroup">
            <arg type="a(isvu)" direction="in"/><arg type="ai" direction="out"/>
          </method>
          <method name="AboutToShow">
            <arg type="i" direction="in"/><arg type="b" direction="out"/>
          </method>
          <method name="AboutToShowGroup">
            <arg type="ai" direction="in"/><arg type="ai" direction="out"/><arg type="ai" direction="out"/>
          </method>
          <signal name="ItemsPropertiesUpdated"><arg type="a(ia{sv})"/><arg type="a(ias)"/></signal>
          <signal name="LayoutUpdated"><arg type="u"/><arg type="i"/></signal>
          <signal name="ItemActivationRequested"><arg type="i"/><arg type="u"/></signal>
        </interface>
        """u8.ToArray();

    private readonly Connection _connection;
    private readonly IReadOnlyList<MenuEntry?> _entries;
    private readonly bool[] _checked;
    private readonly object _gate = new();

    public DBusMenu(Connection connection, IReadOnlyList<MenuEntry?> entries)
    {
        _connection = connection;
        _entries = entries;
        _checked = new bool[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            _checked[i] = entries[i]?.IsChecked?.Invoke() ?? false;
    }

    public string Path => ObjectPath;

    public bool RunMethodHandlerSynchronously(Message message) => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([IntrospectionXml]);
            return default;
        }

        var request = context.Request;
        switch (request.InterfaceAsString, request.MemberAsString)
        {
            case (Interface or null, "GetLayout"):
                GetLayout(context);
                break;
            case (Interface or null, "GetGroupProperties"):
                GetGroupProperties(context);
                break;
            case (Interface or null, "GetProperty"):
                GetProperty(context);
                break;
            case (Interface or null, "Event"):
                Event(context);
                break;
            case (Interface or null, "EventGroup"):
                EventGroup(context);
                break;
            case (Interface or null, "AboutToShow"):
                SyncToggles();
                ReplyBool(context, false);
                break;
            case (Interface or null, "AboutToShowGroup"):
                SyncToggles();
                AboutToShowGroup(context);
                break;
            case ("org.freedesktop.DBus.Properties", "Get"):
                GetMenuProperty(context);
                break;
            case ("org.freedesktop.DBus.Properties", "GetAll"):
                GetAllMenuProperties(context);
                break;
            default:
                context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", $"Unknown method {request.MemberAsString}.");
                break;
        }

        return default;
    }

    /// <summary>
    /// Re-reads the check marks and tells the host about the ones that changed.
    /// </summary>
    public void SyncToggles()
    {
        var changed = new List<int>();
        lock (_gate)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i]?.IsChecked is not { } isChecked)
                    continue;
                var now = isChecked();
                if (now == _checked[i])
                    continue;
                _checked[i] = now;
                changed.Add(i + 1);
            }
        }

        if (changed.Count > 0)
            _connection.TrySendMessage(CreatePropertiesUpdated(changed));
    }

    private MessageBuffer CreatePropertiesUpdated(List<int> ids)
    {
        var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(null, ObjectPath, Interface, "ItemsPropertiesUpdated", "a(ia{sv})a(ias)");
        var updated = writer.WriteArrayStart(DBusType.Struct);
        foreach (var id in ids)
        {
            writer.WriteStructureStart();
            writer.WriteInt32(id);
            WriteProperties(ref writer, id);
        }

        writer.WriteArrayEnd(updated);
        writer.WriteArrayEnd(writer.WriteArrayStart(DBusType.Struct));
        return writer.CreateMessage();
    }

    private void GetLayout(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var parentId = reader.ReadInt32();
        var depth = reader.ReadInt32();
        if (!IsKnown(parentId))
        {
            ReplyUnknownId(context, parentId);
            return;
        }

        var writer = context.CreateReplyWriter("u" + LayoutSignature);
        writer.WriteUInt32(1);
        WriteItem(ref writer, parentId, depth);
        context.Reply(writer.CreateMessage());
    }

    private void WriteItem(ref MessageWriter writer, int id, int depth)
    {
        writer.WriteStructureStart();
        writer.WriteInt32(id);
        WriteProperties(ref writer, id);
        var children = writer.WriteArrayStart(DBusType.Variant);
        if (id == 0 && depth != 0)
        {
            for (var child = 1; child <= _entries.Count; child++)
            {
                writer.WriteSignature(LayoutSignature);
                WriteItem(ref writer, child, depth - 1);
            }
        }

        writer.WriteArrayEnd(children);
    }

    private void GetGroupProperties(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var ids = reader.ReadArrayOfInt32();
        if (ids.Length == 0)
            ids = Enumerable.Range(0, _entries.Count + 1).ToArray();

        var writer = context.CreateReplyWriter("a(ia{sv})");
        var array = writer.WriteArrayStart(DBusType.Struct);
        foreach (var id in ids.Where(IsKnown))
        {
            writer.WriteStructureStart();
            writer.WriteInt32(id);
            WriteProperties(ref writer, id);
        }

        writer.WriteArrayEnd(array);
        context.Reply(writer.CreateMessage());
    }

    private void GetProperty(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var id = reader.ReadInt32();
        var name = reader.ReadString();
        if (!IsKnown(id))
        {
            ReplyUnknownId(context, id);
            return;
        }

        foreach (var (key, value) in Properties(id))
        {
            if (key != name)
                continue;
            var writer = context.CreateReplyWriter("v");
            WriteVariant(ref writer, value);
            context.Reply(writer.CreateMessage());
            return;
        }

        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Item {id} has no property {name}.");
    }

    private void Event(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var id = reader.ReadInt32();
        var eventId = reader.ReadString();
        HandleEvent(id, eventId);
        if (!context.NoReplyExpected)
            context.Reply(context.CreateReplyWriter(null).CreateMessage());
    }

    private void EventGroup(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        var end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            var id = reader.ReadInt32();
            var eventId = reader.ReadString();
            reader.ReadVariantValue();
            reader.ReadUInt32();
            HandleEvent(id, eventId);
        }

        var writer = context.CreateReplyWriter("ai");
        writer.WriteArrayEnd(writer.WriteArrayStart(DBusType.Int32));
        context.Reply(writer.CreateMessage());
    }

    private void HandleEvent(int id, string eventId)
    {
        if (eventId == "opened" && id == 0)
        {
            SyncToggles();
            return;
        }

        if (eventId != "clicked" || id < 1 || id > _entries.Count || _entries[id - 1] is not { } entry)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            entry.Run();
            SyncToggles();
        });
    }

    private static void AboutToShowGroup(MethodContext context)
    {
        var writer = context.CreateReplyWriter("aiai");
        writer.WriteArrayEnd(writer.WriteArrayStart(DBusType.Int32));
        writer.WriteArrayEnd(writer.WriteArrayStart(DBusType.Int32));
        context.Reply(writer.CreateMessage());
    }

    private static void GetMenuProperty(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        reader.ReadString();
        var name = reader.ReadString();
        var writer = context.CreateReplyWriter("v");
        if (!WriteMenuProperty(ref writer, name))
        {
            writer.Dispose();
            context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", $"Unknown property {name}.");
            return;
        }

        context.Reply(writer.CreateMessage());
    }

    private static void GetAllMenuProperties(MethodContext context)
    {
        var writer = context.CreateReplyWriter("a{sv}");
        var dict = writer.WriteDictionaryStart();
        foreach (var name in new[] { "Version", "TextDirection", "Status", "IconThemePath" })
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            WriteMenuProperty(ref writer, name);
        }

        writer.WriteDictionaryEnd(dict);
        context.Reply(writer.CreateMessage());
    }

    private static bool WriteMenuProperty(ref MessageWriter writer, string name)
    {
        switch (name)
        {
            case "Version":
                writer.WriteVariantUInt32(3);
                return true;
            case "TextDirection":
                writer.WriteVariantString("ltr");
                return true;
            case "Status":
                writer.WriteVariantString("normal");
                return true;
            case "IconThemePath":
                writer.WriteSignature("as");
                writer.WriteArray(Array.Empty<string>());
                return true;
            default:
                return false;
        }
    }

    private bool IsKnown(int id) => id >= 0 && id <= _entries.Count;

    private List<(string Name, object Value)> Properties(int id)
    {
        if (id == 0)
            return [("children-display", "submenu")];
        if (_entries[id - 1] is not { } entry)
            return [("type", "separator")];

        var properties = new List<(string Name, object Value)> { ("label", entry.Label) };
        if (entry.IsChecked is not null)
        {
            bool isChecked;
            lock (_gate)
                isChecked = _checked[id - 1];
            properties.Add(("toggle-type", "checkmark"));
            properties.Add(("toggle-state", isChecked ? 1 : 0));
        }

        return properties;
    }

    private void WriteProperties(ref MessageWriter writer, int id)
    {
        var dict = writer.WriteDictionaryStart();
        foreach (var (name, value) in Properties(id))
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            WriteVariant(ref writer, value);
        }

        writer.WriteDictionaryEnd(dict);
    }

    private static void WriteVariant(ref MessageWriter writer, object value)
    {
        if (value is int number)
            writer.WriteVariantInt32(number);
        else
            writer.WriteVariantString((string)value);
    }

    private static void ReplyBool(MethodContext context, bool value)
    {
        var writer = context.CreateReplyWriter("b");
        writer.WriteBool(value);
        context.Reply(writer.CreateMessage());
    }

    private static void ReplyUnknownId(MethodContext context, int id)
        => context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unknown menu item {id}.");
}
