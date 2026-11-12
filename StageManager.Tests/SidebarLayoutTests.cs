using StageManager.Core;

namespace StageManager.Tests;

public class SidebarLayoutTests
{
	private static SidebarLayoutResult Calc(double height, int scenes, double aspect = 1.6, double sidebarWidth = 0, double fixedW = 0, double fixedH = 0) =>
		SidebarLayout.Calculate(new SidebarLayoutInput(height, scenes, aspect, sidebarWidth, fixedW, fixedH));

	[Fact]
	public void Thumbnails_get_smaller_with_more_scenes()
	{
		var few = Calc(1000, 3);
		var many = Calc(1000, 8);

		Assert.True(many.ThumbnailHeight < few.ThumbnailHeight);
		Assert.True(many.ThumbnailWidth < few.ThumbnailWidth);
	}

	[Theory]
	[InlineData(1000, 1)]
	[InlineData(1000, 3)]
	[InlineData(1000, 6)]
	[InlineData(1000, 12)]
	[InlineData(700, 5)]
	[InlineData(2000, 12)]
	public void Wanted_scenes_fit_into_the_height(double height, int scenes)
	{
		var r = Calc(height, scenes);
		var used = r.EffectiveMaxScenes * (r.ThumbnailHeight + SidebarLayout.IconRowHeight + r.CardGap) + 2 * SidebarLayout.VerticalMargin;

		Assert.True(used <= height + 0.001, $"{used} > {height}");
	}

	[Fact]
	public void Calculated_thumbnails_keep_the_aspect_ratio()
	{
		var r = Calc(1000, 6, aspect: 1.6);

		Assert.Equal(1.6, r.ThumbnailWidth / r.ThumbnailHeight, 3);
	}

	[Fact]
	public void A_single_scene_does_not_get_a_huge_thumbnail()
	{
		var r = Calc(2000, 1);

		Assert.True(r.ThumbnailWidth <= SidebarLayout.MaxThumbnailWidth);
		Assert.True(r.ThumbnailHeight <= SidebarLayout.MaxThumbnailHeight);
		Assert.Equal(1, r.EffectiveMaxScenes);
		Assert.False(r.IsLimited);
	}

	[Fact]
	public void Too_little_height_limits_the_number_of_scenes()
	{
		var r = Calc(500, 12);

		Assert.True(r.IsLimited);
		Assert.True(r.EffectiveMaxScenes < 12);
		Assert.True(r.EffectiveMaxScenes >= 1);
		Assert.True(r.ThumbnailHeight >= SidebarLayout.MinThumbnailHeight);
	}

	[Fact]
	public void Tiny_area_still_shows_one_scene()
	{
		var r = Calc(10, 6);

		Assert.Equal(1, r.EffectiveMaxScenes);
		Assert.True(r.ThumbnailWidth > 0 && r.ThumbnailHeight > 0);
	}

	[Fact]
	public void Auto_sidebar_width_follows_the_thumbnail_within_limits()
	{
		var r = Calc(1000, 6);

		Assert.Equal(r.ThumbnailWidth + SidebarLayout.EdgePadding + SidebarLayout.ShadowRoom, r.SidebarWidth, 3);
		Assert.InRange(r.SidebarWidth, SidebarSettings.MinWidth, SidebarSettings.MaxWidth);
	}

	[Fact]
	public void Fixed_sidebar_width_shrinks_the_thumbnail_but_keeps_its_proportions()
	{
		var wide = Calc(1000, 3);
		var narrow = Calc(1000, 3, sidebarWidth: 200);

		Assert.Equal(200, narrow.SidebarWidth);
		Assert.True(narrow.ThumbnailWidth <= 200 - SidebarLayout.EdgePadding - SidebarLayout.ShadowRoom + 0.001);
		Assert.Equal(wide.ThumbnailWidth / wide.ThumbnailHeight, narrow.ThumbnailWidth / narrow.ThumbnailHeight, 3);
	}

	[Fact]
	public void Fixed_sidebar_width_never_enlarges_the_thumbnail()
	{
		var auto = Calc(1000, 6);
		var wideBar = Calc(1000, 6, sidebarWidth: 480);

		Assert.Equal(auto.ThumbnailWidth, wideBar.ThumbnailWidth, 3);
		Assert.Equal(480, wideBar.SidebarWidth);
	}

