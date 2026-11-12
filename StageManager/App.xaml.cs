using StageManager.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		public static SettingsService Settings { get; } = new SettingsService(new SettingsStore(Path.Combine(AppPaths.DataDirectory, "settings.json"), Log.Warn));

		/// <summary>The hotkeys of the running app, null if it is not started yet or already closed.</summary>
		public static StageManager.Native.HotkeyManager? Hotkeys { get; set; }

		public App()
		{
			AppDomain.CurrentDomain.UnhandledException += (_, e) =>
				Log.Error($"Unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);

			TaskScheduler.UnobservedTaskException += (_, e) =>
			{
				Log.Error("Unobserved task exception", e.Exception);
				e.SetObserved();
			};

			DispatcherUnhandledException += (_, e) =>
				Log.Error("Unhandled UI exception", e.Exception);
		}

		protected override void OnStartup(StartupEventArgs e)
		{
			Log.Info($"Starting StageManager (data directory: {AppPaths.DataDirectory})");
			base.OnStartup(e);
		}

		protected override void OnExit(ExitEventArgs e)
		{
			Log.Info($"Exiting StageManager (exit code {e.ApplicationExitCode})");
			base.OnExit(e);
		}
	}
}
