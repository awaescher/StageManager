using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StageManager
{
    [System.Diagnostics.DebuggerDisplay("{Title}")]
	public class Scene
	{
		public event EventHandler SelectedChanged;

		public Guid Id { get; }

		public string Title { get; private set; }

		public IEnumerable<IWindow> Windows => _windows;

		private List<IWindow> _windows = new List<IWindow>();
		private bool _selected;

		public Scene(string key, params IWindow[] windows) : this(Guid.NewGuid(), key, windows)
		{
		}

		public Scene(Guid id, string key, params IWindow[] windows)
		{
			Id = id;
			_windows.AddRange(windows);
			UpdateTitle();
			Key = key;
		}

		public void Remove(IWindow window)
		{
			_windows.Remove(window);
			UpdateTitle();
		}

		public void Add(IWindow window)
		{
			if (_windows.Contains(window))
				return;

			_windows.Add(window);
			UpdateTitle();
		}

		/// <summary>Brings the windows into a new order, back to front. Windows the order does not name keep their place at the back.</summary>
		/// <returns>True if the order changed.</returns>
		public bool Reorder(IReadOnlyList<IntPtr> backToFront)
		{
			var position = new Dictionary<IntPtr, int>();
			for (var i = 0; i < backToFront.Count; i++)
				position.TryAdd(backToFront[i], i);

			var reordered = _windows
				.Select((w, i) => (Window: w, Key: position.TryGetValue(w.Handle, out var p) ? p : -1, Index: i))
				.OrderBy(x => x.Key)
				.ThenBy(x => x.Index)
				.Select(x => x.Window)
				.ToList();

			if (reordered.SequenceEqual(_windows))
				return false;

			_windows = reordered;
			UpdateTitle();
			return true;
		}

		private void UpdateTitle()
		{
			Title = string.Join(Environment.NewLine, Windows.Select(w => Max(20, w.Title)));
		}

		private string Max(int max, string title)
		{
			if (title.Length > max)
				return title.Substring(0, max - 3) + "...";

			return title;
		}

		public bool IsSelected
		{
			get => _selected;
			set 
			{
				if (_selected != value)
				{
					_selected = value;
					SelectedChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		public bool HasFocus => Windows.Any(w => w.IsFocused);

		public string Key { get; }
	}
}
