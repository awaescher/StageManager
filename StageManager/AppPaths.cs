using System;
using System.IO;

namespace StageManager
{
	internal static class AppPaths
	{
		public static string DataDirectory { get; } = Initialize();

		public static string LogFile => Path.Combine(DataDirectory, "debug.log");

		private static string Initialize()
		{
			var overridePath = Environment.GetEnvironmentVariable("STAGEMANAGER_DATA_DIR");
			var path = string.IsNullOrWhiteSpace(overridePath)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StageManager")
				: overridePath;

			Directory.CreateDirectory(path);
			return path;
		}
	}
}
