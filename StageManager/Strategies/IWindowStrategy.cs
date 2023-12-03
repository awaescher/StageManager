using StageManager.Native.Window;

namespace StageManager.Strategies
{
    internal interface IWindowStrategy
	{
		void Show(IWindow window);

		void Hide(IWindow window);
	}
}