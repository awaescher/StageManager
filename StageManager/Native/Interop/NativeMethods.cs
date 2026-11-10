using System;
using System.Runtime.InteropServices;

namespace StageManager.Native.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeSize
    {
        public int cx;
        public int cy;
    }

    public static class NativeMethods
    {
        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUnregisterThumbnail(IntPtr thumb);

        /// <summary>Waits until DWM has composed the next frame.</summary>
        [DllImport("dwmapi.dll")]
        public static extern int DwmFlush();

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out NativeSize size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnail, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, DWMWINDOWATTRIBUTE dwAttribute, out bool pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        /// <summary>Switches the animation Windows plays when a window is minimized, restored or shown off for this window.</summary>
        public static bool SetTransitionsDisabled(IntPtr hWnd, bool disabled)
        {
            var value = disabled ? 1 : 0;
            return DwmSetWindowAttribute(hWnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int)) == 0;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public int ptMinX, ptMinY;
            public int ptMaxX, ptMaxY;
            public RECT rcNormalPosition;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_SHOWMAXIMIZED = 3;

        /// <summary>
        /// The rectangle a window gets when it is restored, as Windows keeps it: in workspace coordinates, which differ from screen
        /// coordinates when the taskbar is at the top or the left.
        /// </summary>
        public static bool TryGetNormalPosition(IntPtr hWnd, out RECT rect)
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            var ok = GetWindowPlacement(hWnd, ref placement);
            rect = placement.rcNormalPosition;
            return ok && rect.right > rect.left && rect.bottom > rect.top;
        }

        /// <summary>Puts a window into the normal or the maximized state without activating it. <paramref name="normal"/> in workspace coordinates.</summary>
        public static bool SetPlacement(IntPtr hWnd, RECT normal, bool maximized)
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(hWnd, ref placement))
                return false;

            placement.flags = 0;
            placement.showCmd = maximized ? SW_SHOWMAXIMIZED : SW_SHOWNOACTIVATE;
            placement.rcNormalPosition = normal;
            return SetWindowPlacement(hWnd, ref placement);
        }

        /// <summary>Size of a window as it will be after it is restored. Only the proportions are of interest.</summary>
        public static bool TryGetRestoredSize(IntPtr hWnd, out (double Width, double Height) size)
        {
            if (TryGetRestoredRect(hWnd, out var rect, out _))
            {
                size = (rect.right - rect.left, rect.bottom - rect.top);
                return true;
            }

            size = default;
            return false;
        }

        private const int WPF_RESTORETOMAXIMIZED = 0x0002;

        /// <summary>The rectangle a minimized window gets back when it is restored, and whether it will be maximized.</summary>
        public static bool TryGetRestoredRect(IntPtr hWnd, out RECT rect, out bool maximized)
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (GetWindowPlacement(hWnd, ref placement))
            {
                var w = placement.rcNormalPosition.right - placement.rcNormalPosition.left;
                var h = placement.rcNormalPosition.bottom - placement.rcNormalPosition.top;
                if (w > 0 && h > 0)
                {
                    rect = placement.rcNormalPosition;
                    maximized = (placement.flags & WPF_RESTORETOMAXIMIZED) != 0 || placement.showCmd == 3;
                    return true;
                }
            }

            rect = default;
            maximized = false;
            return false;
        }
    }
}