	[Fact]
	public void Fixed_thumbnail_size_is_used_and_limits_the_scene_count()
	{
		var r = Calc(1000, 12, fixedW: 200, fixedH: 150);

		Assert.Equal(200, r.ThumbnailWidth);
		Assert.Equal(150, r.ThumbnailHeight);
		Assert.True(r.IsLimited);
		Assert.Equal(4, r.EffectiveMaxScenes); // 952 / (150 + 65) = 4.43
	}

	[Fact]
	public void Half_a_fixed_size_is_ignored()
	{
		var r = Calc(1000, 6, fixedW: 200, fixedH: 0);

		Assert.Equal(1.6, r.ThumbnailWidth / r.ThumbnailHeight, 3);
	}

	[Fact]
	public void Out_of_range_input_does_not_break_the_calculation()
	{
		var r = Calc(1000, 500, aspect: double.NaN);

		Assert.True(double.IsFinite(r.ThumbnailWidth) && r.ThumbnailWidth > 0);
		Assert.InRange(r.EffectiveMaxScenes, 1, SidebarSettings.MaxScenesLimit);
	}
}

public class ThumbnailStackTests
{
	[Fact]
	public void Empty_scene_has_no_slots()
	{
		Assert.Empty(ThumbnailStack.Calculate(200, 120, 0, 3));
	}

	[Fact]
	public void Single_window_fills_the_card()
	{
		var slot = Assert.Single(ThumbnailStack.Calculate(200, 120, 1, 3));

		Assert.Equal(new StackSlot(200, 120, 0, 0, true), slot);
	}

	[Fact]
	public void Front_window_sits_in_the_corner_and_the_others_move_up_and_right()
	{
		var slots = ThumbnailStack.Calculate(200, 120, 3, 3);

		Assert.Equal(0, slots[2].OffsetX);
		Assert.Equal(ThumbnailStack.Step, slots[1].OffsetX);
		Assert.Equal(2 * ThumbnailStack.Step, slots[0].OffsetY);
	}

	[Fact]
	public void The_whole_stack_fits_into_the_card()
	{
		foreach (var slot in ThumbnailStack.Calculate(200, 120, 4, 4))
		{
			Assert.True(slot.OffsetX + slot.Width <= 200 + 0.001);
			Assert.True(slot.OffsetY + slot.Height <= 120 + 0.001);
		}
	}

	[Fact]
	public void Only_the_frontmost_windows_are_visible()
	{
		var slots = ThumbnailStack.Calculate(200, 120, 5, 2);

		Assert.Equal(new[] { false, false, false, true, true }, slots.Select(s => s.IsVisible));
		Assert.Equal(0, slots[4].OffsetX);
		Assert.Equal(ThumbnailStack.Step, slots[3].OffsetX);
		Assert.Equal(200 - ThumbnailStack.Step, slots[4].Width);
	}

	[Fact]
	public void Max_visible_is_limited_to_the_window_count_and_at_least_one()
	{
		Assert.All(ThumbnailStack.Calculate(200, 120, 2, 6), s => Assert.True(s.IsVisible));
		Assert.Single(ThumbnailStack.Calculate(200, 120, 3, 0), s => s.IsVisible);
	}
}

public class AspectFitTests
{
	[Fact]
	public void Wide_source_is_letterboxed_vertically()
	{
		var r = AspectFit.Calculate(200, 200, 400, 200);

		Assert.Equal(new FittedRect(0, 50, 200, 100), r);
	}

	[Fact]
	public void Tall_source_is_letterboxed_horizontally()
	{
		var r = AspectFit.Calculate(200, 100, 100, 200);

		Assert.Equal(new FittedRect(75, 0, 50, 100), r);
	}

	[Fact]
	public void Matching_aspect_fills_the_container()
	{
		Assert.Equal(new FittedRect(0, 0, 160, 100), AspectFit.Calculate(160, 100, 1920, 1200));
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(-1, 100)]
	public void Unknown_source_size_fills_the_container(double w, double h)
	{
		Assert.Equal(new FittedRect(0, 0, 160, 100), AspectFit.Calculate(160, 100, w, h));
	}

	[Fact]
	public void Empty_container_gives_an_empty_result()
	{
		Assert.Equal(new FittedRect(0, 0, 0, 0), AspectFit.Calculate(0, 100, 400, 200));
	}
}

