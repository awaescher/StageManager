using StageManager.Core;

namespace StageManager.Tests;

public class CardDragTrackerTests
{
	private static CardDragTracker Pressed(double x = 100, double y = 300)
	{
		var t = new CardDragTracker();
		t.Press(x, y);
		return t;
	}

	[Fact]
	public void A_press_without_movement_is_a_click()
	{
		var t = Pressed();

		Assert.Equal(DragEnd.Click, t.Release());
	}

	[Fact]
	public void A_little_wobble_is_still_a_click()
	{
		var t = Pressed();

		t.Move(104, 303, outside: -100);
		t.Move(98, 297, outside: -100);

		Assert.False(t.IsDragging);
		Assert.Equal(DragEnd.Click, t.Release());
	}

	[Fact]
	public void A_click_that_wobbles_out_of_the_sidebar_is_still_a_click()
	{
		// the pointer is 7 pixels away from where it went down and beyond the sidebar: that is no drag
		var t = Pressed(x: 200);

		t.Move(207, 300, outside: 60);

		Assert.False(t.IsDragging);
		Assert.Equal(DragEnd.Click, t.Release());
	}

	[Fact]
	public void The_drag_starts_after_the_minimum_distance()
	{
		var t = Pressed();

		Assert.False(t.Move(105, 300, -90).IsDragging);
		Assert.True(t.Move(109, 300, -90).IsDragging);
	}

	[Fact]
	public void The_distance_counts_in_every_direction()
	{
		var t = Pressed();

		Assert.True(t.Move(100, 310, -90).IsDragging);
	}

	[Fact]
	public void Letting_go_over_the_sidebar_cancels()
	{
		var t = Pressed();
		t.Move(150, 320, outside: -40);

		Assert.Equal(DragEnd.Cancel, t.Release());
	}

	[Fact]
	public void Letting_go_on_the_stage_drops()
	{
		var t = Pressed();
		t.Move(150, 320, -40);
		t.Move(400, 320, outside: 120);

		Assert.Equal(DragEnd.Drop, t.Release());
	}

	[Fact]
	public void Letting_go_just_beyond_the_edge_does_not_drop()
	{
		var t = Pressed();
		t.Move(150, 320, -40);
		t.Move(300, 320, outside: CardDragTracker.ArmDistance - 1);

		Assert.False(t.State.IsArmed);
		Assert.Equal(DragEnd.Cancel, t.Release());
	}

	[Fact]
	public void The_card_is_armed_at_the_arm_distance()
	{
		var t = Pressed();

		var state = t.Move(300, 320, outside: CardDragTracker.ArmDistance);

		Assert.True(state.IsArmed);
	}

	[Fact]
	public void An_armed_card_stays_armed_while_the_pointer_wobbles_at_the_edge()
	{
		var t = Pressed();
		t.Move(300, 320, outside: 60);

		// between the two thresholds: does not flicker
		Assert.True(t.Move(280, 320, outside: 30).IsArmed);
		Assert.True(t.Move(270, 320, outside: 20).IsArmed);
	}

	[Fact]
	public void An_armed_card_is_disarmed_when_the_pointer_is_back_near_the_sidebar()
	{
		var t = Pressed();
		t.Move(300, 320, outside: 60);

		Assert.False(t.Move(230, 320, outside: CardDragTracker.DisarmDistance - 1).IsArmed);
		Assert.Equal(DragEnd.Cancel, t.Release());
	}

	[Fact]
	public void A_card_that_is_not_armed_does_not_arm_between_the_thresholds()
	{
		var t = Pressed();
		t.Move(150, 320, -40);

		Assert.False(t.Move(250, 320, outside: 30).IsArmed);
	}

	[Fact]
	public void A_card_can_be_armed_again_after_it_was_disarmed()
	{
		var t = Pressed();
		t.Move(300, 320, 60);
		t.Move(200, 320, 5);

		Assert.True(t.Move(310, 320, 70).IsArmed);
		Assert.Equal(DragEnd.Drop, t.Release());
	}

	[Fact]
	public void Cancel_ends_the_drag_without_a_result()
	{
		var t = Pressed();
		t.Move(300, 320, 60);

		t.Cancel();

		Assert.False(t.IsPressed);
		Assert.False(t.IsDragging);
		Assert.False(t.State.IsArmed);
	}

	[Fact]
	public void Moving_without_a_press_does_nothing()
	{
		var t = new CardDragTracker();

		var state = t.Move(500, 500, 100);

		Assert.False(state.IsDragging);
		Assert.False(state.IsArmed);
	}

	[Fact]
	public void A_second_press_starts_clean()
	{
		var t = Pressed();
		t.Move(300, 320, 60);
		t.Release();

		t.Press(100, 300);

		Assert.False(t.IsDragging);
		Assert.False(t.State.IsArmed);
		Assert.Equal(DragEnd.Click, t.Release());
	}

