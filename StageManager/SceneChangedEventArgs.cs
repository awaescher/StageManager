using System;
using workspacer;
using IWindow = workspacer.IWindow;

namespace StageManager
{
	public class SceneChangedEventArgs : EventArgs
	{
		public SceneChangedEventArgs(Scene scene, IWindow window, ChangeType change)
		{
			Scene = scene;
			Window = window;
			Change = change;
		}

		public Scene Scene { get; }
		public IWindow Window { get; }
		public ChangeType Change { get; }
	}

	public enum ChangeType
	{ 
		Created,
		Updated,
		Removed
	}
}