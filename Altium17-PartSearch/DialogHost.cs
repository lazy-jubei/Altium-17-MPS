using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Altium17PartSearch
{
    internal static class DialogHost
    {
        // An explicit Altium owner keeps WPF's hidden taskbar owner from placing
        // a modal dialog behind the disabled editor window under Wine.
        internal static bool? Show(Window window)
        {
            var owner = GetActiveWindow();
            if (owner == IntPtr.Zero) owner = GetForegroundWindow();
            if (owner != IntPtr.Zero)
            {
                GetWindowThreadProcessId(owner, out uint processId);
                if (processId == (uint)Process.GetCurrentProcess().Id)
                    new WindowInteropHelper(window).Owner = owner;
            }
            return window.ShowDialog();
        }

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
