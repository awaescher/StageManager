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

		// held for as long as the process lives: it is what tells a second start that StageManager runs already
		private static System.Threading.Mutex? _singleInstance;

		public App()
		{
			AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			{
				Log.Error($"Unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);

				// the app is going down: the windows it moved out of the screen must not go with it
				if (e.IsTerminating)
					BringBackWindows();
			};

			TaskScheduler.UnobservedTaskException += (_, e) =>
			{
				Log.Error("Unobserved task exception", e.Exception);
				e.SetObserved();
			};

			DispatcherUnhandledException += (_, e) =>
				Log.Error("Unhandled UI exception", e.Exception);
		}

		/// <summary>Best effort, whatever state the app is in: it may be out of handles.</summary>
		private static void BringBackWindows()
		{
			try
			{
				StageManager.Native.Parking.RestoreAll();
				StageManager.Native.WindowRescue.Run();
			}
			catch (Exception ex)
			{
				Log.Error("The windows could not be brought back", ex);
			}
		}

		/// <summary>
		/// Two copies of StageManager would hide and show the same windows against each other until one of them runs out of handles.
		/// </summary>
		private static bool IsOnlyInstance()
		{
			try
			{
				// one per data directory: the settings and the list of hidden windows are what two copies would fight over
				var bytes = System.Text.Encoding.UTF8.GetBytes(AppPaths.DataDirectory.ToUpperInvariant());
				var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
				_singleInstance = new System.Threading.Mutex(initiallyOwned: true, $@"Local\StageManager.{key}", out var createdNew);
				return createdNew;
			}
			catch (Exception ex)
			{
				// better to run twice than not at all
				Log.Warn($"The check for a second instance failed: {ex.Message}");
				return true;
			}
		}

		protected override void OnStartup(StartupEventArgs e)
		{
			if (!IsOnlyInstance())
			{
				Log.Warn("StageManager runs already, this start is ended");
				Loc.SetLanguage(Settings.Current.Language);
				MessageBox.Show(Loc.Get("App.AlreadyRunning"), "StageManager", MessageBoxButton.OK, MessageBoxImage.Information);

				// the first copy keeps its windows: nothing of this one must touch them
				Environment.Exit(0);
				return;
			}

			Log.Info($"Starting StageManager (data directory: {AppPaths.DataDirectory})");

			Loc.Follow(Settings);
			StageManager.Native.ResourceWatch.Start();

			base.OnStartup(e);
		}

		protected override void OnExit(ExitEventArgs e)
		{
			Log.Info($"Exiting StageManager (exit code {e.ApplicationExitCode})");
			base.OnExit(e);
		}
	}
}
