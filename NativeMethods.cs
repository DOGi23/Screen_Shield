using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ScreenShield.Security
{
    public static class NativeMethods
    {
        // --- Process & Thread APIs ---
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        // --- Window APIs ---
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // --- System Parameters ---
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SystemParametersInfo(uint uAction, uint uParam, string lpvParam, uint fuWinIni);

        // --- Shell Icon APIs ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", SetLastError = true)]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public const int SHCNE_ASSOCCHANGED = 0x08000000;
        public const uint SHCNF_FLUSH = 0x1000;

        // --- Constants ---
        public const uint PROCESS_CREATE_THREAD = 0x0002;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint PROCESS_VM_READ = 0x0010;

        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint MEM_RELEASE = 0x8000;
        public const uint PAGE_EXECUTE_READWRITE = 0x40;

        public const uint INFINITE = 0xFFFFFFFF;

        public const uint WM_COMMAND = 0x0111;
        public const int TOGGLE_DESKTOP_ICONS = 0x7402;

        public const uint SPI_SETDESKTOPWALLPAPER = 0x0014;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_LARGEICON = 0x000000000;

        // Display Affinity Flags
        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_MONITOR = 0x00000001;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        // --- Helper Methods ---

        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool Is64BitProcess(IntPtr hProcess)
        {
            if (!Environment.Is64BitOperatingSystem)
                return false;

            bool isWow64;
            if (!IsWow64Process(hProcess, out isWow64))
                return true; // Fallback to 64-bit on failure

            return !isWow64;
        }

        public static bool SetForeignWindowAffinity(IntPtr hwnd, uint affinity)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return false;

            // Open the target process with required memory and thread privileges
            IntPtr hProcess = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                false,
                pid
            );

            if (hProcess == IntPtr.Zero)
            {
                // Access Denied: likely trying to access an elevated process from a non-elevated app
                return false;
            }

            try
            {
                IntPtr pUser32 = GetModuleHandle("user32.dll");
                IntPtr pFunc = GetProcAddress(pUser32, "SetWindowDisplayAffinity");
                if (pFunc == IntPtr.Zero) return false;

                bool is64 = Is64BitProcess(hProcess);
                byte[] shellcode;

                if (is64)
                {
                    // --- x64 Shellcode Assembly ---
                    // 48 83 ec 28            sub rsp, 40 (0x28)
                    // 48 b9 [8-bytes hwnd]   mov rcx, hwnd
                    // ba [4-bytes affinity]  mov edx, affinity
                    // 48 b8 [8-bytes func]   mov rax, pFunc
                    // ff d0                  call rax
                    // 48 83 c4 28            add rsp, 40 (0x28)
                    // c3                     ret

                    shellcode = new byte[36];

                    // sub rsp, 40
                    shellcode[0] = 0x48; shellcode[1] = 0x83; shellcode[2] = 0xEC; shellcode[3] = 0x28;

                    // mov rcx, hwnd
                    shellcode[4] = 0x48; shellcode[5] = 0xB9;
                    byte[] hwndBytes = BitConverter.GetBytes(hwnd.ToInt64());
                    Array.Copy(hwndBytes, 0, shellcode, 6, 8);

                    // mov edx, affinity
                    shellcode[14] = 0xBA;
                    byte[] affBytes = BitConverter.GetBytes(affinity);
                    Array.Copy(affBytes, 0, shellcode, 15, 4);

                    // mov rax, pFunc
                    shellcode[19] = 0x48; shellcode[20] = 0xB8;
                    byte[] funcBytes = BitConverter.GetBytes(pFunc.ToInt64());
                    Array.Copy(funcBytes, 0, shellcode, 21, 8);

                    // call rax
                    shellcode[29] = 0xFF; shellcode[30] = 0xD0;

                    // add rsp, 40
                    shellcode[31] = 0x48; shellcode[32] = 0x83; shellcode[33] = 0xC4; shellcode[34] = 0x28;

                    // ret
                    shellcode[35] = 0xC3;
                }
                else
                {
                    // --- x86 Shellcode Assembly ---
                    // 68 [4-bytes affinity]  push affinity
                    // 68 [4-bytes hwnd]      push hwnd
                    // b8 [4-bytes func]      mov eax, pFunc
                    // ff d0                  call eax
                    // c3                     ret

                    shellcode = new byte[18];

                    // push affinity
                    shellcode[0] = 0x68;
                    byte[] affBytes = BitConverter.GetBytes(affinity);
                    Array.Copy(affBytes, 0, shellcode, 1, 4);

                    // push hwnd
                    shellcode[5] = 0x68;
                    byte[] hwndBytes = BitConverter.GetBytes(hwnd.ToInt32());
                    Array.Copy(hwndBytes, 0, shellcode, 6, 4);

                    // mov eax, pFunc
                    shellcode[10] = 0xB8;
                    byte[] funcBytes = BitConverter.GetBytes(pFunc.ToInt32());
                    Array.Copy(funcBytes, 0, shellcode, 11, 4);

                    // call eax
                    shellcode[15] = 0xFF; shellcode[16] = 0xD0;

                    // ret
                    shellcode[17] = 0xC3;
                }

                // Allocate memory in target process
                IntPtr pAlloc = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)shellcode.Length, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                if (pAlloc == IntPtr.Zero) return false;

                // Write shellcode
                IntPtr bytesWritten;
                bool success = WriteProcessMemory(hProcess, pAlloc, shellcode, (uint)shellcode.Length, out bytesWritten);
                if (!success)
                {
                    VirtualFreeEx(hProcess, pAlloc, 0, MEM_RELEASE);
                    return false;
                }

                // Execute shellcode in the remote thread
                uint threadId;
                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, pAlloc, IntPtr.Zero, 0, out threadId);
                if (hThread == IntPtr.Zero)
                {
                    VirtualFreeEx(hProcess, pAlloc, 0, MEM_RELEASE);
                    return false;
                }

                // Wait for the thread to complete (500ms timeout is plenty for this rapid call)
                WaitForSingleObject(hThread, 1000);

                // Cleanup
                CloseHandle(hThread);
                VirtualFreeEx(hProcess, pAlloc, 0, MEM_RELEASE);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        // --- Taskbar List COM Interface for Hiding Windows ---
        [ComImport]
        [Guid("56fdf342-fd6d-11d0-958a-006097c9a090")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ITaskbarList
        {
            [PreserveSig]
            void HrInit();
            
            [PreserveSig]
            void AddTab(IntPtr hwnd);
            
            [PreserveSig]
            void DeleteTab(IntPtr hwnd);
            
            [PreserveSig]
            void ActivateTab(IntPtr hwnd);
            
            [PreserveSig]
            void SetActiveAlt(IntPtr hwnd);
        }

        [ComImport]
        [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
        [ClassInterface(ClassInterfaceType.None)]
        public class TaskbarList
        {
        }

        private static ITaskbarList? _taskbarList;

        public static void DeleteWindowFromTaskbar(IntPtr hwnd)
        {
            try
            {
                if (_taskbarList == null)
                {
                    _taskbarList = (ITaskbarList)new TaskbarList();
                    _taskbarList.HrInit();
                }
                _taskbarList.DeleteTab(hwnd);
            }
            catch { }
        }

        public static void AddWindowToTaskbar(IntPtr hwnd)
        {
            try
            {
                if (_taskbarList == null)
                {
                    _taskbarList = (ITaskbarList)new TaskbarList();
                    _taskbarList.HrInit();
                }
                _taskbarList.AddTab(hwnd);
            }
            catch { }
        }
    }
}
