namespace StageManager.Core;

/// <summary>Space between the rectangle of a window and its visible frame. Windows adds invisible resize borders around most windows.</summary>
public readonly record struct Insets(double Left, double Top, double Right, double Bottom)
{
	/// <summary>The usual borders of a window on Windows 10 and 11: invisible at the sides and the bottom, none at the top.</summary>
	public static readonly Insets Default = new(7, 0, 7, 7);
}

/// <summary>Works out where windows start and end their flights. Pure calculation, no Win32.</summary>
public static class FlightPlanner
{
	/// <summary>Where a window with the given proportions shows on a card: fitted into the card and centered, without distortion.</summary>
	public static Pose OnCard(Area card, double sourceWidth, double sourceHeight, double opacity)
	{
		var fit = AspectFit.Calculate(card.Width, card.Height, sourceWidth, sourceHeight);
		return new Pose(card.Left + fit.X, card.Top + fit.Y, fit.Width, fit.Height, opacity);
	}

	public static Pose OfFrame(Area frame, double opacity = 1) => new(frame.Left, frame.Top, frame.Width, frame.Height, opacity);

	public static Insets InsetsOf(Area windowRect, Area frame) => new(
		frame.Left - windowRect.Left,
		frame.Top - windowRect.Top,
		windowRect.Right - frame.Right,
		windowRect.Bottom - frame.Bottom);

	/// <summary>The visible frame of a window of which only the rectangle is known, e.g. the restored position of a minimized window.</summary>
	public static Area FrameOf(Area windowRect, Insets insets)
	{
		var frame = new Area(
			windowRect.Left + insets.Left,
			windowRect.Top + insets.Top,
			windowRect.Right - insets.Right,
			windowRect.Bottom - insets.Bottom);

		// a window that is smaller than its borders keeps its rectangle
		return frame.Width > 0 && frame.Height > 0 ? frame : windowRect;
	}

	public static bool Intersects(Area a, Area b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
}
