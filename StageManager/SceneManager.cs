using AsyncAwaitBestPractices;
using StageManager.Strategies;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using workspacer;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using IWindow = workspacer.IWindow;

namespace StageManager
{
	public class SceneManager
	{
		private readonly Desktop _desktop;
		private List<Scene> _scenes;
		private Scene _current;
		private IntPtr _desktopHandle;
		private bool _suspend = false;
		private Guid? _reentrancyLockSceneId;

		public event EventHandler<SceneChangedEventArgs> SceneChanged;
		public event EventHandler<CurrentSceneSelectionChangedEventArgs> CurrentSceneSelectionChanged;
		public event EventHandler<IWindow> RequestWindowPreviewUpdate;

		private IWindowStrategy ShowStrategy { get; } = new WindowNormalizeStrategy();
		private IWindowStrategy HideStrategy { get; } = new WindowMinimizeStrategy();

		public WindowsManager WindowsManager { get; }

		public SceneManager(WindowsManager windowsManager)
		{
			WindowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
			_desktop = new Desktop();
		}

		public async Task Start()
		{
			if (Thread.CurrentThread.ManagedThreadId != 1)
				throw new NotSupportedException("Start has to be called on the main thread, otherwise events won't be fired.");

			WindowsManager.WindowCreated += WindowsManager_WindowCreated;
			WindowsManager.WindowUpdated += WindowsManager_WindowUpdated;
			WindowsManager.WindowDestroyed += WindowsManager_WindowDestroyed;
			WindowsManager.UntrackedFocus += WindowsManager_UntrackedFocus;

			await WindowsManager.Start();


		}

		private void WindowsManager_WindowUpdated(IWindow window, WindowUpdateType type)
		{
			if (_suspend)
				return;

			if (type == WindowUpdateType.Foreground)
			{
				SwitchToSceneByWindow(window).SafeFireAndForget();
				//var scene = FindSceneForWindow(window);

				//if (scene is null)
				//	_scenes.Add(new Scene(window.ProcessName, window));

				//SwitchTo(scene).SafeFireAndForget();
			}
		}

		private async void WindowsManager_UntrackedFocus(object? sender, IntPtr e)
		{
			//// TODO BETTER

			// Attention, recognizing the desktop handle to show/hide desktop icons
			// might interfere with situations when windows are picked from the taskbar
			// when there are more than one window to choose from.
			// in this case, it might be that untracked focus is fired which sets the 
			// scene to null and causes two scene changes forth and back to the prior scene

			//if (_desktopHandle == IntPtr.Zero)
			//{
			//	Win32.GetWindowThreadProcessId(e, out var processId);
			//	if (processId != 0)
			//	{
			//		var process = System.Diagnostics.Process.GetProcessById((int)processId);
			//		if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
			//		{
			//			_desktopHandle = e;
			//		}
			//	}
			//}

			//if (e == _desktopHandle)
			//{
			//	await SwitchTo(null);
			//}
		}

