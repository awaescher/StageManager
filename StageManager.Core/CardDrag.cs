namespace StageManager.Core;

/// <summary>
/// Finds the card in the sidebar a dragged card would be merged into. A card becomes the target once the pointer is well inside it
/// and stays the target until the pointer has clearly left it, so the highlight does not jump between neighbouring cards.
/// </summary>
public sealed class CardDropTargets
{
	/// <summary>Pixels the pointer has to be inside a card to make it the target.</summary>
	public const double EnterInset = 10;

	/// <summary>Pixels around the target the pointer may leave it before it is not the target any more.</summary>
	public const double LeaveMargin = 14;

	private Guid? _target;

	public Guid? Target => _target;

	/// <param name="cards">The cards in the sidebar, without the dragged one.</param>
	public Guid? Update(double x, double y, IReadOnlyList<(Guid Id, Area Area)> cards)
	{
		if (_target is Guid current)
		{
			foreach (var (cardId, area) in cards)
			{
				if (cardId == current && Contains(area, x, y, -LeaveMargin))
					return _target;
			}
		}

		_target = null;
		foreach (var (cardId, area) in cards)
		{
			if (Contains(area, x, y, EnterInset))
			{
				_target = cardId;
				break;
			}
		}

		return _target;
	}

	public void Clear() => _target = null;

	private static bool Contains(Area a, double x, double y, double inset) =>
		a.Width > 2 * inset && a.Height > 2 * inset
		&& x >= a.Left + inset && x <= a.Right - inset && y >= a.Top + inset && y <= a.Bottom - inset;
}

public enum DragEnd
{
	/// <summary>The button went up without a drag: a plain click.</summary>
	Click,

	/// <summary>The card was let go on the stage.</summary>
	Drop,

	/// <summary>The drag was given up: let go over the sidebar, Escape or the mouse was lost.</summary>
	Cancel
}

/// <param name="Progress">0 over the sidebar to 1 when the card is far out on the stage.</param>
/// <param name="IsArmed">Letting go now drops the card on the stage.</param>
public readonly record struct DragState(bool IsDragging, double Progress, bool IsArmed);

/// <summary>
/// Tells a click from a drag and decides when a dragged card is "over the stage". Pure logic without any Win32 or WPF.
/// <para>
/// A drag only starts after the pointer moved a minimum distance, so a click that wobbles a little is still a click.
/// The drop target has two thresholds (arm and disarm): the card is only armed when the pointer is clearly out of the sidebar
/// and only disarmed when it is clearly back, so it does not flicker at the edge.
/// </para>
/// </summary>
public sealed class CardDragTracker
{
	/// <summary>Pixels the pointer has to move before a press becomes a drag.</summary>
	public const double DefaultStartDistance = 8;

	/// <summary>Pixels beyond the sidebar at which a dragged card is ready to be dropped.</summary>
	public const double ArmDistance = 48;

	/// <summary>Pixels beyond the sidebar below which an armed card is not ready any more.</summary>
	public const double DisarmDistance = 16;

	/// <summary>Pixels beyond the sidebar at which the dragged card has reached its full size.</summary>
	public const double FullDistance = 220;

	private readonly double _startDistance;
	private double _startX;
	private double _startY;
	private bool _pressed;
	private bool _dragging;
	private bool _armed;
	private double _progress;

	public CardDragTracker(double startDistance = DefaultStartDistance)
	{
		_startDistance = Math.Max(1, startDistance);
	}

	public bool IsPressed => _pressed;

	public bool IsDragging => _dragging;

	public DragState State => new(_dragging, _progress, _armed);

	public void Press(double x, double y)
	{
		_pressed = true;
		_dragging = false;
		_armed = false;
		_progress = 0;
		_startX = x;
		_startY = y;
	}

