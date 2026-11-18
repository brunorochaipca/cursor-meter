using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CursorMeter.App.Windows;

[SupportedOSPlatform("windows")]
internal static class StartupShortcut
{
    public const string ScheduledTaskName = "CursorMeter";

    public static string LinkPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "CursorMeter.lnk");

    public static bool IsEnabled => UiAccess.IsConfigured
        ? QueryScheduledTask() is { Enabled: true, LogonTriggerEnabled: true }
        : File.Exists(LinkPath);

    public static MenuEntry MenuEntry() => new("Iniciar com o Windows", Toggle, () => IsEnabled);

    public static void SetEnabled(bool enabled)
    {
        if (UiAccess.IsConfigured)
        {
            DeleteShortcutIfPresent();
            SetScheduledTaskEnabled(enabled);
            return;
        }

        if (!enabled)
        {
            DeleteShortcutIfPresent();
            return;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell não está disponível.");
        object shellObject = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("WScript.Shell não está disponível.");
        dynamic shell = shellObject;
        dynamic shortcut = shell.CreateShortcut(LinkPath);
        shortcut.TargetPath = Environment.ProcessPath;
        shortcut.WorkingDirectory = AppContext.BaseDirectory;
        shortcut.Description = "Cursor Models | Other Models";
        shortcut.Save();
    }

    /// <summary>
    /// Starts the elevated scheduled task when experimental administrator mode is active and the
    /// user launched <c>CursorMeter.exe</c> from a non-elevated shortcut. The task exists even when
    /// starting with Windows is off (logon trigger disabled), so opening the app needs no UAC prompt.
    /// </summary>
    public static bool TryRunScheduledTask()
    {
        if (QueryScheduledTask() is not { Enabled: true })
            return false;
        return RunSchtasks($"/Run /TN \"{ScheduledTaskName}\"", out _) == 0;
    }

    private static void Toggle()
    {
        try
        {
            SetEnabled(!IsEnabled);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
        }
    }

    private static void DeleteShortcutIfPresent()
    {
        if (File.Exists(LinkPath))
            File.Delete(LinkPath);
    }

    internal readonly record struct ScheduledTaskState(bool Enabled, bool LogonTriggerEnabled);

    private static ScheduledTaskState? QueryScheduledTask()
    {
        if (RunSchtasks($"/Query /TN \"{ScheduledTaskName}\" /XML", out string xml) != 0)
            return null;
        return ParseScheduledTask(xml);
    }

    /// <summary>
    /// Reads the task and logon trigger state from the task XML. Task Scheduler may omit
    /// <c>Enabled</c> elements that keep their default value (true).
    /// </summary>
    internal static ScheduledTaskState? ParseScheduledTask(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null)
                return null;
            var ns = root.Name.Namespace;
            bool EnabledOf(XElement? parent) =>
                parent?.Element(ns + "Enabled") is not { } enabled
                || !string.Equals(enabled.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase);

            bool taskEnabled = EnabledOf(root.Element(ns + "Settings"));
            var logon = root.Element(ns + "Triggers")?.Element(ns + "LogonTrigger");
            return new ScheduledTaskState(taskEnabled, logon is not null && EnabledOf(logon));
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Registers the elevated task. Turning startup off keeps the task with its logon trigger
    /// disabled, so the app can still start elevated from the task without a UAC prompt.
    /// </summary>
    private static void SetScheduledTaskEnabled(bool enabled)
    {
        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Caminho do executável indisponível.");
        string workingDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string userId = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name) ?? "";
        string escapedExe = SecurityElement.Escape(exePath) ?? "";
        string escapedDir = SecurityElement.Escape(workingDir) ?? "";

        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger>
                  <Enabled>{(enabled ? "true" : "false")}</Enabled>
                  <UserId>{userId}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{userId}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{escapedExe}</Command>
                  <WorkingDirectory>{escapedDir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        string tempXml = Path.Combine(Path.GetTempPath(), $"cursormeter-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempXml, xml, Encoding.Unicode);
            if (!RunSchtasksElevated($"/Create /TN \"{ScheduledTaskName}\" /XML \"{tempXml}\" /F"))
                throw new InvalidOperationException("Não foi possível registar a tarefa agendada.");
        }
        finally
        {
            if (File.Exists(tempXml))
                File.Delete(tempXml);
        }
    }

    /// <summary>
    /// Runs schtasks for a task change that needs administrator rights. The process already carries
    /// a UIAccess token at medium integrity, so the change is elevated with a UAC prompt.
    /// </summary>
    private static bool RunSchtasksElevated(string arguments)
    {
        if (UiAccess.IsElevated)
            return RunSchtasks(arguments, out _) == 0;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "runas",
            });
            if (process is null)
                return false;
            process.WaitForExit(30000);
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user declined the elevation prompt.
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static int RunSchtasks(string arguments, out string output)
    {
        output = "";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return -1;
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return -1;
        }
    }
}
