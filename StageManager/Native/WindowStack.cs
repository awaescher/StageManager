using StageManager.Native.PInvoke;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StageManager.Native
{
	/// <summary>Reads and sets the order in which windows lie on top of each other.</summary>
	public static class WindowStack
	{
		/// <summary>All top level windows, the top one first.</summary>
		public static IReadOnlyList<long> TopToBottom()
		{
			var windows = new List<long>();
			Win32.EnumWindows((handle, _) =>
			{
				windows.Add(handle.ToInt64());
				return true;
			}, IntPtr.Zero);

			return windows;
		}

		/// <summary>
		/// Lays the windows on top of each other in this order, the last one on top of all, without activating any.
		/// Windows that belong to them, like dialogs, stay above their owner.
		/// </summary>
		public static void Restack(IEnumerable<IntPtr> backToFront)
		{
			foreach (var window in backToFront.Where(w => Win32.IsWindow(w) && !Win32.IsIconic(w)))
			{
				var flags = Win32.SetWindowPosFlags.IgnoreMove | Win32.SetWindowPosFlags.IgnoreResize | Win32.SetWindowPosFlags.DoNotActivate;

				// an app that hangs must not block the switch, its window may end up out of order then
				if (ProcessInfo.IsHung(window))
					flags |= Win32.SetWindowPosFlags.AsynchronousWindowPosition;

				Win32.SetWindowPos(window, Win32.HWND_TOP, 0, 0, 0, 0, flags);
			}
		}
	}
}
