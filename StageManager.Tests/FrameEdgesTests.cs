using StageManager.Core;

namespace StageManager.Tests;

public class FrameEdgesTests
{
	[Fact]
	public void The_edges_make_up_a_frame_without_overlap()
	{
		var edges = FrameEdges.Of(100, 50, 400, 300, 3);

		Assert.Equal((100, 50, 400, 3), edges[0]);
		Assert.Equal((100, 347, 400, 3), edges[1]);
		Assert.Equal((100, 53, 3, 294), edges[2]);
		Assert.Equal((497, 53, 3, 294), edges[3]);

		// the area of the four edges is the area of the frame minus the inside
		var area = edges.Sum(e => e.Width * e.Height);
		Assert.Equal(400 * 300 - (400 - 6) * (300 - 6), area);
	}

	[Fact]
	public void The_edges_stay_inside_the_outer_rectangle()
	{
		foreach (var e in FrameEdges.Of(-80, 20, 250, 120, 4))
		{
			Assert.True(e.X >= -80 && e.X + e.Width <= 170);
			Assert.True(e.Y >= 20 && e.Y + e.Height <= 140);
		}
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(6, 4)]
	[InlineData(10, 4)]
	public void A_tiny_frame_is_never_empty_or_inside_out(double size, int thickness)
	{
		foreach (var e in FrameEdges.Of(10, 10, size, size, thickness))
		{
			Assert.True(e.Width >= 1);
			Assert.True(e.Height >= 1);
		}
	}

	[Fact]
	public void A_frame_that_shrinks_to_nothing_keeps_one_pixel()
	{
		foreach (var e in FrameEdges.Of(0, 0, 0, 0, 3))
		{
			Assert.True(e.Width >= 1);
			Assert.True(e.Height >= 1);
		}
	}

	[Fact]
	public void Fractions_are_rounded_to_pixels()
	{
		var edges = FrameEdges.Of(10.4, 20.6, 100.6, 50.4, 3);

		Assert.Equal(10, edges[0].X);
		Assert.Equal(21, edges[0].Y);
		Assert.Equal(101, edges[0].Width);
	}

	[Fact]
	public void The_choice_between_frames_and_contents_is_offered_only_where_Windows_shows_frames()
	{
		Assert.True(AnimationPolicy.OffersContentChoice(new SystemAnimationState(true, DragFullWindows: false)));
		Assert.False(AnimationPolicy.OffersContentChoice(new SystemAnimationState(true, DragFullWindows: true)));
	}
}
