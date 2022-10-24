using NLog.Filters;
using NLog.LayoutRenderers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Text;
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

		public event EventHandler<SceneChangeEventArgs> SceneChanged;
		public event EventHandler<IWindow> RequestWindowPreviewUpdate;

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

		private void WindowsManager_UntrackedFocus(object? sender, IntPtr e)
		{
			//if (e == Desktop.GetDesktopSHELLDLL_DefView())
			//	_desktop.ShowIcons();
		}

		private void WindowsManager_WindowDestroyed(IWindow window)
		{
			var existentScene = FindSceneForWindow(window);
			var scene = existentScene ?? new Scene(window.ProcessName, window);

			if (existentScene is not null)
			{
				scene.Remove(window);

				if (scene.Windows.Any())
				{
					SceneChanged?.Invoke(this, new SceneChangeEventArgs(scene, window, ChangeType.Updated));
				}
				else
				{
					_scenes.Remove(scene);
					SceneChanged?.Invoke(this, new SceneChangeEventArgs(scene, window, ChangeType.Removed));
				}
			}
		}

		public Scene FindSceneForWindow(IWindow window) => FindSceneForWindow(window.Handle);

		public Scene FindSceneForWindow(IntPtr handle) => _scenes?.FirstOrDefault(s => s.Windows.Any(w => w.Handle == handle));

		private void WindowsManager_WindowCreated(IWindow window, bool firstCreate)
		{
			var existentScene = _current ?? _scenes.FirstOrDefault(s => s.Key == window.ProcessName);
			var scene = existentScene ?? new Scene(window.ProcessName, window);

			if (existentScene is null)
			{
				_scenes.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangeEventArgs(scene, window, ChangeType.Created));
			}
			else
			{
				scene.Add(window);
				SceneChanged?.Invoke(this, new SceneChangeEventArgs(scene, window, ChangeType.Updated));
			}
		}

		private void WindowsManager_WindowUpdated(IWindow window, WindowUpdateType type)
		{
			if (type == WindowUpdateType.Show || type == WindowUpdateType.Foreground)
				RequestWindowPreviewUpdate?.Invoke(this, window);
		}

		public async Task SwitchTo(Scene scene)
		{
			var otherWindows = GetSceneableWindows().Except(scene.Windows).ToArray();

			_current = scene;

			foreach (var s in _scenes)
				s.IsSelected = s.Equals(scene);

			foreach (var w in scene.Windows)
				w.ShowInCurrentState();

			foreach (var o in otherWindows)
				o.Hide();

			await Dump(otherWindows.Select(w => w.Handle));

			_desktop.HideIcons();
		}

		public Task MoveWindow(Scene sourceScene, IWindow window, Scene targetScene)
		{
			if (sourceScene is null || sourceScene.Equals(targetScene))
				return Task.CompletedTask;

			sourceScene.Remove(window);
			targetScene.Add(window);

			SceneChanged?.Invoke(this, new SceneChangeEventArgs(sourceScene, window, ChangeType.Updated));
			SceneChanged?.Invoke(this, new SceneChangeEventArgs(targetScene, window, ChangeType.Updated));

			if (!sourceScene.Windows.Any())
			{
				_scenes.Remove(sourceScene);
				SceneChanged?.Invoke(this, new SceneChangeEventArgs(sourceScene, window, ChangeType.Removed));
			}

			if (targetScene.Equals(_current))
			{
				window.Focus();
			}
			else
			{
				window.Hide();

				// reset window position after move so that the window is back at the starting position on the new scene
				if (window is WindowsWindow w && w.PopLastLocation() is IWindowLocation l)
					workspacer.Win32.SetWindowPos(window.Handle, IntPtr.Zero, l.X, l.Y, 0, 0, workspacer.Win32.SetWindowPosFlags.IgnoreResize);
			}

			return Task.CompletedTask;
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

		public async Task Dump(IEnumerable<IntPtr> hiddenHandles)
		{
			await File.WriteAllTextAsync(@"C:\temp\stager.hidden", string.Join(',', hiddenHandles));
		}

		public async Task Reset()
		{
			var raw = await File.ReadAllTextAsync(@"C:\temp\stager.hidden");
			if (!string.IsNullOrEmpty(raw))
			{
				var hiddenWindows = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
					.Select(i => new WindowsWindow(IntPtr.Parse(i)))
					.ToArray();

				foreach (var w in hiddenWindows)
					w.ShowInCurrentState();

				await File.WriteAllTextAsync(@"C:\temp\stager.hidden", "");
			}

			_desktop.ShowIcons();
		}

		private IEnumerable<IWindow> GetSceneableWindows() => WindowsManager.Windows.Where(w => !w.IsMinimized && !string.IsNullOrEmpty(w.Title));

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
