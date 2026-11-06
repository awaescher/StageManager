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

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out NativeSize size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnail, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, DWMWINDOWATTRIBUTE dwAttribute, out bool pvAttribute, int cbAttribute);

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

        /// <summary>Size of a window as it will be after it is restored. Only the proportions are of interest.</summary>
        public static bool TryGetRestoredSize(IntPtr hWnd, out (double Width, double Height) size)
        {
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (GetWindowPlacement(hWnd, ref placement))
            {
                var w = placement.rcNormalPosition.right - placement.rcNormalPosition.left;
                var h = placement.rcNormalPosition.bottom - placement.rcNormalPosition.top;
                if (w > 0 && h > 0)
                {
                    size = (w, h);
                    return true;
                }
            }

            size = default;
            return false;
        }
    }
}
