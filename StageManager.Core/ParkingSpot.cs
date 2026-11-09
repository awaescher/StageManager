namespace StageManager.Core;

/// <summary>Finds a place for a window outside of all monitors. Has no Win32 dependencies.</summary>
public static class ParkingSpot
{
	/// <summary>How far outside of the outermost monitor a parked window sits.</summary>
	public const double Margin = 160;

	/// <summary>
	/// A rectangle of the size of <paramref name="window"/> beyond the edge of the screen, chosen so that Windows still counts the
	/// window to the monitor it is on now. Otherwise the window would be scaled when the two monitors have a different DPI.
	/// </summary>
	/// <returns>Null if there is no such place, e.g. without monitors.</returns>
	public static Area? Find(Area window, IReadOnlyList<Area> monitors)
	{
		if (monitors.Count == 0 || window.Width <= 0 || window.Height <= 0)
			return null;

		var home = NearestMonitor(window, monitors);

		var left = monitors.Min(m => m.Left);
		var top = monitors.Min(m => m.Top);
		var right = monitors.Max(m => m.Right);
		var bottom = monitors.Max(m => m.Bottom);

		var candidates = new[]
		{
			new Area(left - Margin - window.Width, window.Top, left - Margin, window.Bottom),
			new Area(right + Margin, window.Top, right + Margin + window.Width, window.Bottom),
			new Area(window.Left, bottom + Margin, window.Right, bottom + Margin + window.Height),
			new Area(window.Left, top - Margin - window.Height, window.Right, top - Margin)
		};

		foreach (var candidate in candidates)
		{
			if (NearestMonitor(candidate, monitors) == home && !monitors.Any(m => FlightPlanner.Intersects(m, candidate)))
				return candidate;
		}

		return null;
	}

	/// <summary>The monitor Windows assigns a rectangle to: the one it overlaps most, or if there is none the closest one.</summary>
	public static int NearestMonitor(Area rect, IReadOnlyList<Area> monitors)
	{
		var best = -1;
		var bestOverlap = 0.0;

		for (var i = 0; i < monitors.Count; i++)
		{
			var overlap = OverlapArea(rect, monitors[i]);
			if (overlap > bestOverlap)
			{
				best = i;
				bestOverlap = overlap;
			}
		}

		if (best >= 0)
			return best;

		var closest = 0;
		var closestDistance = double.MaxValue;
		for (var i = 0; i < monitors.Count; i++)
		{
			var distance = DistanceSquared(rect, monitors[i]);
			if (distance < closestDistance)
			{
				closest = i;
				closestDistance = distance;
			}
		}

		return closest;
	}

	private static double OverlapArea(Area a, Area b)
	{
		var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
		var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
		return width > 0 && height > 0 ? width * height : 0;
	}

	private static double DistanceSquared(Area a, Area b)
	{
		var dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
		var dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
		return dx * dx + dy * dy;
	}
}
