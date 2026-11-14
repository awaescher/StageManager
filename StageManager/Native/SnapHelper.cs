using StageManager.Native.PInvoke;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace StageManager.Native
{
	/// <summary>
	/// Stateless helpers to detect windows that were arranged with the Windows snap feature
	/// and to determine which of them form a contiguous layout.
	/// </summary>
	public static class SnapHelper
	{
		/// <summary>
		/// Tolerance in pixels for two window edges to be considered as touching.
		/// </summary>
		public const int ADJACENCY_TOLERANCE = 12;

		private static bool _isWindowArrangedAvailable = true;

		/// <summary>
		/// Determines whether a window is snapped (arranged) by Windows.
		/// </summary>
		public static bool IsArranged(IntPtr handle)
		{
			if (Win32.IsIconic(handle) || Win32.IsZoomed(handle))
				return false;

			if (_isWindowArrangedAvailable)
			{
				try
				{
					return Win32.IsWindowArranged(handle);
				}
				catch (EntryPointNotFoundException)
				{
					_isWindowArrangedAvailable = false;
				}
			}

			// fallback: a snapped window keeps its restore rectangle while its actual rectangle differs
			var placement = new Win32.WINDOWPLACEMENT { length = Marshal.SizeOf<Win32.WINDOWPLACEMENT>() };
			if (!Win32.GetWindowPlacement(handle, ref placement))
				return false;

			var rect = new Win32.Rect();
			Win32.GetWindowRect(handle, ref rect);

			var normal = placement.rcNormalPosition;
			return normal.Right - normal.Left != rect.Right - rect.Left
				|| normal.Bottom - normal.Top != rect.Bottom - rect.Top;
		}

		/// <summary>
		/// Gets the window rectangle as used by SetWindowPos (including the invisible resize borders).
		/// </summary>
		public static Rectangle GetWindowRect(IntPtr handle)
		{
			var rect = new Win32.Rect();
			Win32.GetWindowRect(handle, ref rect);
			return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
		}

		/// <summary>
		/// Gets the visible frame of a window without the invisible resize borders.
		/// </summary>
		public static Rectangle GetFrameRect(IntPtr handle)
		{
			var size = Marshal.SizeOf<Win32.Rect>();
			if (Win32.DwmGetWindowAttribute(handle, (int)Win32.DwmWindowAttribute.DWMWA_EXTENDED_FRAME_BOUNDS, out Win32.Rect rect, size) == 0)
				return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

			return GetWindowRect(handle);
		}

		/// <summary>
		/// Gets the monitor a window is (mostly) placed on.
		/// </summary>
		public static IntPtr GetMonitor(IntPtr handle) => Win32.MonitorFromWindow(handle, Win32.MONITOR_DEFAULTTONEAREST);

		/// <summary>
		/// Determines whether two rectangles touch each other at one edge.
		/// </summary>
		public static bool AreAdjacent(Rectangle a, Rectangle b, int tolerance = ADJACENCY_TOLERANCE)
		{
			var overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
			var overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

			var touchHorizontally = (Math.Abs(a.Right - b.Left) <= tolerance || Math.Abs(b.Right - a.Left) <= tolerance) && overlapY > tolerance;
			var touchVertically = (Math.Abs(a.Bottom - b.Top) <= tolerance || Math.Abs(b.Bottom - a.Top) <= tolerance) && overlapX > tolerance;

			return touchHorizontally || touchVertically;
		}

		/// <summary>
		/// Determines whether two rectangles overlap by more than the given tolerance in both directions.
		/// </summary>
		public static bool OverlapsSignificantly(Rectangle a, Rectangle b, int tolerance = ADJACENCY_TOLERANCE)
		{
			var overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
			var overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

			return overlapX > tolerance && overlapY > tolerance;
		}

		/// <summary>
		/// Gets all items that are connected to the start item through a chain of adjacent rectangles (including the start item).
		/// </summary>
		public static List<T> GetConnected<T>(T start, IEnumerable<T> candidates, Func<T, Rectangle> getRect)
		{
			var remaining = candidates.Where(c => !EqualityComparer<T>.Default.Equals(c, start)).ToList();
			var result = new List<T> { start };
			var queue = new Queue<T>();
			queue.Enqueue(start);

			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				var currentRect = getRect(current);

				var neighbours = remaining.Where(r => AreAdjacent(currentRect, getRect(r))).ToArray();
				foreach (var neighbour in neighbours)
				{
					remaining.Remove(neighbour);
					result.Add(neighbour);
					queue.Enqueue(neighbour);
				}
			}

			return result;
		}
	}
}
