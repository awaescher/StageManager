using System.Runtime.InteropServices;

// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable  InconsistentNaming
// ReSharper disable MemberCanBePrivate.Global

namespace StageManager.Native.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;

        [MarshalAs(UnmanagedType.Bool, SizeConst = 4)]
        public bool fVisible;

        public bool fSourceClientAreaOnly;
    }
}