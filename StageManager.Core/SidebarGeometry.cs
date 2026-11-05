namespace StageManager.Core;

/// <summary>
/// A rectangle in any unit, as long as all values passed to <see cref="SidebarGeometry"/> use the same one (physical pixels in practice).
/// </summary>
public readonly record struct Area(double Left, double Top, double Right, double Bottom)
{
	public double Width => Right - Left;

	public double Height => Bottom - Top;
}

/// <summary>
/// Everything that depends on the side the sidebar sits on. Has no WPF or Win32 dependencies.
/// </summary>
public sealed record SidebarGeometry(Area Area, double Width, SidebarSide Side)
{
	public const double DefaultTriggerZone = 44;

	private bool IsLeft => Side == SidebarSide.Left;

	/// <summary>Left edge of the sidebar window while it is visible.</summary>
	public double OnScreenLeft => IsLeft ? Area.Left : Area.Right - Width;

	/// <summary>Left edge of the sidebar window while it is hidden next to its screen edge.</summary>
	public double OffScreenLeft => IsLeft ? Area.Left - Width : Area.Right;

	public double GetLeft(bool visible) => visible ? OnScreenLeft : OffScreenLeft;

	/// <summary>True if the mouse is at the screen edge of the sidebar and should bring it back.</summary>
	public bool IsInTriggerZone(double mouseX, double zone = DefaultTriggerZone) =>
		IsLeft
			? mouseX >= Area.Left && mouseX <= Area.Left + zone
			: mouseX <= Area.Right && mouseX >= Area.Right - zone;

	/// <summary>True if the mouse is above the area the visible sidebar covers.</summary>
	public bool IsOverSidebar(double mouseX) =>
		IsLeft
			? mouseX >= Area.Left && mouseX <= Area.Left + Width
			: mouseX <= Area.Right && mouseX >= Area.Right - Width;

	/// <summary>True if the mouse is beyond the sidebar on the side of the screen, e.g. after dragging a window out of it.</summary>
	public bool IsPulledOutOfSidebar(double mouseX) =>
		IsLeft ? mouseX > Area.Left + Width : mouseX < Area.Right - Width;

	/// <summary>True if a window in normal state reaches far enough below the sidebar that the sidebar should hide (half its width).</summary>
	public bool DoesNormalWindowOverlap(double windowLeft, double windowWidth) =>
		IsLeft
			? (windowLeft - Area.Left) * 2 < Width
			: (windowLeft + windowWidth) > Area.Right - Width / 2;
}
