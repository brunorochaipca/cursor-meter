using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CursorMeter.App.Windows;

/// <summary>
/// Experimental administrator mode for Start11 versions prior to v3, which leave the taskbar in a
/// raised window band after closing Start or Task View with the mouse. Duplicating the session's
/// <c>winlogon.exe</c> system token allows setting <c>TokenUIAccess</c> on a relaunched copy of this
/// process so its topmost windows enter <c>ZBID_UIACCESS</c> above the raised taskbar.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UiAccess
{
    public const string MarkerFileName = "experimental-admin";
    private const string ForceFlag = "--uiaccess";
    private const string AttemptedFlag = "--uiaccess-attempted";
    private const string SeTcbPrivilege = "SeTcbPrivilege";

    private static bool? _isActive;

    /// <summary>True when the installer enabled experimental administrator mode (or <c>--uiaccess</c> was passed).</summary>
    /// <remarks>Always false in the Island edition, which ignores the marker file and the switch.</remarks>
    public static bool IsConfigured =>
        AppInfo.IsMainland
        && (File.Exists(Path.Combine(AppContext.BaseDirectory, MarkerFileName))
            || HasSwitch(Environment.GetCommandLineArgs(), ForceFlag));

    /// <summary>True when the current process token already carries <c>TokenUIAccess</c>.</summary>
    public static bool IsActive => _isActive ??= QueryTokenFlag(NativeMethods.TokenUiAccess);

    /// <summary>True when the current process is running elevated as administrator.</summary>
    public static bool IsElevated => QueryTokenFlag(NativeMethods.TokenElevation);

    /// <summary>
    /// Ensures <c>TokenUIAccess</c> when experimental administrator mode is configured.
    /// Returns <see langword="true"/> when a replacement process has been started and the caller should exit.
    /// </summary>
    public static bool RelaunchIfNeeded(string[] args)
    {
        if (!AppInfo.IsMainland || (!IsConfigured && !HasSwitch(args, ForceFlag)))
            return false;

        if (IsActive)
        {
            AppLog.Info("UIAccess active (experimental administrator mode)");
            return false;
        }

        if (HasSwitch(args, AttemptedFlag))
        {
            AppLog.Error("UIAccess remained inactive after relaunch; continuing without UIAccess");
            return false;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        if (!IsElevated)
            return ElevateForUiAccess(exePath, args);

        if (!TryCreateUiAccessToken(out var uiAccessToken, out int tokenError))
        {
            AppLog.Error($"Could not create UIAccess token (Win32 error {tokenError}); continuing without UIAccess");
            return false;
        }

        try
        {
            if (StartWithToken(uiAccessToken, exePath, args, out int launchError))
                return true;

            AppLog.Error($"Could not start UIAccess process (Win32 error {launchError}); continuing without UIAccess");
            return false;
        }
        finally
        {
            NativeMethods.CloseHandle(uiAccessToken);
        }
    }

    private static bool ElevateForUiAccess(string exePath, string[] args)
    {
        if (args.Length == 0 && StartupShortcut.TryRunScheduledTask())
            return true;

        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = BuildArguments(args, includeAttempted: false),
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
            });
            return started is not null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppLog.Info("Administrator elevation declined; continuing without UIAccess");
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Error("Could not elevate for UIAccess", ex);
            return false;
        }
    }

    private static bool TryCreateUiAccessToken(out IntPtr uiAccessToken, out int error)
    {
        uiAccessToken = IntPtr.Zero;
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TokenQuery | NativeMethods.TokenDuplicate,
                out var selfToken))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            if (!NativeMethods.GetTokenInformation(
                    selfToken,
                    NativeMethods.TokenSessionId,
                    out uint sessionId,
                    sizeof(uint),
                    out _))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            if (!TryDuplicateWinlogonToken(sessionId, out var systemToken, out error))
                return false;

            try
            {
                if (!NativeMethods.SetThreadToken(IntPtr.Zero, systemToken))
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }

                try
                {
                    if (!NativeMethods.DuplicateTokenEx(
                            selfToken,
                            NativeMethods.TokenQuery
                                | NativeMethods.TokenDuplicate
                                | NativeMethods.TokenAssignPrimary
                                | NativeMethods.TokenAdjustDefault,
                            IntPtr.Zero,
                            NativeMethods.SecurityAnonymous,
                            NativeMethods.TokenPrimary,
                            out uiAccessToken))
                    {
                        error = Marshal.GetLastWin32Error();
                        return false;
                    }

                    uint enabled = 1;
                    if (!NativeMethods.SetTokenInformation(
                            uiAccessToken,
                            NativeMethods.TokenUiAccess,
                            ref enabled,
                            sizeof(uint)))
                    {
                        error = Marshal.GetLastWin32Error();
                        NativeMethods.CloseHandle(uiAccessToken);
                        uiAccessToken = IntPtr.Zero;
                        return false;
                    }

                    error = 0;
                    return true;
                }
                finally
                {
                    NativeMethods.RevertToSelf();
                }
            }
            finally
            {
                NativeMethods.CloseHandle(systemToken);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(selfToken);
        }
    }

    private static bool TryDuplicateWinlogonToken(uint sessionId, out IntPtr systemToken, out int error)
    {
        systemToken = IntPtr.Zero;
        if (!NativeMethods.LookupPrivilegeValue(null, SeTcbPrivilege, out var tcbLuid))
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        var required = new NativeMethods.PrivilegeSet
        {
            PrivilegeCount = 1,
            Control = NativeMethods.PrivilegeSetAllNecessary,
            Privilege = new NativeMethods.LuidAndAttributes { Luid = tcbLuid },
        };

        error = 1168; // ERROR_NOT_FOUND
        var processes = Process.GetProcessesByName("winlogon");
        try
        {
            foreach (var candidate in processes)
            {
                var processHandle = NativeMethods.OpenProcess(
                    NativeMethods.ProcessQueryLimitedInformation,
                    false,
                    candidate.Id);
                if (processHandle == IntPtr.Zero)
                    continue;

                try
                {
                    if (!NativeMethods.OpenProcessToken(
                            processHandle,
                            NativeMethods.TokenQuery | NativeMethods.TokenDuplicate,
                            out var token))
                        continue;

                    try
                    {
                        if (!NativeMethods.PrivilegeCheck(token, ref required, out bool hasTcb) || !hasTcb)
                            continue;
                        if (!NativeMethods.GetTokenInformation(
                                token,
                                NativeMethods.TokenSessionId,
                                out uint tokenSession,
                                sizeof(uint),
                                out _)
                            || tokenSession != sessionId)
                            continue;

                        if (NativeMethods.DuplicateTokenEx(
                                token,
                                NativeMethods.TokenImpersonate,
                                IntPtr.Zero,
                                NativeMethods.SecurityImpersonation,
                                NativeMethods.TokenImpersonation,
                                out systemToken))
                        {
                            error = 0;
                            return true;
                        }

                        error = Marshal.GetLastWin32Error();
                        return false;
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(token);
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(processHandle);
                }
            }

            return false;
        }
        finally
        {
            foreach (var candidate in processes)
                candidate.Dispose();
        }
    }

    private static bool StartWithToken(IntPtr token, string exePath, string[] args, out int error)
    {
        string arguments = BuildArguments(args, includeAttempted: true);
        var commandLine = new StringBuilder($"\"{exePath}\"");
        if (arguments.Length > 0)
        {
            commandLine.Append(' ');
            commandLine.Append(arguments);
        }

        var startupInfo = new NativeMethods.StartupInfo
        {
            Cb = Marshal.SizeOf<NativeMethods.StartupInfo>(),
        };

        bool started = NativeMethods.CreateProcessAsUser(
            token,
            exePath,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            NativeMethods.CreateUnicodeEnvironment,
            IntPtr.Zero,
            AppContext.BaseDirectory,
            ref startupInfo,
            out var processInfo);

        if (!started)
        {
            started = NativeMethods.CreateProcessWithTokenW(
                token,
                0,
                exePath,
                commandLine,
                NativeMethods.CreateUnicodeEnvironment,
                IntPtr.Zero,
                AppContext.BaseDirectory,
                ref startupInfo,
                out processInfo);
        }

        if (!started)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        NativeMethods.CloseHandle(processInfo.Process);
        NativeMethods.CloseHandle(processInfo.Thread);
        error = 0;
        return true;
    }

    private static bool QueryTokenFlag(int tokenInfoClass)
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TokenQuery,
                out var token))
            return false;

        try
        {
            return NativeMethods.GetTokenInformation(
                    token,
                    tokenInfoClass,
                    out uint value,
                    sizeof(uint),
                    out _)
                && value != 0;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static bool HasSwitch(string[] args, string flag)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string BuildArguments(string[] args, bool includeAttempted)
    {
        var list = new List<string>(args.Length + 1);
        foreach (var arg in args)
        {
            if (string.Equals(arg, AttemptedFlag, StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(QuoteArgument(arg));
        }

        if (includeAttempted)
            list.Add(AttemptedFlag);

        return string.Join(' ', list);
    }

    private static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && !arg.AsSpan().ContainsAny(' ', '\t', '"'))
            return arg;
        return $"\"{arg.Replace("\"", "\\\"")}\"";
    }
}
