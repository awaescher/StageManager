using StageManager.Native.PInvoke;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StageManager.Native
{
	public sealed record RunningApp(string FileName, string Path, string Name);

	/// <summary>What the user knows an app by: its name and its icon, read from the executable.</summary>
	public static class AppInfo
	{
		private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>The name in the file properties ("Google Chrome"), or the fallback if there is none.</summary>
		public static string DisplayNameOf(string? path, string fallback)
		{
			if (string.IsNullOrWhiteSpace(path))
				return fallback;

			lock (Names)
			{
				if (Names.TryGetValue(path, out var known))
					return known;

				var name = fallback;

				try
				{
					var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
					if (!string.IsNullOrEmpty(description))
						name = description;
				}
				catch (Exception)
				{
				}

				Names[path] = name;
				return name;
			}
		}

		/// <summary>
		/// Where an app is installed, found by its file name: Windows keeps the path of installed apps under "App Paths". Null for an app
		/// that is not registered there and not part of Windows.
		/// </summary>
		public static string? ResolvePath(string? fileName)
		{
			if (string.IsNullOrWhiteSpace(fileName))
				return null;

			var name = System.IO.Path.GetFileName(fileName.Trim());
			if (!name.Contains('.'))
				name += ".exe";

			try
			{
				foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
				{
					foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths" })
					{
						using var key = hive.OpenSubKey($@"{root}\{name}");
						var value = (key?.GetValue(null) as string)?.Trim().Trim('"');

						if (string.IsNullOrEmpty(value))
							continue;

						var expanded = Environment.ExpandEnvironmentVariables(value);
						if (System.IO.File.Exists(expanded))
							return expanded;
					}
				}

				var system = System.IO.Path.Combine(Environment.SystemDirectory, name);
				if (System.IO.File.Exists(system))
					return system;
			}
			catch (Exception)
			{
			}

			return null;
		}

		/// <summary>The icon of an executable, null if it has none or cannot be read.</summary>
		public static ImageSource? IconOf(string? path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;

			lock (Icons)
			{
				if (Icons.TryGetValue(path, out var known))
					return known;

				ImageSource? icon = null;

				try
				{
					using var drawn = System.Drawing.Icon.ExtractAssociatedIcon(path);
					if (drawn is not null)
					{
						var source = Imaging.CreateBitmapSourceFromHIcon(drawn.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
						source.Freeze();
						icon = source;
					}
				}
				catch (Exception)
				{
				}

				Icons[path] = icon;
				return icon;
			}
		}

		/// <summary>
		/// The apps that have a window StageManager would manage, excluded ones included. Not the scenes of StageManager: those do not
		/// hold the apps that are excluded.
		/// </summary>
		public static IReadOnlyList<RunningApp> Running()
		{
			var apps = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
			var self = Environment.ProcessId;

			Win32.EnumWindows((handle, param) =>
			{
				if (!Win32Helper.IsAppWindow(handle))
					return true;

				var window = new WindowsWindow(handle);
				if (window.ProcessId < 0 || window.ProcessId == self || string.IsNullOrEmpty(window.Title))
					return true;

				var path = window.ProcessExecutable;
				if (string.IsNullOrEmpty(path) || apps.ContainsKey(path) || !window.IsCandidate(ignoreExclusions: true))
					return true;

				apps[path] = new RunningApp(System.IO.Path.GetFileName(path), path, DisplayNameOf(path, window.ProcessName));
				return true;
			}, IntPtr.Zero);

			return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
		}
	}
}