	/// <param name="outside">Pixels beyond the sidebar, see <see cref="SidebarGeometry.DistanceOutside"/>.</param>
	public DragState Move(double x, double y, double outside)
	{
		if (!_pressed)
			return State;

		if (!_dragging)
		{
			var dx = x - _startX;
			var dy = y - _startY;
			if (dx * dx + dy * dy < _startDistance * _startDistance)
				return State;

			_dragging = true;
		}

		_progress = double.IsFinite(outside) ? Math.Clamp(outside / FullDistance, 0, 1) : 0;

		if (!_armed && outside >= ArmDistance)
			_armed = true;
		else if (_armed && !(outside >= DisarmDistance))
			_armed = false;

		return State;
	}

	/// <summary>The button went up.</summary>
	public DragEnd Release()
	{
		var result = !_dragging ? DragEnd.Click : _armed ? DragEnd.Drop : DragEnd.Cancel;
		Reset();
		return result;
	}

	public void Cancel() => Reset();

	private void Reset()
	{
		_pressed = false;
		_dragging = false;
		_armed = false;
		_progress = 0;
	}
}

/// <summary>Where the windows of a dragged card show while the card follows the pointer. Pure calculation.</summary>
public static class DragPreviewLayout
{
	/// <summary>How much larger the dragged card gets when it is far out on the stage.</summary>
	public const double MaxScale = 1.7;

	public static double Scale(double progress) => 1 + (MaxScale - 1) * FlightMotion.Ease(progress);

	/// <summary>
	/// The rectangle of the whole card. It starts exactly over the card in the sidebar and moves to the pointer while it is pulled out,
	/// so it does not jump when the drag begins.
	/// </summary>
	/// <param name="grabOffsetX">Pointer position minus the center of the card when the drag began.</param>
	public static Area CardRect(double pointerX, double pointerY, double grabOffsetX, double grabOffsetY, double cardWidth, double cardHeight, double progress)
	{
		var scale = Scale(progress);
		var settle = FlightMotion.Ease(progress);
		var centerX = pointerX - grabOffsetX * (1 - settle);
		var centerY = pointerY - grabOffsetY * (1 - settle);
		var width = cardWidth * scale;
		var height = cardHeight * scale;

		return new Area(centerX - width / 2, centerY - height / 2, centerX + width / 2, centerY + height / 2);
	}

	/// <summary>One slot per window, back to front. Windows beyond the visible ones get a slot without opacity.</summary>
	/// <param name="scale">Physical pixels per unit of the sidebar layout, the steps of the stack are measured in that unit.</param>
	public static IReadOnlyList<Pose> Slots(Area card, int windowCount, int maxVisible, double scale = 1)
	{
		scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
		var stack = ThumbnailStack.Calculate(card.Width / scale, card.Height / scale, windowCount, maxVisible);
		var poses = new List<Pose>(stack.Length);

		foreach (var slot in stack)
		{
			// the offsets of a stack are measured from the bottom left corner of the card
			var left = card.Left + slot.OffsetX * scale;
			var top = card.Bottom - (slot.OffsetY + slot.Height) * scale;
			poses.Add(new Pose(left, top, slot.Width * scale, slot.Height * scale, slot.IsVisible ? 1 : 0));
		}

		return poses;
	}

	/// <summary>
	/// Where each window shows on a card in the sidebar: in its slot of the stack, fitted without distortion, like the card draws it.
	/// Back to front. Flights start and end here, so the card and the flying windows match when one takes over from the other.
	/// </summary>
	public static Pose[] OnStack(Area card, IReadOnlyList<(double Width, double Height)> sources, int maxVisible, double scale = 1)
	{
		var slots = Slots(card, sources.Count, maxVisible, scale);
		var poses = new Pose[sources.Count];

		for (var i = 0; i < poses.Length; i++)
			poses[i] = InSlot(slots[i], sources[i].Width, sources[i].Height);

		return poses;
	}

	public static Pose InSlot(Pose slot, double sourceWidth, double sourceHeight) =>
		FlightPlanner.OnCard(new Area(slot.X, slot.Y, slot.X + slot.Width, slot.Y + slot.Height), sourceWidth, sourceHeight, slot.Opacity);

	/// <summary>How visible the hint for the stage is: a faint outline while the card leaves the sidebar, fully when letting go would drop it.</summary>
	public static double HintOpacity(double progress, bool armed) => armed ? 1 : Math.Clamp(progress, 0, 1) * 0.35;
}
