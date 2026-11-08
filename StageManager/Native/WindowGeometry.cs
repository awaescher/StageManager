using StageManager.Core;
using StageManager.Native.Interop;
using StageManager.Native.PInvoke;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StageManager.Native
{
	public static class WindowGeometry
	{
		private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

		/// <summary>
		/// The visible frame of a window, which is what DWM shows as its thumbnail. For a minimized window the frame it gets back when it
		/// is restored. Null if it cannot be read.
		/// </summary>
		public static Area? FrameOf(IntPtr window)
		{
			if (Win32.IsIconic(window))
				return RestoredFrameOf(window);

			if (Win32.IsZoomed(window))
				return WorkAreaOf(window);

			if (Win32.DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out Win32.Rect frame, Marshal.SizeOf<Win32.Rect>()) == 0
				&& frame.Right > frame.Left && frame.Bottom > frame.Top)
				return new Area(frame.Left, frame.Top, frame.Right, frame.Bottom);

			var rect = new Win32.Rect();
			return Win32.GetWindowRect(window, ref rect) ? new Area(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
		}

		private static Area? RestoredFrameOf(IntPtr window)
		{
			if (!NativeMethods.TryGetRestoredRect(window, out var restored, out var maximized))
				return null;

			var rect = new Area(restored.left, restored.top, restored.right, restored.bottom);

			if (maximized)
				return WorkAreaOf(rect);

			return FlightPlanner.FrameOf(rect, Insets.Default);
		}

		private static Area WorkAreaOf(IntPtr window) => ToArea(Screen.FromHandle(window).WorkingArea);

		private static Area WorkAreaOf(Area near) =>
			ToArea(Screen.FromRectangle(new System.Drawing.Rectangle((int)near.Left, (int)near.Top, (int)near.Width, (int)near.Height)).WorkingArea);

		public static Area BoundsOf(Area near) =>
			ToArea(Screen.FromRectangle(new System.Drawing.Rectangle((int)near.Left, (int)near.Top, Math.Max(1, (int)near.Width), Math.Max(1, (int)near.Height))).Bounds);

		private static Area ToArea(System.Drawing.Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);
	}
}
