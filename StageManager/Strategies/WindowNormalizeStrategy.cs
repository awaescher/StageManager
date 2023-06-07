using StageManager.Native.PInvoke;
using StageManager.Native.Window;

namespace StageManager.Strategies
{
    internal class WindowNormalizeStrategy : IWindowStrategy
	{
		public void Invoke(IWindow window)
		{
			Win32.ShowWindow(window.Handle, Win32.SW.SW_SHOWNOACTIVATE);
		}
	}
}
