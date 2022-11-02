using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace StageManager.Model
{
	[System.Diagnostics.DebuggerDisplay("{Title}")]
	public class SceneModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;
		private bool _isVisible;
		private Scene _scene;

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

		public void UpdateFromScene(Scene updatedScene)
		{
			if (Id != updatedScene.Id)
				throw new NotSupportedException();

			Scene = updatedScene;

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

			Updated = DateTime.UtcNow;
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
	}
}
