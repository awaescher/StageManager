using StageManager.Native.PInvoke;
using StageManager.Native.Window;

namespace StageManager.Strategies
{
    internal class NormalizeAndMinimizeWindowStrategy : IWindowStrategy
	{
		public void Show(IWindow window)
		{
			// never show windows that were hidden by their application, they would reappear as "zombies"
			if (!Win32.IsWindowVisible(window.Handle) && !Win32.IsIconic(window.Handle))
				return;

			// leave visible windows untouched: SW_SHOWNOACTIVATE would reset snapped or maximized windows
			// to their restore size and break the native Windows snap group
			if (Win32.IsWindowVisible(window.Handle) && !Win32.IsIconic(window.Handle))
				return;

			Win32.ShowWindow(window.Handle, Win32.SW.SW_SHOWNOACTIVATE);
		}

		public void Hide(IWindow window)
		{
			// SW_MINIMIZE would make a hidden window visible again (minimized in the taskbar)
			if (!Win32.IsWindowVisible(window.Handle) || Win32.IsIconic(window.Handle))
				return;

			Win32.ShowWindow(window.Handle, Win32.SW.SW_MINIMIZE);
		}
	}
}
