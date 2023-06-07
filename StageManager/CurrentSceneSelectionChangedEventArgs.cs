using System;

namespace StageManager
{
	public class CurrentSceneSelectionChangedEventArgs : EventArgs
	{
		public CurrentSceneSelectionChangedEventArgs(Scene prior, Scene current)
		{
			Prior = prior;
			Current = current;
		}

		public Scene Prior { get; }

		public Scene Current { get; }

	}
}