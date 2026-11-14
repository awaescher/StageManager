using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;

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

		private readonly Dictionary<IntPtr, Rectangle> _layout = new Dictionary<IntPtr, Rectangle>();

		/// <summary>
		/// Creates a process scene, all windows of a process share it.
		/// </summary>
		public Scene(string key, params IWindow[] windows)
			: this(SceneKind.Process, key, IntPtr.Zero, windows)
		{
		}

		private Scene(SceneKind kind, string key, IntPtr monitor, IEnumerable<IWindow> windows)
		{
			_windows.AddRange(windows);
			UpdateTitle();
			Kind = kind;
			Key = key ?? kind.ToString().ToLowerInvariant() + ":" + Id;
			Monitor = monitor;
		}

		/// <summary>
		/// Creates a scene for windows that were arranged together with the Windows snap feature.
		/// Its key never matches a process name, so new windows still go to their process scene.
		/// </summary>
		public static Scene CreateSnapGroup(IntPtr monitor, IEnumerable<IWindow> windows) => new Scene(SceneKind.SnapGroup, null, monitor, windows);

		/// <summary>
		/// Creates a scene that belongs to a single window only. Its key never matches a process name,
		/// so new windows of the same process still go to their process scene.
		/// </summary>
		public static Scene CreateSingleWindowScene(IWindow window) => new Scene(SceneKind.SingleWindow, null, IntPtr.Zero, new[] { window });

		public SceneKind Kind { get; }

		/// <summary>
		/// True if this scene contains windows that were snapped together instead of windows of one process.
		/// </summary>
		public bool IsSnapGroup => Kind == SceneKind.SnapGroup;

		/// <summary>
		/// True if this scene groups the windows of one process. Only these scenes receive new windows.
		/// </summary>
		public bool IsProcessScene => Kind == SceneKind.Process;

		/// <summary>
		/// The monitor the snap group is placed on. Only set for snap groups.
		/// </summary>
		public IntPtr Monitor { get; }

		/// <summary>
		/// The window rectangles (as used by SetWindowPos) of a snap group, stored to restore the exact layout.
		/// </summary>
		public IReadOnlyDictionary<IntPtr, Rectangle> Layout => _layout;

		/// <summary>
		/// Stores the current rectangles of all visible, not minimized windows of this scene.
		/// </summary>
		public void CaptureLayout()
		{
			foreach (var window in _windows)
			{
				if (Win32.IsIconic(window.Handle) || !Win32.IsWindowVisible(window.Handle))
					continue;

				_layout[window.Handle] = SnapHelper.GetWindowRect(window.Handle);
			}
		}

		public bool Contains(IWindow window) => _windows.Any(w => w.Handle == window.Handle);

		public void Remove(IWindow window)
		{
			_windows.RemoveAll(w => w.Handle == window.Handle);
			_layout.Remove(window.Handle);
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

namespace StageManager
{
	public enum SceneKind
	{
		/// <summary>
		/// All windows of a process (the default)
		/// </summary>
		Process,

		/// <summary>
		/// Windows of different scenes that were snapped together
		/// </summary>
		SnapGroup,

		/// <summary>
		/// A single window that got its own scene, e.g. after it was detached from its process scene
		/// </summary>
		SingleWindow
	}
}
