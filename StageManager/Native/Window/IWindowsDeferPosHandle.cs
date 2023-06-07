using System;

namespace StageManager.Native.Window
{
    public interface IWindowsDeferPosHandle : IDisposable
    {
        void DeferWindowPos(IWindow window, IWindowLocation location);
    }
}
