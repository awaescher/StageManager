using StageManager.Native.PInvoke;
using System;

namespace StageManager.Native
{
    public static class FocusStealer
    {
        public static void Steal(IntPtr windowToFocus)
        {
            Win32.keybd_event(0, 0, 0, UIntPtr.Zero);
            Win32.SetForegroundWindow(windowToFocus);
        }
    }
}
