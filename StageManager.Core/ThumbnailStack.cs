namespace StageManager.Core;

/// <summary>Placement of one thumbnail of a stacked scene. Offsets are measured from the bottom left corner of the card.</summary>
public readonly record struct StackSlot(double Width, double Height, double OffsetX, double OffsetY, bool IsVisible);

/// <summary>
/// Stacks the thumbnails of a scene behind each other. The last window is in front and uses the bottom left corner,
/// every window behind it moves a step up and to the right, and all thumbnails get smaller so the stack always fits into the card.
/// </summary>
public static class ThumbnailStack
{
	public const double Step = 8;

	/// <param name="windowCount">Number of windows in the scene, ordered from back to front.</param>
	/// <param name="maxVisible">Number of windows that are shown, the frontmost ones. The others get an invisible slot.</param>
	public static StackSlot[] Calculate(double cardWidth, double cardHeight, int windowCount, int maxVisible)
	{
		var slots = new StackSlot[Math.Max(0, windowCount)];
		if (slots.Length == 0)
			return slots;

		var shown = Math.Clamp(maxVisible, 1, slots.Length);
		var firstVisible = slots.Length - shown;

		var width = Math.Max(1, cardWidth - (shown - 1) * Step);
		var height = Math.Max(1, cardHeight - (shown - 1) * Step);

		for (var i = 0; i < slots.Length; i++)
		{
			var depth = slots.Length - 1 - i;
			slots[i] = i < firstVisible
				? new StackSlot(width, height, 0, 0, false)
				: new StackSlot(width, height, depth * Step, depth * Step, true);
		}

		return slots;
	}
}

public readonly record struct FittedRect(double X, double Y, double Width, double Height);

public static class AspectFit
{
	/// <summary>
	/// Fits a source of the given size into a container without distortion and centers it.
	/// Returns the full container if the source size is unknown.
	/// </summary>
	public static FittedRect Calculate(double containerWidth, double containerHeight, double sourceWidth, double sourceHeight)
	{
		if (containerWidth <= 0 || containerHeight <= 0)
			return new FittedRect(0, 0, 0, 0);

		if (sourceWidth <= 0 || sourceHeight <= 0)
			return new FittedRect(0, 0, containerWidth, containerHeight);

		var scale = Math.Min(containerWidth / sourceWidth, containerHeight / sourceHeight);
		var width = sourceWidth * scale;
		var height = sourceHeight * scale;

		return new FittedRect((containerWidth - width) / 2, (containerHeight - height) / 2, width, height);
	}
}
