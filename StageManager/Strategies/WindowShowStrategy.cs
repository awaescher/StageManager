using StageManager.Native.Window;

namespace StageManager.Strategies
{
    internal class WindowShowStrategy : IWindowStrategy
	{
		public void Invoke(IWindow window)
		{
			window.ShowInCurrentState();
		}
	}
}
