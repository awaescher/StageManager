using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using workspacer;

namespace StageManager.Strategies
{
	internal class WindowHideStratgy : IWindowStrategy
	{
		public void Invoke(IWindow window)
		{
			window.Hide();
		}

		public bool IsRelevant(IWindow window)
		{
			return window.CanLayout;
		}
	}
}
