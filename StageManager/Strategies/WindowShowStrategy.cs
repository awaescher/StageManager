using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using workspacer;

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