	[Fact]
	public void Releasing_twice_gives_a_click_the_second_time()
	{
		var t = Pressed();
		t.Move(300, 320, 60);

		Assert.Equal(DragEnd.Drop, t.Release());
		Assert.Equal(DragEnd.Click, t.Release());
	}

	[Fact]
	public void Another_monitor_never_arms()
	{
		var t = Pressed();

		var state = t.Move(2500, 320, outside: double.NegativeInfinity);

		Assert.True(state.IsDragging);
		Assert.False(state.IsArmed);
		Assert.Equal(0, state.Progress);
	}

	[Theory]
	[InlineData(-50, 0)]
	[InlineData(0, 0)]
	[InlineData(110, 0.5)]
	[InlineData(220, 1)]
	[InlineData(900, 1)]
	public void Progress_grows_with_the_distance_to_the_sidebar(double outside, double expected)
	{
		var t = Pressed();

		Assert.Equal(expected, t.Move(300, 320, outside).Progress, 6);
	}
}

public class DragPreviewLayoutTests
{
	[Fact]
	public void The_card_does_not_jump_when_the_drag_begins()
	{
		// grabbed 30 pixels right of the center of a 160 x 100 card
		var rect = DragPreviewLayout.CardRect(pointerX: 130, pointerY: 300, grabOffsetX: 30, grabOffsetY: 10, cardWidth: 160, cardHeight: 100, progress: 0);

		Assert.Equal(100, rect.Left + rect.Width / 2, 6);
		Assert.Equal(290, rect.Top + rect.Height / 2, 6);
		Assert.Equal(160, rect.Width, 6);
	}

	[Fact]
	public void Far_out_the_card_is_centered_on_the_pointer_and_larger()
	{
		var rect = DragPreviewLayout.CardRect(pointerX: 800, pointerY: 400, grabOffsetX: 30, grabOffsetY: 10, cardWidth: 160, cardHeight: 100, progress: 1);

		Assert.Equal(800, rect.Left + rect.Width / 2, 6);
		Assert.Equal(400, rect.Top + rect.Height / 2, 6);
		Assert.Equal(160 * DragPreviewLayout.MaxScale, rect.Width, 6);
		Assert.Equal(100 * DragPreviewLayout.MaxScale, rect.Height, 6);
	}

	[Fact]
	public void The_card_grows_steadily()
	{
		var previous = DragPreviewLayout.Scale(0);
		Assert.Equal(1, previous, 6);

		for (var p = 0.05; p <= 1.0001; p += 0.05)
		{
			var scale = DragPreviewLayout.Scale(p);
			Assert.True(scale >= previous - 1e-9);
			previous = scale;
		}

		Assert.Equal(DragPreviewLayout.MaxScale, previous, 6);
	}

	[Fact]
	public void The_slots_follow_the_card_and_keep_the_stack_inside_it()
	{
		var card = new Area(100, 200, 360, 360);

		var slots = DragPreviewLayout.Slots(card, windowCount: 3, maxVisible: 3);

		Assert.Equal(3, slots.Count);
		Assert.All(slots, s =>
		{
			Assert.InRange(s.X, card.Left - 0.001, card.Right + 0.001);
			Assert.InRange(s.X + s.Width, card.Left - 0.001, card.Right + 0.001);
			Assert.InRange(s.Y, card.Top - 0.001, card.Bottom + 0.001);
			Assert.InRange(s.Y + s.Height, card.Top - 0.001, card.Bottom + 0.001);
		});
	}

	[Fact]
	public void The_front_window_sits_in_the_bottom_left_corner()
	{
		var card = new Area(100, 200, 360, 360);

		var slots = DragPreviewLayout.Slots(card, 3, 3);

		Assert.Equal(card.Left, slots[2].X, 6);
		Assert.Equal(card.Bottom, slots[2].Y + slots[2].Height, 6);
		Assert.True(slots[0].Y < slots[2].Y);
	}

	[Fact]
	public void Windows_beyond_the_visible_ones_are_invisible()
	{
		var slots = DragPreviewLayout.Slots(new Area(0, 0, 200, 120), windowCount: 5, maxVisible: 2);

		Assert.Equal(new[] { 0.0, 0.0, 0.0, 1.0, 1.0 }, slots.Select(s => s.Opacity));
	}

	[Fact]
	public void A_single_window_fills_the_card()
	{
		var card = new Area(10, 20, 210, 140);

		var slot = Assert.Single(DragPreviewLayout.Slots(card, 1, 3));

		Assert.Equal(new Pose(10, 20, 200, 120, 1), slot);
	}

	[Fact]
	public void The_steps_of_the_stack_grow_with_the_scale_of_the_display()
	{
		// a card of 200 x 120 layout units on a display at 150 percent
		var card = new Area(0, 0, 300, 180);

		var slots = DragPreviewLayout.Slots(card, 2, 2, scale: 1.5);

		// the back window is one step (8 units = 12 pixels) up and to the right
		Assert.Equal(12, slots[0].X, 6);
		Assert.Equal(0, slots[0].Y, 6);
		Assert.Equal((200 - 8) * 1.5, slots[0].Width, 6);
		Assert.Equal(0, slots[1].X, 6);
		Assert.Equal(180, slots[1].Y + slots[1].Height, 6);
	}

