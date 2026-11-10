using StageManager.Core;

namespace StageManager.Tests;

public class OffScreenRescueTests
{
	private static readonly Area Monitor = new(0, 0, 1920, 1080);
	private static readonly Area WorkArea = new(0, 0, 1920, 1040);

	private static Area? Target(Area window, params (Area Monitor, Area WorkArea)[] screens) =>
		OffScreenRescue.Target(window, screens.Select(s => s.Monitor).ToList(), screens.Select(s => s.WorkArea).ToList());

	[Fact]
	public void A_window_parked_beyond_the_left_edge_comes_back_to_the_left_edge_at_its_height()
	{
		var target = Target(new Area(-960, 200, -160, 700), (Monitor, WorkArea));

		Assert.Equal(new Area(0, 200, 800, 700), target);
	}

	[Fact]
	public void A_window_below_the_screen_comes_back_into_the_work_area_above_the_taskbar()
	{
		var target = Target(new Area(300, 1300, 1100, 1800), (Monitor, WorkArea));

		Assert.Equal(new Area(300, 540, 1100, 1040), target);
	}

	[Fact]
	public void A_window_on_the_screen_stays_where_it_is()
	{
		Assert.Null(Target(new Area(300, 200, 1100, 700), (Monitor, WorkArea)));
	}

	[Fact]
	public void A_window_that_only_shows_a_thin_strip_is_lost()
	{
		Assert.True(OffScreenRescue.IsLost(new Area(-795, 200, 5, 700), new[] { Monitor }));
	}

	[Fact]
	public void A_window_that_reaches_far_enough_onto_the_screen_is_not_lost()
	{
		Assert.False(OffScreenRescue.IsLost(new Area(-700, 200, 100, 700), new[] { Monitor }));
	}

	[Fact]
	public void A_window_larger_than_the_work_area_starts_at_its_top_left_corner()
	{
		var target = Target(new Area(-3000, 100, -500, 1600), (Monitor, WorkArea));

		Assert.Equal(new Area(0, 0, 2500, 1500), target);
	}

	[Fact]
	public void The_window_goes_to_the_closest_monitor()
	{
		var right = new Area(1920, 0, 3840, 1080);
		var target = Target(new Area(4000, 200, 4800, 700), (Monitor, WorkArea), (right, right));

		Assert.Equal(new Area(3040, 200, 3840, 700), target);
	}

	[Fact]
	public void Without_monitors_nothing_is_moved()
	{
		Assert.Null(OffScreenRescue.Target(new Area(-960, 200, -160, 700), Array.Empty<Area>(), Array.Empty<Area>()));
	}
}