		private void WindowsManager_WindowDestroyed(IWindow window)
		{
			var scene = FindSceneForWindow(window);

			if (scene is not null)
			{
				scene.Remove(window);

				if (scene.Windows.Any())
				{
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
				}
				else
				{
					_scenes.Remove(scene);
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Removed));
				}
			}
		}

		public Scene FindSceneForWindow(IWindow window) => FindSceneForWindow(window.Handle);

		public Scene FindSceneForWindow(IntPtr handle) => _scenes?.FirstOrDefault(s => s.Windows.Any(w => w.Handle == handle));

		private Scene FindSceneForProcess(string processName) => _scenes.FirstOrDefault(s => string.Equals(s.Key, processName, StringComparison.OrdinalIgnoreCase));

		private async void WindowsManager_WindowCreated(IWindow window, bool firstCreate)
		{
			SwitchToSceneByNewWindow(window).SafeFireAndForget();
		}


		private async Task SwitchToSceneByWindow(IWindow window)
		{
			var scene = FindSceneForWindow(window);
			if (scene is null)
			{
				scene = new Scene(window.ProcessName, window);
				_scenes.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));
			}

			await SwitchTo(scene);
		}

		private async Task SwitchToSceneByNewWindow(IWindow window)
		{
			var existentScene = FindSceneForProcess(window.ProcessName);
			var scene = existentScene ?? new Scene(window.ProcessName, window);

			if (existentScene is null)
			{
				_scenes.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));
			}
			else
			{
				scene.Add(window);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
			}

			await SwitchTo(scene).ConfigureAwait(true);
		}

		/// <summary>
		/// Determines if a scene is switched back to shortly after it has been hidden.
		/// This can happen if an app activates one of it's windows after being hidde,
		/// like Microsoft Teams does if there's a small floating window for a current call.
		/// </summary>
		/// <param name="scene"></param>
		/// <returns></returns>
		private bool IsReentrancy(Scene? scene)
		{
			if (Guid.Equals(scene?.Id, _reentrancyLockSceneId))
				return true;

			if (_current is object)
			{
				_reentrancyLockSceneId = _current.Id;

				Task.Run(async () =>
				{
					await Task.Delay(1000).ConfigureAwait(false);
					_reentrancyLockSceneId = null;
				}).SafeFireAndForget();
			}

			return false;
		}

		public async Task SwitchTo(Scene? scene)
		{
			if (object.Equals(scene, _current))
				return;

			if (IsReentrancy(scene))
				return;

			try
			{
				_suspend = true;

				var otherWindows = GetSceneableWindows().Except(scene?.Windows ?? Array.Empty<IWindow>()).ToArray();

				var prior = _current;
				_current = scene;

				if (prior is object)
				{
					// screenshot the windows before hiding them
					foreach (var w in prior.Windows)
						RequestWindowPreviewUpdate?.Invoke(this, w);
				}

				foreach (var s in _scenes)
					s.IsSelected = s.Equals(scene);

				if (scene is object)
				{
					foreach (var w in scene.Windows)
						ShowStrategy.Invoke(w);
				}

				foreach (var o in otherWindows)
					HideStrategy.Invoke(o);

				CurrentSceneSelectionChanged?.Invoke(this, new CurrentSceneSelectionChangedEventArgs(prior, _current));

				if (scene is null)
					_desktop.ShowIcons();
				else
					_desktop.HideIcons();
			}
			finally
			{
				_suspend = false;
			}
		}

		public Task MoveWindow(Scene sourceScene, IWindow window, Scene targetScene)
		{
			try
			{
				_suspend = true;

				if (sourceScene is null || sourceScene.Equals(targetScene))
					return Task.CompletedTask;

				sourceScene.Remove(window);
				targetScene.Add(window);

				SceneChanged?.Invoke(this, new SceneChangedEventArgs(sourceScene, window, ChangeType.Updated));
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(targetScene, window, ChangeType.Updated));

				if (!sourceScene.Windows.Any())
				{
					_scenes.Remove(sourceScene);
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(sourceScene, window, ChangeType.Removed));
				}

				if (targetScene.Equals(_current))
				{
					ShowStrategy.Invoke(window);
					window.Focus();
				}
				else
				{
					HideStrategy.Invoke(window);

					// reset window position after move so that the window is back at the starting position on the new scene
					if (window is WindowsWindow w && w.PopLastLocation() is IWindowLocation l)
						workspacer.Win32.SetWindowPos(window.Handle, IntPtr.Zero, l.X, l.Y, 0, 0, workspacer.Win32.SetWindowPosFlags.IgnoreResize);
				}

				return Task.CompletedTask;
			}
			finally
			{
				_suspend = false;
			}
		}

		public async Task MoveWindow(IntPtr handle, Scene targetScene)
		{
			var source = FindSceneForWindow(handle);

			if (source is null || source.Equals(targetScene))
				return;

			var window = source.Windows.First(w => w.Handle == handle);
			await MoveWindow(source, window, targetScene);
		}

		public async Task PopWindowFrom(Scene sourceScene)
		{
			if (sourceScene is null || _current is null || sourceScene.Equals(_current))
				return;

			var window = sourceScene.Windows.LastOrDefault();

			if (window is object)
				await MoveWindow(sourceScene, window, _current).ConfigureAwait(false);
		}

		private IEnumerable<IWindow> GetSceneableWindows() => WindowsManager?.Windows?.Where(w => !string.IsNullOrEmpty(w.ProcessFileName) && !string.IsNullOrEmpty(w.Title));

		public IEnumerable<Scene> GetScenes()
		{
			if (_scenes is null)
			{
				_scenes = GetSceneableWindows()
							.GroupBy(w => w.ProcessName)
							.Select(group => new Scene(group.Key, group.ToArray()))
							.ToList();
			}

			return _scenes;
		}

		public IEnumerable<IWindow> GetCurrentWindows() => _current?.Windows ?? GetSceneableWindows();
	}
}
