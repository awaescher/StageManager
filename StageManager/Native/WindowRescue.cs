using StageManager.Core;
using StageManager.Native.PInvoke;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace StageManager.Native
{
	/// <summary>
	/// Brings back app windows that are out of reach: left out of the screen by a run that crashed before it could write its journal,
	/// by a move that never arrived at an app that hung, or by a monitor that was unplugged.
	/// </summary>
	public static class WindowRescue
	{
		/// <param name="skip">Windows that are out of the screen on purpose, e.g. the ones parked right now.</param>
		/// <returns>The number of windows that were brought back.</returns>
		public static int Run(Func<IntPtr, bool>? skip = null)
		{
			var monitors = Screen.AllScreens;
			var bounds = monitors.Select(s => ToArea(s.Bounds)).ToList();
			var workAreas = monitors.Select(s => ToArea(s.WorkingArea)).ToList();
			var ownProcess = Environment.ProcessId;
			var rescued = 0;

			foreach (var window in Candidates(ownProcess))
			{
				if (skip?.Invoke(window) == true)
					continue;

				var rect = new Win32.Rect();
				if (!Win32.GetWindowRect(window, ref rect))
					continue;

				if (OffScreenRescue.Target(new Area(rect.Left, rect.Top, rect.Right, rect.Bottom), bounds, workAreas) is not Area target)
					continue;

				// asynchronous, an app that hangs must not block the start or the end of StageManager
				var moved = Win32.SetWindowPos(window, IntPtr.Zero, (int)target.Left, (int)target.Top, 0, 0,
					Win32.SetWindowPosFlags.IgnoreResize | Win32.SetWindowPosFlags.IgnoreZOrder | Win32.SetWindowPosFlags.DoNotActivate
					| Win32.SetWindowPosFlags.FrameChanged | Win32.SetWindowPosFlags.AsynchronousWindowPosition);

				if (moved)
				{
					rescued++;
					Log.Warn($"Brought back a window that was out of the screen at {rect.Left},{rect.Top}: {Describe(window)}");
				}
			}

			return rescued;
		}

		/// <summary>The windows a user can work with: visible, not minimized, not cloaked, own their taskbar button.</summary>
		private static List<IntPtr> Candidates(int ownProcess)
		{
			var windows = new List<IntPtr>();

			Win32.EnumWindows((handle, _) =>
			{
				if (IsCandidate(handle, ownProcess))
					windows.Add(handle);

				return true;
			}, IntPtr.Zero);

			return windows;
		}

		private static bool IsCandidate(IntPtr window, int ownProcess)
		{
			if (!Win32.IsWindowVisible(window) || Win32.IsIconic(window) || Win32Helper.IsCloaked(window))
				return false;

			if (!Win32Helper.IsAppWindow(window) || !Win32Helper.IsAltTabWindow(window))
				return false;

			Win32.GetWindowThreadProcessId(window, out var processId);
			if (processId == 0 || processId == ownProcess)
				return false;

			var rect = new Win32.Rect();
			if (!Win32.GetWindowRect(window, ref rect)
				|| rect.Right - rect.Left < WindowFilter.MinWidth || rect.Bottom - rect.Top < WindowFilter.MinHeight)
				return false;

			var className = new System.Text.StringBuilder(256);
			Win32.GetClassName(window, className, className.Capacity);

			return WindowFilter.IsCandidate(new WindowFacts(className.ToString(), ProcessNameOf(processId), IsRoot: true, IsMinimized: false,
				rect.Right - rect.Left, rect.Bottom - rect.Top));
		}

		private static string ProcessNameOf(uint processId)
		{
			var path = ProcessInfo.GetExecutablePath(processId);
			return string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetFileNameWithoutExtension(path);
		}

		private static string Describe(IntPtr window)
		{
			var title = new System.Text.StringBuilder(256);
			Win32.GetWindowText(window, title, title.Capacity);
			Win32.GetWindowThreadProcessId(window, out var processId);
			return $"\"{title}\" ({ProcessNameOf(processId)})";
		}

		private static Area ToArea(System.Drawing.Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);
	}
}
