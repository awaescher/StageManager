using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using workspacer;

namespace StageManager.Strategies
{
	internal class WindowNormalizeStrategy : IWindowStrategy
	{
		public void Invoke(IWindow window)
		{
			Win32.ShowWindow(window.Handle, Win32.SW.SW_SHOWNOACTIVATE);
		}
	}
}
