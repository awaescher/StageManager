using StageManager.Native.Window;
using System;
using System.Runtime.InteropServices;

namespace StageManager.Strategies
{
	/// <summary>
	/// Works well with opacity = 0, higher opacity will make the windows appear when clicked
	/// Visual Studio cannot be hidden this way, might be the same with other windows
	/// </summary>
	internal class OpacityWindowStrategy : IWindowStrategy
	{
		[DllImport("user32.dll")]
		static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		[DllImport("user32.dll")]
		static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll")]
		public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

		public const int GWL_EXSTYLE = -20;
		public const int WS_EX_LAYERED = 0x80000;
		public const int LWA_ALPHA = 0x2;

		public void Show(IWindow window)
		{
			_ = SetWindowLong(window.Handle, GWL_EXSTYLE, GetWindowLong(window.Handle, GWL_EXSTYLE) | WS_EX_LAYERED);
			SetLayeredWindowAttributes(window.Handle, 0, 255, LWA_ALPHA);

			window.BringToTop();
		}

		public void Hide(IWindow window)
		{
			_ = SetWindowLong(window.Handle, GWL_EXSTYLE, GetWindowLong(window.Handle, GWL_EXSTYLE) | WS_EX_LAYERED);
			SetLayeredWindowAttributes(window.Handle, 0, 0, LWA_ALPHA);
		}
	}
}