public class CardAlignmentTests
{
	[Fact]
	public void A_wide_window_sits_on_the_bottom_edge_not_in_the_middle()
	{
		// 3:1 on a 160 x 100 card: 160 x 53.3, centered it would float 23 units above the icons
		var centered = AspectFit.Calculate(160, 100, 1200, 400);
		var bottom = AspectFit.Calculate(160, 100, 1200, 400, 0, 1);

		Assert.True(centered.Y > 20);
		Assert.Equal(100, bottom.Y + bottom.Height, 6);
		Assert.Equal(0, bottom.X, 6);
	}

	[Fact]
	public void A_tall_window_sits_in_the_corner_at_the_screen_edge()
	{
		var left = AspectFit.Calculate(160, 100, 400, 800, 0, 1);
		var right = AspectFit.Calculate(160, 100, 400, 800, 1, 1);

		Assert.Equal(0, left.X, 6);
		Assert.Equal(160, right.X + right.Width, 6);
		Assert.Equal(100, left.Y + left.Height, 6);
	}

	[Fact]
	public void The_corner_follows_the_side_of_the_sidebar()
	{
		Assert.Equal(new CardAlignment(0, 1), CardAlignment.For(SidebarSide.Left));
		Assert.Equal(new CardAlignment(1, 1), CardAlignment.For(SidebarSide.Right));
	}

	[Fact]
	public void The_default_stays_centered()
	{
		Assert.Equal(AspectFit.Calculate(160, 100, 400, 800), AspectFit.Calculate(160, 100, 400, 800, 0.5, 0.5));
	}

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(-3)]
	[InlineData(7)]
	public void An_odd_alignment_never_puts_the_window_outside_the_card(double align)
	{
		var r = AspectFit.Calculate(160, 100, 400, 800, align, align);

		Assert.InRange(r.X, 0, 160 - r.Width + 0.0001);
		Assert.InRange(r.Y, 0, 100 - r.Height + 0.0001);
	}

	[Fact]
	public void Flights_end_where_the_card_shows_the_window()
	{
		var card = new Area(10, 100, 170, 200);
		var align = CardAlignment.For(SidebarSide.Left);

		var pose = FlightPlanner.OnCard(card, 1200, 400, 1, align.X, align.Y);

		Assert.Equal(200, pose.Y + pose.Height, 6);
		Assert.Equal(10, pose.X, 6);
	}

	[Fact]
	public void A_stack_keeps_every_window_on_the_corner_of_its_slot()
	{
		var card = new Area(0, 0, 200, 120);
		var poses = DragPreviewLayout.OnStack(card, new[] { (1600.0, 500.0), (800.0, 800.0) }, 2, 1, CardAlignment.For(SidebarSide.Left));
		var slots = DragPreviewLayout.Slots(card, 2, 2);

		for (var i = 0; i < poses.Length; i++)
		{
			Assert.Equal(slots[i].Y + slots[i].Height, poses[i].Y + poses[i].Height, 6);
			Assert.Equal(slots[i].X, poses[i].X, 6);
		}
	}
}

public class CardGapTests
{
	private static SidebarLayoutResult Calc(double height, int scenes, double fixedW = 0, double fixedH = 0) =>
		SidebarLayout.Calculate(new SidebarLayoutInput(height, scenes, 1.6, 0, fixedW, fixedH));

	[Theory]
	[InlineData(600, 6)]
	[InlineData(800, 8)]
	[InlineData(1000, 12)]
	[InlineData(500, 12)]
	public void The_space_between_cards_is_never_less_than_the_minimum(double height, int scenes)
	{
		Assert.True(Calc(height, scenes).CardGap >= SidebarLayout.MinCardGap);
	}

	[Fact]
	public void Where_the_cards_cannot_grow_any_more_the_space_between_them_does()
	{
		// one scene on a tall screen: the card is at its largest, the room left goes into the space, up to the maximum
		var r = Calc(2000, 6);

		Assert.True(r.CardGap > SidebarLayout.MinCardGap);
		Assert.True(r.CardGap <= SidebarLayout.MaxCardGap);
	}

	[Fact]
	public void A_very_tall_screen_does_not_pull_the_cards_apart_forever()
	{
		var r = Calc(6000, 2);

		Assert.Equal(SidebarLayout.MaxCardGap, r.CardGap);
	}

