namespace StageManager.Core;

/// <summary>
/// The order windows of a scene lie on top of each other. Scenes keep their windows back to front: the last one is in front,
/// on the card in the sidebar, while flying and on the stage.
/// </summary>
public static class ZOrder
{
	/// <summary>Sorts the windows that are on the screen the way they lie there.</summary>
	/// <param name="backToFront">The windows in the order known so far.</param>
	/// <param name="topToBottom">The windows on the screen, the top one first, as Windows lists them.</param>
	/// <returns>
	/// Back to front. The windows on the screen are sorted among the places they had, the others (hidden or minimized) keep theirs:
	/// nothing tells where they lie.
	/// </returns>
	public static IReadOnlyList<long> FromScreen(IReadOnlyList<long> backToFront, IReadOnlyList<long> topToBottom)
	{
		var depth = new Dictionary<long, int>();
		for (var i = 0; i < topToBottom.Count; i++)
			depth.TryAdd(topToBottom[i], i);

		var sorted = new Queue<long>(backToFront.Where(depth.ContainsKey).OrderByDescending(w => depth[w]));

		return backToFront.Select(w => depth.ContainsKey(w) ? sorted.Dequeue() : w).ToList();
	}

	/// <summary>Puts windows in front of the others, in the given order. The rest keep their order. Windows that are not there are ignored.</summary>
	/// <param name="front">The windows that come to the front, back to front.</param>
	public static IReadOnlyList<long> ToFront(IReadOnlyList<long> backToFront, IReadOnlyList<long> front)
	{
		var moving = front.Where(backToFront.Contains).Distinct().ToList();
		return backToFront.Where(w => !moving.Contains(w)).Concat(moving).ToList();
	}
}
