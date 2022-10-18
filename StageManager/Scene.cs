using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using IWindow = workspacer.IWindow;

namespace StageManager
{
	[System.Diagnostics.DebuggerDisplay("{Title}")]
	public class Scene
	{
		public event EventHandler SelectedChanged;

		public Guid Id { get; } = Guid.NewGuid();

		public string Title { get; private set; }

		public IEnumerable<IWindow> Windows => _windows;

		private List<IWindow> _windows = new List<IWindow>();
		private bool _selected;

		public Scene(string key, params IWindow[] windows)
		{
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
			_windows.Add(window);
			UpdateTitle();
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
