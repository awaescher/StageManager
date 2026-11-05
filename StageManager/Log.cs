using System;
using System.IO;

namespace StageManager
{
	internal static class Log
	{
		private const long MAX_SIZE_BYTES = 1024 * 1024;
		private static readonly object _lock = new();
		private static bool _rotationChecked;

		public static void Info(string message) => Write("INFO ", message);

		public static void Warn(string message) => Write("WARN ", message);

		public static void Error(string message, Exception? exception = null) =>
			Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

		private static void Write(string level, string message)
		{
			try
			{
				lock (_lock)
				{
					RotateIfNeeded();
					File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
				}
			}
			catch
			{
				// logging must never take the application down
			}
		}

		private static void RotateIfNeeded()
		{
			if (_rotationChecked)
				return;

			_rotationChecked = true;

			var file = new FileInfo(AppPaths.LogFile);
			if (file.Exists && file.Length > MAX_SIZE_BYTES)
				File.Move(file.FullName, file.FullName + ".old", overwrite: true);
		}
	}
}
