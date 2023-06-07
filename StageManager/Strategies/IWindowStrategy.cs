using StageManager.Native.Window;

namespace StageManager.Strategies
{
    internal interface IWindowStrategy
	{
		void Invoke(IWindow window);
	}
}