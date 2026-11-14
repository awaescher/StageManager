using StageManager.Native.PInvoke;
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;

namespace StageManager
{
	/// <summary>
	/// TEMPORARY: simple file logging to analyse snap group and sidebar issues.
	/// The log is recreated on every start: %LOCALAPPDATA%\StageManager\debug.log
	/// </summary>
	internal static class DebugLog
	{
		private static readonly object _lock = new object();

		public static string FilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StageManager", "debug.log");

		static DebugLog()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
				File.WriteAllText(FilePath, $"--- StageManager started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---{Environment.NewLine}", Encoding.UTF8);
			}
			catch
			{
			}
		}

		public static void Write(string category, string message)
		{
			var line = $"{DateTime.Now:HH:mm:ss.fff} [T{Thread.CurrentThread.ManagedThreadId,2}] {category,-10} {message}{Environment.NewLine}";

			lock (_lock)
			{
				try
				{
					File.AppendAllText(FilePath, line, Encoding.UTF8);
				}
				catch
				{
				}
			}
		}

		public static string Describe(IntPtr handle)
		{
			var buffer = new StringBuilder(255);
			Win32.GetWindowText(handle, buffer, buffer.Capacity + 1);
			var title = buffer.ToString();
			if (title.Length > 40)
				title = title.Substring(0, 40);

			return $"0x{handle.ToInt64():X} \"{title}\"";
		}

		public static string Rect(Rectangle r) => $"[{r.Left},{r.Top},{r.Right},{r.Bottom}]";

		public static string State(IntPtr handle)
		{
			var rect = new Win32.Rect();
			Win32.GetWindowRect(handle, ref rect);

			bool arranged;
			try
			{
				arranged = Win32.IsWindowArranged(handle);
			}
			catch (EntryPointNotFoundException)
			{
				arranged = false;
			}

			return $"rect=[{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}] arranged={arranged} visible={Win32.IsWindowVisible(handle)} min={Win32.IsIconic(handle)} max={Win32.IsZoomed(handle)}";
		}
	}
}
