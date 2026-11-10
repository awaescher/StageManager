using StageManager.Core;

namespace StageManager.Tests;

public class SidebarPresenceTests
{
	private static readonly SidebarGeometry Left = new(new Area(0, 0, 1920, 1040), 280, SidebarSide.Left);
	private static readonly SidebarGeometry Right = new(new Area(0, 0, 1920, 1040), 280, SidebarSide.Right);

	private static SidebarInput At(double x, double y = 500, bool overlapped = true, bool hold = false, double now = 0) =>
		new(x, y, overlapped, hold, now);

	[Fact]
	public void Without_covering_windows_it_stays_on_screen()
	{
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.OnScreen, p.Decide(SidebarMode.OnScreen, At(900, overlapped: false)));
		Assert.Equal(SidebarMode.OnScreen, p.Decide(SidebarMode.OffScreen, At(900, overlapped: false)));
	}

	[Fact]
	public void A_covering_window_hides_it_when_the_pointer_is_elsewhere()
	{
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.OffScreen, p.Decide(SidebarMode.OnScreen, At(900, now: 10)));
	}

	[Fact]
	public void A_window_that_covers_it_under_the_pointer_lifts_it_above_instead_of_hiding_it_behind()
	{
		// the bug: a card was clicked, its maximized window came up over the sidebar while the pointer was still on it
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.OnScreen, At(150)));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(20)]
	[InlineData(SidebarGeometry.DefaultTriggerZone)]
	public void The_screen_edge_brings_it_back(double x)
	{
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.OffScreen, At(x)));
	}

	[Fact]
	public void The_screen_edge_works_on_the_right_side()
	{
		var p = new SidebarPresence(Right);

		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.OffScreen, At(1919)));
		Assert.Equal(SidebarMode.OffScreen, p.Decide(SidebarMode.OffScreen, At(5)));
	}

	[Fact]
	public void The_edge_is_ignored_above_or_below_the_monitor()
	{
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.OffScreen, p.Decide(SidebarMode.OffScreen, At(0, y: 1200)));
		Assert.Equal(SidebarMode.OffScreen, p.Decide(SidebarMode.OffScreen, At(0, y: -5)));
	}

	[Fact]
	public void Moving_over_the_hidden_sidebar_area_does_not_bring_it_back()
	{
		// only the edge does, otherwise it would pop up over every maximized window the pointer crosses
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.OffScreen, p.Decide(SidebarMode.OffScreen, At(200)));
	}

	[Fact]
	public void It_stays_while_the_pointer_is_on_it()
	{
		var p = new SidebarPresence(Left);
		p.Decide(SidebarMode.OffScreen, At(0, now: 0));

		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.Flyover, At(250, now: 5)));
	}

	[Fact]
	public void It_hides_only_after_the_pointer_has_been_away_for_a_moment()
	{
		var p = new SidebarPresence(Left);
		p.Decide(SidebarMode.OffScreen, At(0, now: 0));
		p.Decide(SidebarMode.Flyover, At(250, now: 1));

		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.Flyover, At(400, now: 1.1)));
		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.Flyover, At(400, now: 1 + SidebarPresence.HideDelay - 0.01)));
		Assert.Equal(SidebarMode.OffScreen, p.Decide(SidebarMode.Flyover, At(400, now: 1 + SidebarPresence.HideDelay + 0.01)));
	}

	[Fact]
	public void Coming_back_in_time_keeps_it()
	{
		var p = new SidebarPresence(Left);
		p.Decide(SidebarMode.Flyover, At(250, now: 1));
		p.Decide(SidebarMode.Flyover, At(400, now: 1.2));

		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.Flyover, At(250, now: 1.3)));
		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.Flyover, At(400, now: 1.5)));
	}

	[Fact]
	public void A_hold_keeps_it_while_the_pointer_is_away()
	{
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.Flyover, At(1200, hold: true, now: 1)));
		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.Flyover, At(1200, hold: true, now: 9)));
	}

	[Fact]
	public void A_hold_does_not_bring_a_hidden_sidebar_back()
	{
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.OffScreen, p.Decide(SidebarMode.OffScreen, At(1200, hold: true)));
	}

	[Fact]
	public void When_the_covering_window_goes_a_flying_sidebar_settles_on_screen()
	{
		var p = new SidebarPresence(Left);

		Assert.Equal(SidebarMode.OnScreen, p.Decide(SidebarMode.Flyover, At(900, overlapped: false)));
		Assert.Equal(SidebarMode.Flyover, p.Decide(SidebarMode.Flyover, At(100, overlapped: false)));
	}
}
