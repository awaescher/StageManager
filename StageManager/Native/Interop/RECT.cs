using System.Runtime.InteropServices;

// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable  InconsistentNaming
// ReSharper disable MemberCanBePrivate.Global

namespace StageManager.Native.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public bool IsEmpty()
        {
            return left == 0 && top == 0 && right == 0 && bottom == 0;
        }
    }
}