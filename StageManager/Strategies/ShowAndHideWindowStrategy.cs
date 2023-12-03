using StageManager.Native.Window;

namespace StageManager.Strategies
{
	/// <summary>
	/// Shows and hides windows, which makes them disappear completely until they are showed again.
	/// The user cannot bring them back without some advanced tricks.
	/// </summary>
	internal class ShowAndHideWindowStrategy : IWindowStrategy
	{
		public void Show(IWindow window)
		{
			window.ShowInCurrentState();
		}

		public void Hide(IWindow window)
		{
			window.Hide();
		}
	}
}
