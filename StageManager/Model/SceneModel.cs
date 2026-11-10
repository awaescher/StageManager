using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using StageManager.Core;

namespace StageManager.Model
{
	[System.Diagnostics.DebuggerDisplay("{Title}")]
	public class SceneModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;
		private bool _isVisible;
		private Scene _scene;
		private int _maxVisibleWindows = 3;

		public static SceneModel FromScene(Scene scene)
		{
			var model = new SceneModel();
			model.Id = scene.Id;
			model.Windows = new ObservableCollection<WindowModel>(scene.Windows.Select(w => new WindowModel(w)));
			model.Scene = scene;
			return model;
		}

		public SceneModel()
		{
			Updated = DateTime.UtcNow;
		}

		/// <returns>True if the windows of the card are in a new order, their thumbnails have to be stacked again then.</returns>
		public bool UpdateFromScene(Scene updatedScene)
		{
			if (Id != updatedScene.Id)
				throw new NotSupportedException();

			Scene = updatedScene;
			var before = Windows.Select(w => w.Handle).ToArray();

			var updatedWindows = updatedScene.Windows.ToArray();
			for (int i = 0; i < updatedWindows.Length; i++)
			{
				if (Windows.Count > i && Windows[i].Window.Handle == updatedWindows[i].Handle)
				{
					// same position - just update
					Windows[i].Window = updatedWindows[i];
				}
				else
				{
					var windowToUpdate = Windows.FirstOrDefault(w => w.Window.Handle == updatedWindows[i].Handle);
					if (windowToUpdate is object)
					{
						// has the window but other position -> update and move
						windowToUpdate.Window = updatedWindows[i];
						Windows.Move(Windows.IndexOf(windowToUpdate), i);
					}
					else
					{
						// no window tp update --> add/insert
						Windows.Insert(i, new WindowModel(updatedWindows[i]));
					}
				}
			}

			// remove windows that have been gone
			if (Windows.Count > updatedScene.Windows.Count())
			{
				for (int i = Windows.Count - 1; i >= 0; i--)
				{
					if (!updatedScene.Windows.Any(w => w.Handle == Windows[i].Window.Handle))
						Windows.RemoveAt(i);
				}
			}

			Restack();

			Updated = DateTime.UtcNow;

			return !Windows.Select(w => w.Handle).SequenceEqual(before);
		}

		private void Scene_SelectedChanged(object? sender, EventArgs e)
		{
			Updated = DateTime.UtcNow;
		}

		public Guid Id { get; set; }

		public Scene Scene
		{
			get => _scene;
			private set
			{
				if (_scene is object)
					_scene.SelectedChanged -= Scene_SelectedChanged;

				_scene = value;

				if (_scene is object)
					_scene.SelectedChanged += Scene_SelectedChanged;
			}
		}

		public string Title => Scene?.Title ?? "";

		public bool IsVisible
		{
			get => _isVisible;
			set
			{
				if (_isVisible != value)
				{
					_isVisible = value;
					RaisePropertyChanged();
					RaisePropertyChanged(nameof(Visibility));
				}
			}
		}

		public DateTime Updated { get; private set; }

		private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
		}

		public System.Windows.Visibility Visibility => IsVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

		public ObservableCollection<WindowModel> Windows { get; set; } = new ObservableCollection<WindowModel>();

		public double ThumbnailWidth { get; private set; } = 150;

		private double _cardOpacity = 1;
		private bool _isMergeTarget;

		/// <summary>A card dragged in the sidebar is over this one: letting go merges it into this one.</summary>
		public bool IsMergeTarget
		{
			get => _isMergeTarget;
			set
			{
				if (_isMergeTarget == value)
					return;

				_isMergeTarget = value;
				RaisePropertyChanged();
			}
		}

		/// <summary>
		/// How visible the thumbnails and icons of the card are. 0 while the windows of the card are still flying to it, dimmed while the
		/// card is dragged out of the sidebar.
		/// </summary>
		public double CardOpacity
		{
			get => _cardOpacity;
			set
			{
				if (_cardOpacity == value)
					return;

				_cardOpacity = value;
				RaisePropertyChanged();
			}
		}

		public double ThumbnailHeight { get; private set; } = 100;

		/// <summary>
		/// Sets the card size and arranges the thumbnails of the windows as a stack. Called again whenever the layout or the windows change.
		/// </summary>
		public void ApplyLayout(double thumbnailWidth, double thumbnailHeight, int maxVisibleWindows)
		{
			_maxVisibleWindows = maxVisibleWindows;

			if (ThumbnailWidth != thumbnailWidth || ThumbnailHeight != thumbnailHeight)
			{
				ThumbnailWidth = thumbnailWidth;
				ThumbnailHeight = thumbnailHeight;
				RaisePropertyChanged(nameof(ThumbnailWidth));
				RaisePropertyChanged(nameof(ThumbnailHeight));
			}

			Restack();
		}

		private void Restack()
		{
			var slots = ThumbnailStack.Calculate(ThumbnailWidth, ThumbnailHeight, Windows.Count, _maxVisibleWindows);

			for (int i = 0; i < slots.Length; i++)
				Windows[i].ApplySlot(slots[i]);
		}
	}
}
