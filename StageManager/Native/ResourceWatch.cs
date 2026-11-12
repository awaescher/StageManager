using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace StageManager.Native
{
	/// <summary>
	/// Writes how many objects of Windows the app holds to the log, every few minutes. A process may hold 10 000 objects of the window
	/// manager; when it is out of them, even a menu cannot be opened and the app ends. The curve shows what fills them up.
	/// </summary>
	internal static class ResourceWatch
	{
		private const uint GR_GDIOBJECTS = 0;
		private const uint GR_USEROBJECTS = 1;
		private const uint Limit = 10000;

		private static Timer? _timer;
		private static uint _lastLogged;
		private static bool _warned;

		[DllImport("user32.dll")]
		private static extern uint GetGuiResources(IntPtr process, uint flags);

		public static void Start()
		{
			_timer ??= new Timer(_ => Check(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
		}

		private static void Check()
		{
			try
			{
				using var process = Process.GetCurrentProcess();
				var user = GetGuiResources(process.Handle, GR_USEROBJECTS);
				var gdi = GetGuiResources(process.Handle, GR_GDIOBJECTS);
				var text = $"Objects of Windows held: {user} of the window manager (limit {Limit}), {gdi} of the graphics, {process.HandleCount} handles";

				if (user >= Limit * 7 / 10)
				{
					// the last warning before the app runs out
					if (!_warned)
					{
						_warned = true;
						Log.Error(text);
					}
				}
				else if (Math.Abs((long)user - _lastLogged) >= 100)
				{
					// quiet while nothing changes, a growing number shows up in the log
					Log.Info(text);
				}

				_lastLogged = user;
			}
			catch (Exception ex)
			{
				Log.Warn($"The objects of Windows could not be counted: {ex.Message}");
			}
		}
	}
}
