namespace StageManager.Core;

/// <summary>Where a window sits on its card when its proportions leave room: 0 is left or top, 1 is right or bottom.</summary>
public readonly record struct CardAlignment(double X, double Y)
{
	public static readonly CardAlignment Centered = new(0.5, 0.5);

	/// <summary>
	/// The bottom corner at the screen edge. The icons of the app sit on that corner, so they touch the window however wide or tall it is,
	/// and the windows of all cards share one line to look along.
	/// </summary>
	public static CardAlignment For(SidebarSide side) => new(side == SidebarSide.Right ? 1 : 0, 1);
}

/// <summary>How tall a card is: as tall as its windows are on it, so the space between two cards is the same everywhere.</summary>
public static class CardShape
{
	/// <summary>A card of very flat windows still stays this tall, otherwise it would be a sliver.</summary>
	public const double MinHeight = 48;

	/// <param name="cardWidth">Width of the card, the windows are fitted into it.</param>
	/// <param name="maxHeight">The tallest a card gets: the height the layout works out for the sidebar.</param>
	/// <param name="backToFront">Width and height of each window of the card, back to front. 0 for a window of unknown size.</param>
	/// <param name="maxVisible">How many of the windows show as thumbnails, the frontmost ones.</param>
	public static double Height(double cardWidth, double maxHeight, IReadOnlyList<(double Width, double Height)> backToFront, int maxVisible)
	{
		if (backToFront.Count == 0 || !(maxHeight > 0))
			return maxHeight;

		var shown = Math.Clamp(maxVisible, 1, backToFront.Count);
		var inset = (shown - 1) * ThumbnailStack.Step;
		var slotWidth = Math.Max(1, cardWidth - inset);
		var slotHeight = Math.Max(1, maxHeight - inset);

		var tallest = 0.0;
		for (var i = backToFront.Count - shown; i < backToFront.Count; i++)
		{
			var source = backToFront[i];
			tallest = Math.Max(tallest, AspectFit.Calculate(slotWidth, slotHeight, source.Width, source.Height).Height);
		}

		return Math.Clamp(tallest + inset, Math.Min(MinHeight, maxHeight), maxHeight);
	}
}
