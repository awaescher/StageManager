namespace StageManager.Core;

/// <summary>Where a window sits on its card when its proportions leave room: 0 is left or top, 1 is right or bottom.</summary>
public readonly record struct CardAlignment(double X, double Y)
{
	public static readonly CardAlignment Centered = new(0.5, 0.5);

	/// <summary>
	/// The bottom corner at the screen edge. The icons of the app sit on that corner, so they touch the window however wide or tall it is,
	/// and the windows of all cards share one line to look along.
	/// </summary>
	public static CardAlignment For(SidebarSide side) => new(side == SidebarSide.Right ? 1 : 0, 1);
}