	[Fact]
	public void When_the_cards_fill_the_screen_the_space_is_the_minimum()
	{
		// the cards take what there is, nothing is left over
		var r = Calc(1000, 6);

		Assert.InRange(r.CardGap, SidebarLayout.MinCardGap, SidebarLayout.MinCardGap + 1);
	}

	[Fact]
	public void A_fixed_thumbnail_size_leaves_room_which_becomes_space()
	{
		var r = Calc(1400, 3, fixedW: 192, fixedH: 120);

		Assert.True(r.CardGap > SidebarLayout.MinCardGap);
		Assert.Equal(3, r.EffectiveMaxScenes);
	}

	[Theory]
	[InlineData(700, 4)]
	[InlineData(1000, 6)]
	[InlineData(1400, 3)]
	[InlineData(2000, 12)]
	public void Cards_and_space_still_fit_into_the_height(double height, int scenes)
	{
		var r = Calc(height, scenes);
		var used = r.EffectiveMaxScenes * (r.ThumbnailHeight + SidebarLayout.IconRowHeight + r.CardGap) + 2 * SidebarLayout.VerticalMargin;

		Assert.True(used <= height + 0.001, $"{used} > {height}");
	}
}

public class CardShapeTests
{
	[Fact]
	public void A_flat_window_makes_a_low_card()
	{
		// 3:1 in a card of 160 x 100: the window is 53 high, the card is as high as the window
		var h = CardShape.Height(160, 100, new[] { (1200.0, 400.0) }, 3);

		Assert.Equal(160 / 3.0, h, 3);
	}

	[Fact]
	public void A_window_with_the_proportions_of_the_card_makes_a_full_card()
	{
		Assert.Equal(100, CardShape.Height(160, 100, new[] { (1600.0, 1000.0) }, 3), 6);
	}

	[Fact]
	public void A_tall_window_is_never_taller_than_the_card_may_be()
	{
		Assert.Equal(100, CardShape.Height(160, 100, new[] { (400.0, 1600.0) }, 3), 6);
	}

	[Fact]
	public void A_stack_is_as_tall_as_its_tallest_visible_window_plus_the_steps()
	{
		var flat = (1600.0, 400.0);
		var tall = (800.0, 800.0);

		var h = CardShape.Height(200, 120, new[] { tall, flat }, 2);

		// the steps of a stack of 2 take 8 from the slot: 192 x 112; the tall window is 112 high, plus the step
		Assert.Equal(120, h, 6);
	}

	[Fact]
	public void Windows_beyond_the_visible_stack_do_not_make_the_card_taller()
	{
		var h = CardShape.Height(160, 100, new[] { (400.0, 1600.0), (1200.0, 400.0) }, 1);

		Assert.Equal(160 / 3.0, h, 3);
	}

	[Fact]
	public void A_very_flat_window_still_leaves_a_card_you_can_hit()
	{
		var h = CardShape.Height(160, 100, new[] { (4000.0, 100.0) }, 3);

		Assert.Equal(CardShape.MinHeight, h);
	}

	[Fact]
	public void Windows_of_unknown_size_fill_the_card()
	{
		Assert.Equal(100, CardShape.Height(160, 100, new[] { (0.0, 0.0) }, 3), 6);
	}

	[Fact]
	public void A_card_without_windows_keeps_the_given_height()
	{
		Assert.Equal(100, CardShape.Height(160, 100, Array.Empty<(double, double)>(), 3));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	[InlineData(double.NaN)]
	public void A_card_without_a_usable_maximum_gives_it_back(double max)
	{
		var h = CardShape.Height(160, max, new[] { (1600.0, 1000.0) }, 3);

		Assert.True(double.IsNaN(max) ? double.IsNaN(h) : h == max);
	}

	[Fact]
	public void The_slots_of_a_low_card_end_at_its_bottom_edge()
	{
		var h = CardShape.Height(160, 100, new[] { (1200.0, 400.0) }, 3);
		var slot = Assert.Single(ThumbnailStack.Calculate(160, h, 1, 3));
		var fit = AspectFit.Calculate(slot.Width, slot.Height, 1200, 400, 0, 1);

		Assert.Equal(h, fit.Y + fit.Height, 6);
		Assert.Equal(0, fit.Y, 6);
	}
}
