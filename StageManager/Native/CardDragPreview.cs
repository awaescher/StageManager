using StageManager.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StageManager.Native
{
	/// <summary>
	/// The windows of a card that is dragged out of the sidebar. They follow the pointer on a <see cref="FlightOverlay"/> and fly to the
	/// stage when the card is dropped, or back to the card when the drag is given up.
	/// </summary>
	internal sealed class CardDragPreview
	{
		private sealed record Entry(int Slot, Area Frame);

		private const string HintText = "Add to stage";

		private readonly FlightOverlay _overlay;
		private readonly List<Entry> _entries;
		private readonly int _windowCount;
		private readonly int _maxVisible;
		private readonly double _scale;
		private readonly Area _hintArea;

		private CardDragPreview(FlightOverlay overlay, List<Entry> entries, int windowCount, int maxVisible, double scale, Area hintArea)
		{
			_overlay = overlay;
			_entries = entries;
			_windowCount = windowCount;
			_maxVisible = maxVisible;
			_scale = scale;
			_hintArea = hintArea;
		}

		/// <param name="windows">The windows of the card, back to front.</param>
		/// <param name="card">Where the thumbnails of the card are, in physical pixels.</param>
		/// <param name="stage">Where the hint shows, in physical pixels.</param>
		/// <param name="scale">Physical pixels per unit of the sidebar layout.</param>
		public static CardDragPreview? Start(IReadOnlyList<IntPtr> windows, Area card, Area stage, FlightStyle style, int maxVisible, double scale)
		{
			if (windows.Count == 0 || card.Width < 8 || card.Height < 8)
				return null;

			var overlay = new FlightOverlay(WindowGeometry.BoundsOf(card), style);
			var entries = new List<Entry>();

			try
			{
				overlay.Show();

				var slots = DragPreviewLayout.Slots(card, windows.Count, maxVisible, scale);
				for (var i = 0; i < windows.Count; i++)
				{
					// hidden windows are out of the screen, they come back where they were before
					var frame = Parking.OriginalFrameOf(windows[i]) ?? WindowGeometry.FrameOf(windows[i]);
					if (frame is not Area f || f.Width <= 0 || f.Height <= 0)
						continue;

					var pose = DragPreviewLayout.InSlot(slots[i], f.Width, f.Height);
					if (overlay.Add(windows[i], pose, pose))
						entries.Add(new Entry(i, f));
				}
			}
			catch (Exception ex)
			{
				Log.Error("The drag preview could not be prepared", ex);
				overlay.Finish();
				return null;
			}

			if (entries.Count == 0)
			{
				overlay.Finish();
				return null;
			}

			return new CardDragPreview(overlay, entries, windows.Count, maxVisible, scale, stage);
		}

		/// <summary>Completes once the windows show on the overlay, exactly on top of the card.</summary>
		public Task WhenOnScreen() => _overlay.WhenOnScreen();

		/// <param name="card">The rectangle of the whole card, see <see cref="DragPreviewLayout.CardRect"/>.</param>
		public void Update(Area card, double hintOpacity)
		{
			var slots = DragPreviewLayout.Slots(card, _windowCount, _maxVisible, _scale);

			for (var k = 0; k < _entries.Count; k++)
				_overlay.Place(k, DragPreviewLayout.InSlot(slots[_entries[k].Slot], _entries[k].Frame.Width, _entries[k].Frame.Height));

			_overlay.ShowHint(_hintArea, HintText, hintOpacity);
		}

		/// <summary>The windows fly to where they show on the stage.</summary>
		public Task Drop(TimeSpan duration)
		{
			_overlay.ShowHint(_hintArea, HintText, 0);
			return _overlay.FlyTo(_entries.Select(e => FlightPlanner.OfFrame(e.Frame)).ToArray(), duration);
		}

		/// <summary>The windows fly back onto the card in the sidebar.</summary>
		/// <param name="first">Index of the first of these windows in the stack of the card, for a card that also holds other windows.</param>
		/// <param name="total">Number of windows in the stack of the card, 0 for just these.</param>
		public Task Return(Area card, TimeSpan duration, int first = 0, int total = 0)
		{
			_overlay.ShowHint(_hintArea, HintText, 0);
			var count = Math.Max(total, first + _windowCount);
			var slots = DragPreviewLayout.Slots(card, count, _maxVisible, _scale);
			return _overlay.FlyTo(_entries.Select(e => DragPreviewLayout.InSlot(slots[first + e.Slot], e.Frame.Width, e.Frame.Height)).ToArray(), duration);
		}

		public void Finish() => _overlay.Finish();
	}
}
