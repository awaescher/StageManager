using StageManager.Core;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;

namespace StageManager.Strategies
{
	/// <summary>
	/// Hides windows by moving them out of the screen. Unlike minimized windows they keep the picture DWM shows of them,
	/// which Chromium and WebView based windows lose when they are minimized.
	/// A window that cannot be moved (it is minimized already) or the setting "Minimize" fall back to minimizing.
	/// </summary>
	internal class ScreenOffsetWindowStrategy : IWindowStrategy
	{
		private readonly IWindowStrategy _minimize = new NormalizeAndMinimizeWindowStrategy();

		public void Show(IWindow window)
		{
			// whatever way it was hidden, it comes back: parked windows are moved back, minimized ones are restored
			if (Parking.IsParked(window.Handle))
			{
				Parking.Unpark(window.Handle);

				// the window kept its place in the stack while it was out of the screen, the ones of a scene end up above the others
				Win32.SetWindowPos(window.Handle, Win32.HWND_TOP, 0, 0, 0, 0,
					Win32.SetWindowPosFlags.IgnoreMove | Win32.SetWindowPosFlags.IgnoreResize | Win32.SetWindowPosFlags.DoNotActivate);
				return;
			}

			_minimize.Show(window);
		}

		public void Hide(IWindow window)
		{
			if (App.Settings.Current.HideStrategy == HideStrategy.Park && Parking.Park(window.Handle))
				return;

			_minimize.Hide(window);
		}
	}
}
