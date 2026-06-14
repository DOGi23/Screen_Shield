using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ScreenShield.Security
{
    [SupportedOSPlatform("windows")]
    public static class AntiDebug
    {
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] ref bool isDebuggerPresent);

        // List of well-known reverse engineering, decompilation, and cracking tools.
        private static readonly string[] CrackingProcesses = new[]
        {
            "dnspy",
            "ilspy",
            "cheatengine",
            "ollydbg",
            "x64dbg",
            "x32dbg",
            "processhacker",
            "ida",
            "ida64",
            "de4dot",
            "dotpeek",
            "wireshark"
        };

        /// <summary>
        /// Performs comprehensive checks to detect debugger attachment or hacking software.
        /// </summary>
        public static void PerformChecks()
        {
            // 1. Check managed .NET debugger
            if (Debugger.IsAttached)
            {
                CrashProcess("Managed debugger detected");
            }

            // 2. Check Win32 API IsDebuggerPresent
            if (IsDebuggerPresent())
            {
                CrashProcess("PEB debugger flag active");
            }

            // 3. Check Win32 API CheckRemoteDebuggerPresent
            bool isRemoteDebuggerPresent = false;
            try
            {
                if (CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isRemoteDebuggerPresent) && isRemoteDebuggerPresent)
                {
                    CrashProcess("Remote debugger attached");
                }
            }
            catch { }

            // 4. Scan processes for blacklisted cracking tools
            try
            {
                var processes = Process.GetProcesses();
                foreach (var process in processes)
                {
                    try
                    {
                        string name = process.ProcessName.ToLowerInvariant();
                        foreach (var blacklisted in CrackingProcesses)
                        {
                            if (name.Contains(blacklisted))
                            {
                                CrashProcess($"Unauthorized tool detected: {process.ProcessName}");
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Instantly terminates the process using FailFast, giving debuggers no chance to intercept the state.
        /// </summary>
        private static void CrashProcess(string reason)
        {
            // FailFast terminates the process immediately without triggering try/finally blocks or finalizers
            Environment.FailFast($"[ScreenShield Protection] Security violation: {reason}");
        }
    }
}
