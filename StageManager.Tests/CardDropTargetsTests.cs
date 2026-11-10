using StageManager.Core;

namespace StageManager.Tests;

public class CardDropTargetsTests
{
	private static readonly Guid A = Guid.NewGuid();
	private static readonly Guid B = Guid.NewGuid();

	// two cards above each other with a gap of 30 pixels
	private static readonly (Guid, Area)[] Cards =
	{
		(A, new Area(20, 100, 220, 220)),
		(B, new Area(20, 250, 220, 370))
	};

	[Fact]
	public void The_pointer_well_inside_a_card_makes_it_the_target()
	{
		var t = new CardDropTargets();

		Assert.Equal(A, t.Update(120, 160, Cards));
		Assert.Equal(B, t.Update(120, 300, Cards));
	}

	[Fact]
	public void The_edge_of_a_card_is_not_enough()
	{
		var t = new CardDropTargets();

		Assert.Null(t.Update(120, 100 + CardDropTargets.EnterInset - 1, Cards));
		Assert.Null(t.Update(22, 160, Cards));
	}

	[Fact]
	public void Between_cards_there_is_no_target()
	{
		var t = new CardDropTargets();

		Assert.Null(t.Update(120, 235, Cards));
	}

	[Fact]
	public void A_target_stays_while_the_pointer_slips_a_little_out_of_it()
	{
		var t = new CardDropTargets();
		t.Update(120, 160, Cards);

		Assert.Equal(A, t.Update(120, 220 + CardDropTargets.LeaveMargin - 1, Cards));
		Assert.Equal(A, t.Update(20 - CardDropTargets.LeaveMargin + 1, 160, Cards));
	}

	[Fact]
	public void A_target_goes_when_the_pointer_has_clearly_left_it()
	{
		var t = new CardDropTargets();
		t.Update(120, 160, Cards);

		Assert.Null(t.Update(120, 220 + CardDropTargets.LeaveMargin + 1, Cards));
	}

	[Fact]
	public void The_next_card_takes_over_once_the_pointer_is_well_inside_it()
	{
		var t = new CardDropTargets();
		t.Update(120, 160, Cards);

		Assert.Equal(B, t.Update(120, 300, Cards));
	}

	[Fact]
	public void A_target_that_left_the_sidebar_is_dropped()
	{
		var t = new CardDropTargets();
		t.Update(120, 160, Cards);

		Assert.Null(t.Update(120, 160, new[] { Cards[1] }));
	}

	[Fact]
	public void Clear_forgets_the_target()
	{
		var t = new CardDropTargets();
		t.Update(120, 160, Cards);

		t.Clear();

		Assert.Null(t.Target);
	}
}
