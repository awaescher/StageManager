using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StageManager
{
	internal static class PoorMansAsync
	{
		public static void Wait(Func<Task> action, TimeSpan maximumWaitTime)
		{
			var done = false;
			var watch = Stopwatch.StartNew();

			Task.Run(async () =>
			{
				await Task.Run(action).ConfigureAwait(false);
				done = true;
			});

			while (!done)
			{
				if (watch.Elapsed < maximumWaitTime)
				{
					System.Threading.Thread.Sleep(10);
				}
				else
				{
					watch.Stop();
					done = true;
				}
			}
		}
	}
}