	[Fact]
	public void On_the_stack_each_window_keeps_its_proportions_inside_its_slot()
	{
		var card = new Area(100, 100, 300, 220);

		var poses = DragPreviewLayout.OnStack(card, new[] { (1000.0, 1000.0), (1600.0, 900.0) }, maxVisible: 3);
		var slots = DragPreviewLayout.Slots(card, 2, 3);

		Assert.Equal(2, poses.Length);
		for (var i = 0; i < 2; i++)
		{
			Assert.InRange(poses[i].X, slots[i].X - 0.001, slots[i].X + slots[i].Width);
			Assert.InRange(poses[i].Y + poses[i].Height, slots[i].Y, slots[i].Y + slots[i].Height + 0.001);
		}

		Assert.Equal(1.0, poses[0].Width / poses[0].Height, 6);
		Assert.Equal(16.0 / 9, poses[1].Width / poses[1].Height, 6);
	}

	[Fact]
	public void On_the_stack_hidden_windows_are_invisible()
	{
		var poses = DragPreviewLayout.OnStack(new Area(0, 0, 200, 120), new[] { (800.0, 600.0), (800.0, 600.0), (800.0, 600.0) }, maxVisible: 1);

		Assert.Equal(new[] { 0.0, 0.0, 1.0 }, poses.Select(p => p.Opacity));
	}

	[Theory]
	[InlineData(0, false, 0)]
	[InlineData(1, false, 0.35)]
	[InlineData(1, true, 1)]
	[InlineData(0, true, 1)]
	[InlineData(5, false, 0.35)]
	public void The_hint_is_faint_until_the_card_can_be_dropped(double progress, bool armed, double expected)
	{
		Assert.Equal(expected, DragPreviewLayout.HintOpacity(progress, armed), 6);
	}
}

public class SidebarDistanceTests
{
	private static readonly Area Primary = new(0, 0, 1920, 1080);

	[Theory]
	[InlineData(SidebarSide.Left, 280, 0)]
	[InlineData(SidebarSide.Left, 380, 100)]
	[InlineData(SidebarSide.Left, 100, -180)]
	[InlineData(SidebarSide.Right, 1640, 0)]
	[InlineData(SidebarSide.Right, 1540, 100)]
	[InlineData(SidebarSide.Right, 1800, -160)]
	public void The_distance_is_measured_from_the_inner_edge(SidebarSide side, double mouseX, double expected)
	{
		var g = new SidebarGeometry(Primary, 280, side);

		Assert.Equal(expected, g.DistanceOutside(mouseX, 500), 6);
	}

	[Theory]
	[InlineData(-5, 500)]
	[InlineData(1925, 500)]
	[InlineData(500, -5)]
	[InlineData(500, 1085)]
	public void A_pointer_on_another_monitor_has_no_distance(double x, double y)
	{
		var g = new SidebarGeometry(Primary, 280, SidebarSide.Left);

		Assert.Equal(double.NegativeInfinity, g.DistanceOutside(x, y));
	}

	[Fact]
	public void The_stage_is_the_part_next_to_the_sidebar()
	{
		Assert.Equal(new Area(280, 0, 1920, 1080), new SidebarGeometry(Primary, 280, SidebarSide.Left).StageArea);
		Assert.Equal(new Area(0, 0, 1640, 1080), new SidebarGeometry(Primary, 280, SidebarSide.Right).StageArea);
	}

	[Fact]
	public void The_drag_preview_follows_windows_for_frames()
	{
		var frames = AnimationPolicy.DecideDragPreview(new AnimationSettings { FrameIfWindowContentIsOff = true }, new SystemAnimationState(false, DragFullWindows: false));
		var windows = AnimationPolicy.DecideDragPreview(new AnimationSettings { FrameIfWindowContentIsOff = true }, new SystemAnimationState(false, DragFullWindows: true));
		var off = AnimationPolicy.DecideDragPreview(new AnimationSettings { Mode = AnimationMode.Off, FrameIfWindowContentIsOff = false }, new SystemAnimationState(false, DragFullWindows: false));

		Assert.Equal(FlightStyle.Frames, frames);
		Assert.Equal(FlightStyle.Windows, windows);
		Assert.Equal(FlightStyle.Windows, off);
	}

	[Fact]
	public void Dragging_to_the_stage_is_on_by_default_and_survives_a_round_trip()
	{
		var s = new AppSettings().Normalize();
		Assert.True(s.Sidebar.DragToStage);

		s.Sidebar.DragToStage = false;
		Assert.False(AppSettings.Deserialize(s.Serialize())!.Sidebar.DragToStage);
	}
}
