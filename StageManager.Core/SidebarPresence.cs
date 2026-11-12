namespace StageManager.Core;

public enum SidebarMode
{
	/// <summary>Visible at its place, below other windows.</summary>
	OnScreen,

	/// <summary>Hidden next to the screen edge.</summary>
	OffScreen,

	/// <summary>Visible above all windows, brought back by the pointer at the screen edge.</summary>
	Flyover
}

/// <summary>What decides whether the sidebar shows, at one moment.</summary>
/// <param name="Overlapped">A window of the stage covers the place of the sidebar (maximized or reaching far enough below it).</param>
/// <param name="Hold">Something on the sidebar is in use (a card is dragged, a menu is open): it must not go away now.</param>
/// <param name="Summon">The keyboard asks for the sidebar, e.g. while a scene is picked: it shows as if the pointer was at the screen edge.</param>
public readonly record struct SidebarInput(double MouseX, double MouseY, bool Overlapped, bool Hold, double Now, bool Summon = false);

/// <summary>
/// Decides when the sidebar shows, hides or flies over the windows. Pure logic.
/// <para>
/// The sidebar never stays "on screen" below a window that covers it: then it would be invisible and the screen edge could not bring it back.
/// It hides only after the pointer has been away for a moment, so a pointer that slips off its edge does not make it flicker.
/// </para>
/// </summary>
public sealed class SidebarPresence
{
	/// <summary>Seconds the pointer has to be away from the sidebar before a flying sidebar hides.</summary>
	public const double HideDelay = 0.35;

	private readonly SidebarGeometry _geometry;
	private readonly double _triggerZone;
	private double _lastNear = double.NegativeInfinity;

	public SidebarPresence(SidebarGeometry geometry, double triggerZone = SidebarGeometry.DefaultTriggerZone)
	{
		_geometry = geometry;
		_triggerZone = triggerZone;
	}

	public SidebarMode Decide(SidebarMode current, SidebarInput input)
	{
		var area = _geometry.Area;
		var onMonitor = input.MouseY >= area.Top && input.MouseY <= area.Bottom;
		var atEdge = input.Summon || (onMonitor && _geometry.IsInTriggerZone(input.MouseX, _triggerZone));
		var over = onMonitor && _geometry.IsOverSidebar(input.MouseX);
		var visible = current != SidebarMode.OffScreen;

		if (input.Hold || atEdge || (over && visible))
			_lastNear = input.Now;

		if (!input.Overlapped)
		{
			// nothing covers it: it lives at its place, a flying one stays above while the pointer is on it
			return current == SidebarMode.Flyover && (over || input.Hold) ? SidebarMode.Flyover : SidebarMode.OnScreen;
		}

		if (input.Hold && visible)
			return SidebarMode.Flyover;

		if (atEdge || (over && visible))
			return SidebarMode.Flyover;

		if (current == SidebarMode.OffScreen)
			return SidebarMode.OffScreen;

		return input.Now - _lastNear < HideDelay ? SidebarMode.Flyover : SidebarMode.OffScreen;
	}
}
