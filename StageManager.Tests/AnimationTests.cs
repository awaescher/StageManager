using StageManager.Core;

namespace StageManager.Tests;

public class AnimationPolicyTests
{
	private static AnimationSettings Settings(AnimationMode mode, bool frameIfOff = true) =>
		new() { Mode = mode, FrameIfWindowContentIsOff = frameIfOff };

	[Theory]
	[InlineData(AnimationMode.System, true, true, FlightStyle.Windows)]
	[InlineData(AnimationMode.System, false, true, FlightStyle.None)]
	[InlineData(AnimationMode.On, false, true, FlightStyle.Windows)]
	[InlineData(AnimationMode.On, true, true, FlightStyle.Windows)]
	[InlineData(AnimationMode.Off, true, true, FlightStyle.None)]
	[InlineData(AnimationMode.Off, false, false, FlightStyle.None)]
	public void Mode_and_system_decide_if_animated(AnimationMode mode, bool systemAnimations, bool dragFull, FlightStyle expected)
	{
		Assert.Equal(expected, AnimationPolicy.Decide(Settings(mode), new SystemAnimationState(systemAnimations, dragFull)));
	}

	[Fact]
	public void Frames_fly_if_windows_shows_a_frame_while_dragging()
	{
		var style = AnimationPolicy.Decide(Settings(AnimationMode.System), new SystemAnimationState(true, DragFullWindows: false));

		Assert.Equal(FlightStyle.Frames, style);
	}

	[Fact]
	public void Frames_also_fly_if_animations_are_forced_on()
	{
		var style = AnimationPolicy.Decide(Settings(AnimationMode.On), new SystemAnimationState(false, DragFullWindows: false));

		Assert.Equal(FlightStyle.Frames, style);
	}

	[Fact]
	public void The_frame_fallback_can_be_switched_off()
	{
		var style = AnimationPolicy.Decide(Settings(AnimationMode.System, frameIfOff: false), new SystemAnimationState(true, DragFullWindows: false));

		Assert.Equal(FlightStyle.Windows, style);
	}

	[Fact]
	public void Off_never_animates_not_even_frames()
	{
		var style = AnimationPolicy.Decide(Settings(AnimationMode.Off), new SystemAnimationState(true, DragFullWindows: false));

		Assert.Equal(FlightStyle.None, style);
	}

	[Fact]
	public void Defaults_follow_the_system()
	{
		Assert.Equal(AnimationMode.System, new AppSettings().Normalize().Animations.Mode);
		Assert.Equal(1.0, new AppSettings().Normalize().Animations.Speed);
	}
}

public class FlightMotionTests
{
	[Fact]
	public void The_easing_starts_at_zero_and_ends_on_exactly_one()
	{
		Assert.Equal(0, FlightMotion.Ease(0), 10);
		Assert.Equal(1, FlightMotion.Ease(1), 10);
	}

	[Fact]
	public void The_easing_never_overshoots_and_only_goes_forward()
	{
		var previous = 0.0;
		for (var p = 0.0; p <= 1.0; p += 0.01)
		{
			var e = FlightMotion.Ease(p);
			Assert.InRange(e, 0, 1);
			Assert.True(e >= previous - 1e-9, $"went backwards at {p}");
			previous = e;
		}
	}

	[Fact]
	public void The_easing_is_brisk_at_the_start_and_covers_most_of_the_way_early()
	{
		Assert.True(FlightMotion.Ease(0.25) > 0.6);
		Assert.True(FlightMotion.Ease(0.5) > 0.9);
	}

	[Theory]
	[InlineData(-1, 0)]
	[InlineData(2, 1)]
	[InlineData(double.NaN, 0)]
	public void The_easing_clamps_its_input(double input, double expected)
	{
		Assert.Equal(expected, FlightMotion.Ease(input), 10);
	}

	[Theory]
	[InlineData(1.0, 520)]
	[InlineData(2.0, 260)]
	[InlineData(0.5, 1040)]
	public void Speed_shortens_the_duration(double speed, double expectedMs)
	{
		Assert.Equal(expectedMs, FlightMotion.Duration(speed).TotalMilliseconds, 3);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-3)]
	[InlineData(double.NaN)]
	[InlineData(1000)]
	public void Speed_out_of_range_stays_usable(double speed)
	{
		var ms = FlightMotion.Duration(speed).TotalMilliseconds;

		Assert.InRange(ms, 520 / AnimationSettings.MaxSpeed - 0.001, 520 / AnimationSettings.MinSpeed + 0.001);
	}

	[Theory]
	[InlineData(0.0, 0.25)]
	[InlineData(0.1, 0.25)]
	[InlineData(1.5, 1.5)]
	[InlineData(9, 3.0)]
	[InlineData(double.NaN, 1.0)]
	public void Speed_is_clamped_in_the_settings(double value, double expected)
	{
		var s = new AppSettings { Animations = { Speed = value } }.Normalize();

		Assert.Equal(expected, s.Animations.Speed);
	}

	[Fact]
	public void An_unknown_mode_falls_back_to_system()
	{
		var s = new AppSettings { Animations = { Mode = (AnimationMode)99 } }.Normalize();

		Assert.Equal(AnimationMode.System, s.Animations.Mode);
	}
}

public class FlightTests
{
	private static readonly Pose Card = new(10, 100, 160, 100);
	private static readonly Pose Stage = new(400, 50, 800, 500);
	private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(500);

