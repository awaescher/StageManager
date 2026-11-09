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

		/// <summary>
		/// The part of a DWM thumbnail that shows the visible frame of the window, without the invisible resize borders around it.
		/// The card in the sidebar and the flying windows both draw this part, so one can take over from the other without a jump.
		/// Null if the size of the thumbnail is unknown.
		/// </summary>
		public static Interop.RECT? FrameSourceOf(IntPtr thumbnail, IntPtr window)
		{
			if (thumbnail == IntPtr.Zero || Interop.NativeMethods.DwmQueryThumbnailSourceSize(thumbnail, out var size) != 0 || size.cx <= 0 || size.cy <= 0)
				return null;

			var insets = InsetsOf(window);
			var rect = new Interop.RECT
			{
				left = (int)Math.Round(insets.Left),
				top = (int)Math.Round(insets.Top),
				right = size.cx - (int)Math.Round(insets.Right),
				bottom = size.cy - (int)Math.Round(insets.Bottom)
			};

			return rect.right > rect.left && rect.bottom > rect.top ? rect : null;
		}

		private static Insets InsetsOf(IntPtr window)
		{
			// a minimized window has no frame to measure, the usual borders are the best guess
			if (Win32.IsIconic(window))
				return Insets.Default;

			var rect = new Win32.Rect();
			if (!Win32.GetWindowRect(window, ref rect)
				|| Win32.DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out Win32.Rect frame, Marshal.SizeOf<Win32.Rect>()) != 0
				|| frame.Right <= frame.Left || frame.Bottom <= frame.Top)
				return Insets.Default;

			var insets = FlightPlanner.InsetsOf(new Area(rect.Left, rect.Top, rect.Right, rect.Bottom), new Area(frame.Left, frame.Top, frame.Right, frame.Bottom));
			return insets.Left < 0 || insets.Top < 0 || insets.Right < 0 || insets.Bottom < 0 ? Insets.Default : insets;
		}

		/// <summary>Width and height of the visible frame of a window, the proportions its thumbnail on a card gets.</summary>
		public static (double Width, double Height) FrameSizeOf(IntPtr window)
		{
			var frame = Parking.OriginalFrameOf(window) ?? FrameOf(window);
			return frame is Area f && f.Width > 0 && f.Height > 0 ? (f.Width, f.Height) : (0, 0);
		}

		private static Area ToArea(System.Drawing.Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);
	}
}
