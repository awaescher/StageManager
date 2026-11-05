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
		var used = r.EffectiveMaxScenes * (r.ThumbnailHeight + SidebarLayout.IconRowHeight + SidebarLayout.CardGap) + 2 * SidebarLayout.VerticalMargin;

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
		Assert.Equal(4, r.EffectiveMaxScenes); // 952 / (150 + 58) = 4.57
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
