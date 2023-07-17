using System;
using System.Reflection;
using Microsoft.Win32;

namespace StageManager
{
	public static class AutoStart
	{
		private const string REG_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

		public static void SetStartup(string appName, bool startup)
		{
			var key = Registry.CurrentUser.OpenSubKey(REG_KEY, writable: true);

			if (startup)
				key.SetValue(appName, GetAppPath());
			else
				key.DeleteValue(appName, throwOnMissingValue: false);
		}

		public static bool IsStartup(string appName) => IsStartup(Registry.CurrentUser.OpenSubKey(REG_KEY), appName);

		public static bool IsStartup(RegistryKey key, string appName) => GetValueAsString(key, appName).Equals(GetAppPath(), StringComparison.OrdinalIgnoreCase);

		private static string GetValueAsString(RegistryKey key, string appName) => key.GetValue(appName)?.ToString() ?? "";

		private static string GetAppPath() => $@"""{Environment.ProcessPath}""";
	}
}
