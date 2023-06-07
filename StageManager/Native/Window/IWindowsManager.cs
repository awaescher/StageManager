namespace StageManager.Native.Window
{
    public delegate void WindowFocusDelegate(IWindow window);

    public interface IWindowsManager
    {
        IWindowsDeferPosHandle DeferWindowsPos(int count);
        
        event WindowFocusDelegate WindowFocused;

        void ToggleFocusedWindowTiling();
    }
}
