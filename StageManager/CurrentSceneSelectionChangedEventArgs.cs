using System;

namespace StageManager
{
	public class CurrentSceneSelectionChangedEventArgs : EventArgs
	{
		public CurrentSceneSelectionChangedEventArgs(Scene prior, Scene current, bool priorCardWaits = false)
		{
			Prior = prior;
			Current = current;
			PriorCardWaits = priorCardWaits;
		}

		public Scene Prior { get; }

		public Scene Current { get; }

		/// <summary>
		/// The windows of the prior scene fly to its card. The card stays invisible until <see cref="SceneManager.CardLanded"/>,
		/// otherwise it would show them before they arrive.
		/// </summary>
		public bool PriorCardWaits { get; }
	}
}
