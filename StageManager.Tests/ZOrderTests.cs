using StageManager.Core;

namespace StageManager.Tests;

public class ZOrderTests
{
	[Fact]
	public void The_top_window_on_the_screen_ends_up_in_front()
	{
		var order = ZOrder.FromScreen(new long[] { 1, 2, 3 }, topToBottom: new long[] { 2, 3, 1 });

		Assert.Equal(new long[] { 1, 3, 2 }, order);
	}

	[Fact]
	public void Windows_of_other_scenes_in_between_do_not_matter()
	{
		var order = ZOrder.FromScreen(new long[] { 1, 2 }, topToBottom: new long[] { 9, 1, 8, 2, 7 });

		Assert.Equal(new long[] { 2, 1 }, order);
	}

	[Fact]
	public void Windows_that_are_not_on_the_screen_keep_their_place()
	{
		// 5 and 6 are hidden, 1 and 2 swap the places they have
		var order = ZOrder.FromScreen(new long[] { 5, 1, 6, 2 }, topToBottom: new long[] { 1, 2 });

		Assert.Equal(new long[] { 5, 2, 6, 1 }, order);
	}

	[Fact]
	public void Nothing_on_the_screen_changes_nothing()
	{
		Assert.Equal(new long[] { 1, 2, 3 }, ZOrder.FromScreen(new long[] { 1, 2, 3 }, topToBottom: Array.Empty<long>()));
	}

	[Fact]
	public void A_window_listed_twice_counts_where_it_is_listed_first()
	{
		var order = ZOrder.FromScreen(new long[] { 1, 2 }, topToBottom: new long[] { 2, 1, 2 });

		Assert.Equal(new long[] { 1, 2 }, order);
	}

	[Fact]
	public void To_front_moves_the_windows_to_the_end_in_their_order_and_keeps_the_others()
	{
		Assert.Equal(new long[] { 1, 4, 3, 2 }, ZOrder.ToFront(new long[] { 1, 2, 3, 4 }, front: new long[] { 3, 2 }));
	}

	[Fact]
	public void To_front_ignores_unknown_windows()
	{
		Assert.Equal(new long[] { 2, 1 }, ZOrder.ToFront(new long[] { 1, 2 }, front: new long[] { 7, 1 }));
	}
}
