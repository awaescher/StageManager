namespace StageManager.Core;

/// <summary>Where the four edges of a frame lie. Pure geometry, so it can be tested without a screen.</summary>
public static class FrameEdges
{
	/// <summary>The rectangles of the four edges in the order top, bottom, left, right. The side edges sit between top and bottom, nothing overlaps.</summary>
	public static (int X, int Y, int Width, int Height)[] Of(double x, double y, double width, double height, int thickness)
	{
		var left = (int)Math.Round(x);
		var top = (int)Math.Round(y);
		var w = Math.Max(1, (int)Math.Round(width));
		var h = Math.Max(1, (int)Math.Round(height));
		var t = Math.Clamp(thickness, 1, Math.Max(1, Math.Min(w, h) / 2));
		var inner = Math.Max(1, h - 2 * t);

		return new[]
		{
			(left, top, w, t),
			(left, top + h - t, w, t),
			(left, top + t, t, inner),
			(left + w - t, top + t, t, inner)
		};
	}
}