	[Fact]
	public void A_flight_starts_at_its_start_and_ends_at_its_target()
	{
		var f = new Flight(1, Card, Stage, Duration);

		Assert.Equal(Card, f.At(0));
		Assert.Equal(Stage.X, f.At(0.5).X, 6);
		Assert.Equal(Stage, f.At(10));
		Assert.True(f.IsDone(0.5));
		Assert.False(f.IsDone(0.1));
	}

	[Fact]
	public void The_middle_of_a_flight_lies_between_start_and_target()
	{
		var f = new Flight(1, Card, Stage, Duration);

		var mid = f.At(0.15);

		Assert.InRange(mid.X, Card.X, Stage.X);
		Assert.InRange(mid.Width, Card.Width, Stage.Width);
		Assert.NotEqual(Card.X, mid.X);
	}

	[Fact]
	public void Opacity_is_interpolated()
	{
		var f = new Flight(1, Card with { Opacity = 0 }, Stage with { Opacity = 1 }, Duration);

		Assert.Equal(0, f.At(0).Opacity);
		Assert.Equal(1, f.At(5).Opacity);
		Assert.InRange(f.At(0.1).Opacity, 0.01, 0.99);
	}

	[Fact]
	public void A_redirected_flight_continues_from_where_it_is()
	{
		var f = new Flight(1, Card, Stage, Duration, now: 0);
		var inTheAir = f.At(0.2);

		f.Redirect(Card, Duration, now: 0.2);

		var right = f.At(0.2);
		Assert.Equal(inTheAir.X, right.X, 6);
		Assert.Equal(inTheAir.Width, right.Width, 6);
		Assert.Equal(Card, f.At(5));
	}

	[Fact]
	public void A_redirected_flight_starts_its_time_over()
	{
		var f = new Flight(1, Card, Stage, Duration, now: 0);

		f.Redirect(Card, Duration, now: 0.3);

		Assert.Equal(0, f.Progress(0.3));
		Assert.False(f.IsDone(0.6));
		Assert.True(f.IsDone(0.8));
	}

	[Fact]
	public void A_flight_with_no_duration_is_done_at_once()
	{
		var f = new Flight(1, Card, Stage, TimeSpan.Zero);

		Assert.True(f.IsDone(0));
		Assert.Equal(Stage, f.At(0));
	}

	[Fact]
	public void The_clock_before_the_start_does_not_move_the_window()
	{
		var f = new Flight(1, Card, Stage, Duration, now: 5);

		Assert.Equal(Card, f.At(0));
	}
}

public class FlightPlannerTests
{
	[Fact]
	public void A_window_on_a_card_keeps_its_proportions_and_is_centered()
	{
		var card = new Area(10, 100, 170, 200); // 160 x 100

		var pose = FlightPlanner.OnCard(card, 400, 400, 0.5);

		Assert.Equal(100, pose.Width, 6);
		Assert.Equal(100, pose.Height, 6);
		Assert.Equal(40, pose.X, 6);
		Assert.Equal(100, pose.Y, 6);
		Assert.Equal(0.5, pose.Opacity);
	}

	[Fact]
	public void A_window_with_the_proportions_of_the_card_fills_it()
	{
		var card = new Area(0, 0, 160, 100);

		var pose = FlightPlanner.OnCard(card, 1600, 1000, 1);

		Assert.Equal(new Pose(0, 0, 160, 100, 1), pose);
	}

	[Fact]
	public void Insets_are_the_difference_between_window_rectangle_and_visible_frame()
	{
		var windowRect = new Area(92, 100, 1000, 700);
		var frame = new Area(100, 100, 992, 693);

		var insets = FlightPlanner.InsetsOf(windowRect, frame);

		Assert.Equal(new Insets(8, 0, 8, 7), insets);
	}

	[Fact]
	public void The_frame_of_a_window_rectangle_follows_from_the_insets()
	{
		var windowRect = new Area(92, 100, 1000, 700);

		var frame = FlightPlanner.FrameOf(windowRect, new Insets(8, 0, 8, 7));

		Assert.Equal(new Area(100, 100, 992, 693), frame);
	}

	[Fact]
	public void Insets_and_frame_round_trip()
	{
		var windowRect = new Area(50, 60, 850, 560);
		var frame = new Area(57, 60, 843, 553);

		Assert.Equal(frame, FlightPlanner.FrameOf(windowRect, FlightPlanner.InsetsOf(windowRect, frame)));
	}

	[Fact]
	public void Insets_that_swallow_the_window_are_ignored()
	{
		var windowRect = new Area(0, 0, 10, 10);

		Assert.Equal(windowRect, FlightPlanner.FrameOf(windowRect, new Insets(7, 0, 7, 7)));
	}

	[Fact]
	public void Default_insets_match_the_usual_windows_borders()
	{
		Assert.Equal(new Area(7, 0, 93, 93), FlightPlanner.FrameOf(new Area(0, 0, 100, 100), Insets.Default));
	}

	[Theory]
	[InlineData(0, 0, 100, 100, 50, 50, 150, 150, true)]
	[InlineData(0, 0, 100, 100, 100, 0, 200, 100, false)]
	[InlineData(0, 0, 100, 100, 200, 200, 300, 300, false)]
	[InlineData(-500, 0, -100, 100, 0, 0, 100, 100, false)]
	[InlineData(0, 0, 1920, 1080, 500, 500, 600, 600, true)]
	public void Windows_on_another_monitor_do_not_intersect(double al, double at, double ar, double ab, double bl, double bt, double br, double bb, bool expected)
	{
		Assert.Equal(expected, FlightPlanner.Intersects(new Area(al, at, ar, ab), new Area(bl, bt, br, bb)));
	}
}