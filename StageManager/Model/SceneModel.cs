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

		public static SceneModel FromScene(Scene scene)
		{
			var model = new SceneModel();
			model.Id = scene.Id;
			model.Windows = new ObservableCollection<WindowModel>(scene.Windows.Select(w => new WindowModel(w)));
			model.Scene = scene;
			return model;
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
		}

		public Guid Id { get; set; }

		public Scene Scene
		{
			get => _scene;
			private set
			{
				if (value?.Id == _scene?.Id)
					return;

				if (_scene is object)
					_scene.SelectedChanged -= _scene_SelectedChanged;

				_scene = value;

				_scene.SelectedChanged += _scene_SelectedChanged;
			}
		}

		private void _scene_SelectedChanged(object sender, EventArgs e)
		{
			RaisePropertyChanged(nameof(Opacity));
		}

		public string Title => _scene.Title;

		public double Opacity => _scene.IsSelected ? 1.0 : 0.5;

		private Scene _scene;

		public string Image => @"C:\Users\awaes_000\Desktop\devenv_1jJwlwtOR8.png";

		private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
		}

		public ObservableCollection<WindowModel> Windows { get; set; } = new ObservableCollection<WindowModel>();
	}
}
