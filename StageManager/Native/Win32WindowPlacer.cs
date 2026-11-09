using StageManager.Core;
using StageManager.Native.PInvoke;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StageManager.Native
{
	/// <summary>Moves windows with Win32 for <see cref="WindowParker"/>.</summary>
	public sealed class Win32WindowPlacer : IWindowPlacer
	{
		private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

		public bool Exists(long window, int processId)
		{
			var handle = new IntPtr(window);
			return Win32.IsWindow(handle) && ProcessIdOf(window) == processId;
		}

		public int ProcessIdOf(long window)
		{
			Win32.GetWindowThreadProcessId(new IntPtr(window), out var processId);
			return (int)processId;
		}

		public Area? GetRect(long window)
		{
			var rect = new Win32.Rect();
			return Win32.GetWindowRect(new IntPtr(window), ref rect) ? new Area(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
		}

		public Area? GetFrame(long window)
		{
			if (Win32.DwmGetWindowAttribute(new IntPtr(window), DWMWA_EXTENDED_FRAME_BOUNDS, out Win32.Rect frame, Marshal.SizeOf<Win32.Rect>()) == 0
				&& frame.Right > frame.Left && frame.Bottom > frame.Top)
				return new Area(frame.Left, frame.Top, frame.Right, frame.Bottom);

			return GetRect(window);
		}

		public bool IsMinimized(long window) => Win32.IsIconic(new IntPtr(window));

		/// <summary>
		/// Asynchronous, so an app that hangs cannot block StageManager. The size stays, the window keeps its z-order and is not activated.
		/// </summary>
		public bool Move(long window, Area rect, bool refreshFrame)
		{
			var flags = Win32.SetWindowPosFlags.IgnoreResize
				| Win32.SetWindowPosFlags.IgnoreZOrder
				| Win32.SetWindowPosFlags.DoNotActivate
				| Win32.SetWindowPosFlags.DoNotChangeOwnerZOrder
				| Win32.SetWindowPosFlags.AsynchronousWindowPosition;

			// Chromium windows lose their rounded corners if their frame is not recalculated after they came back
			if (refreshFrame)
				flags |= Win32.SetWindowPosFlags.FrameChanged;

			return Win32.SetWindowPos(new IntPtr(window), IntPtr.Zero, (int)rect.Left, (int)rect.Top, 0, 0, flags);
		}

		public IReadOnlyList<long> OwnedWindows(long window)
		{
			var owned = new List<long>();
			var owner = new IntPtr(window);

			Win32.EnumWindows((handle, _) =>
			{
				if (Win32.GetWindow(handle, Win32.GW.GW_OWNER) == owner && Win32.IsWindowVisible(handle))
					owned.Add(handle.ToInt64());

				return true;
			}, IntPtr.Zero);

			return owned;
		}

		public IReadOnlyList<Area> Monitors() =>
			Screen.AllScreens.Select(s => new Area(s.Bounds.Left, s.Bounds.Top, s.Bounds.Right, s.Bounds.Bottom)).ToList();
	}

	/// <summary>The one parker of the app. Windows hidden by moving them out of the screen are listed in a journal on disk.</summary>
	public static class Parking
	{
		private static readonly Lazy<WindowParker> Instance = new(() =>
			new WindowParker(new Win32WindowPlacer(), new FileParkingJournal(Path.Combine(AppPaths.DataDirectory, "parked-windows.json"))));

		public static bool IsParked(IntPtr window) => Instance.Value.IsParked(window.ToInt64());

		public static bool Park(IntPtr window) => Instance.Value.Park(window.ToInt64());

		public static bool Unpark(IntPtr window) => Instance.Value.Unpark(window.ToInt64());

		public static void Forget(IntPtr window) => Instance.Value.Forget(window.ToInt64());

		public static Area? OriginalFrameOf(IntPtr window) => Instance.Value.OriginalFrameOf(window.ToInt64());

		public static int RestoreAll()
		{
			// nothing was parked if the parker was never created, but a journal of a killed run may still exist
			var restored = Instance.Value.RestoreAll();
			if (restored > 0)
				Log.Info($"{restored} parked window(s) brought back");

			return restored;
		}

		public static int Recover()
		{
			var restored = Instance.Value.Recover();
			if (restored > 0)
				Log.Warn($"{restored} window(s) left out of the screen by a previous run were brought back");

			return restored;
		}
	}
}
