using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace DnwSus.Agent
{
    /// <summary>
    /// Angemeldete Benutzer ermitteln und die Hinweis-exe in deren Sitzung starten.
    /// Der Agent läuft als SYSTEM in Sitzung 0 und kann dort nichts anzeigen; deshalb wird der Hinweis
    /// mit dem Token des Benutzers auf dessen Desktop gestartet (CreateProcessAsUser).
    /// </summary>
    internal static class UserSession
    {
        public sealed class ActiveSession
        {
            public int SessionId;
            public string UserName;
        }

        /// <summary>Aktive Sitzung mit angemeldetem Benutzer: zuerst die Konsole, sonst eine aktive RDP-Sitzung.</summary>
        public static ActiveSession GetActiveSession()
        {
            uint console = WTSGetActiveConsoleSessionId();
            if (console != 0xFFFFFFFF)
            {
                string user = GetSessionUser((int)console);
                if (user.Length > 0) return new ActiveSession { SessionId = (int)console, UserName = user };
            }

            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out IntPtr buffer, out int count)) return null;
            try
            {
                int size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    var info = (WTS_SESSION_INFO)Marshal.PtrToStructure(buffer + i * size, typeof(WTS_SESSION_INFO));
                    if (info.State != WtsActive || info.SessionId == 0) continue;
                    string user = GetSessionUser(info.SessionId);
                    if (user.Length > 0) return new ActiveSession { SessionId = info.SessionId, UserName = user };
                }
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
            return null;
        }

        private static string GetSessionUser(int sessionId)
        {
            string user = QuerySessionString(sessionId, WtsUserName);
            if (user.Length == 0) return "";
            string domain = QuerySessionString(sessionId, WtsDomainName);
            return domain.Length > 0 ? domain + "\\" + user : user;
        }

        private static string QuerySessionString(int sessionId, int infoClass)
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out IntPtr buffer, out _)) return "";
            try { return Marshal.PtrToStringUni(buffer) ?? ""; }
            finally { WTSFreeMemory(buffer); }
        }

        /// <summary>
        /// Startet ein Programm in der Sitzung des Benutzers und ruft <paramref name="onExit"/> mit dem
        /// Exit-Code auf, sobald es beendet ist (in einem eigenen Thread).
        /// </summary>
        public static int StartInSession(int sessionId, string exePath, string arguments, Action<int> onExit)
        {
            IntPtr userToken = IntPtr.Zero, primaryToken = IntPtr.Zero, environment = IntPtr.Zero;
            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken)) throw new Win32Exception();
                if (!DuplicateTokenEx(userToken, MaximumAllowed, IntPtr.Zero, SecurityIdentification, TokenPrimary, out primaryToken))
                    throw new Win32Exception();
                if (!CreateEnvironmentBlock(out environment, primaryToken, false)) environment = IntPtr.Zero;

                var startup = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = @"winsta0\default" };
                string commandLine = "\"" + exePath + "\" " + arguments;
                if (!CreateProcessAsUser(primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                        CreateUnicodeEnvironment | CreateNoWindow, environment,
                        System.IO.Path.GetDirectoryName(exePath), ref startup, out PROCESS_INFORMATION process))
                    throw new Win32Exception();

                CloseHandle(process.hThread);
                IntPtr processHandle = process.hProcess;
                var waiter = new Thread(() =>
                {
                    try
                    {
                        WaitForSingleObject(processHandle, Infinite);
                        GetExitCodeProcess(processHandle, out uint exitCode);
                        onExit((int)exitCode);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Fehler beim Warten auf den Hinweis", ex);
                    }
                    finally
                    {
                        CloseHandle(processHandle);
                    }
                }) { IsBackground = true, Name = "Hinweis-Warten" };
                waiter.Start();
                return process.dwProcessId;
            }
            finally
            {
                if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
                if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }

        // ---- Win32 ----
        private const int WtsActive = 0;
        private const int WtsUserName = 5;
        private const int WtsDomainName = 7;
        private const uint MaximumAllowed = 0x02000000;
        private const int SecurityIdentification = 1;
        private const int TokenPrimary = 1;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint CreateNoWindow = 0x08000000;
        private const uint Infinite = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public int SessionId;
            [MarshalAs(UnmanagedType.LPWStr)] public string WinStationName;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);
        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WTSQuerySessionInformationW")]
        private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
        [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);
        [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessAsUserW")]
        private static extern bool CreateProcessAsUser(IntPtr token, string application, string commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
            ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    }
}
