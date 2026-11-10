namespace StageManager.Core;

/// <summary>
/// Finds a place on the screen for a window that is out of reach, e.g. one a crashed run left parked without a journal entry.
/// Pure calculation, no Win32.
/// </summary>
public static class OffScreenRescue
{
	/// <summary>How much of a window has to be on a monitor to be grabbed with the mouse. Less counts as lost.</summary>
	public const double MinVisibleWidth = 80;
	public const double MinVisibleHeight = 30;

	/// <summary>True if too little of the window is on any monitor to reach it.</summary>
	public static bool IsLost(Area window, IReadOnlyList<Area> monitors)
	{
		if (monitors.Count == 0 || window.Width <= 0 || window.Height <= 0)
			return false;

		foreach (var m in monitors)
		{
			var width = Math.Min(window.Right, m.Right) - Math.Max(window.Left, m.Left);
			var height = Math.Min(window.Bottom, m.Bottom) - Math.Max(window.Top, m.Top);

			if (width >= Math.Min(MinVisibleWidth, window.Width) && height >= Math.Min(MinVisibleHeight, window.Height))
				return false;
		}

		return true;
	}

	/// <summary>
	/// Where a lost window goes: into the work area of the monitor it is closest to, as near to where it was as possible. It keeps its size;
	/// one that is larger than the work area starts at its top left corner.
	/// </summary>
	/// <param name="workAreas">One per monitor, in the same order.</param>
	/// <returns>Null if the window is not lost.</returns>
	public static Area? Target(Area window, IReadOnlyList<Area> monitors, IReadOnlyList<Area> workAreas)
	{
		if (!IsLost(window, monitors) || workAreas.Count != monitors.Count)
			return null;

		var area = workAreas[ParkingSpot.NearestMonitor(window, monitors)];

		var left = Clamp(window.Left, area.Left, area.Right - window.Width);
		var top = Clamp(window.Top, area.Top, area.Bottom - window.Height);

		return new Area(left, top, left + window.Width, top + window.Height);
	}

	private static double Clamp(double value, double min, double max) => max < min ? min : Math.Clamp(value, min, max);
}
