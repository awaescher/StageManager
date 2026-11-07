using AsyncAwaitBestPractices;
using StageManager.Core;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using StageManager.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StageManager
{
	public class SceneManager
	{
		private readonly Desktop _desktop;
		private readonly DesktopIconsPolicy _desktopIcons;
		private readonly SceneRegistry _registry = new();
		private readonly Dictionary<Guid, Scene> _sceneById = new();
		private List<Scene> _scenes;
		private Scene _current;
		private bool _suspend = false;

		public event EventHandler<SceneChangedEventArgs> SceneChanged;
		public event EventHandler<CurrentSceneSelectionChangedEventArgs> CurrentSceneSelectionChanged;

		private IWindowStrategy WindowStrategy { get; } = new NormalizeAndMinimizeWindowStrategy(); // new WindowNormalizeStrategy/OpacityWindowStrategy/ShowAndHideWindowStrategy

		public WindowsManager WindowsManager { get; }

		public SceneManager(WindowsManager windowsManager)
		{
			WindowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
			_desktop = new Desktop();
			_desktopIcons = new DesktopIconsPolicy(
				_desktop,
				new FileDesktopIconsState(System.IO.Path.Combine(AppPaths.DataDirectory, "desktop-icons.state")),
				() => App.Settings.Current.DesktopIcons);
			_desktopIcons.Start();
			_desktopIcons.Apply(sceneActive: true);
		}

		/// <summary>Applies a changed desktop icon setting right away.</summary>
		public void ApplyDesktopIconSettings() => _desktopIcons.Reapply();

		/// <summary>The Explorer was restarted and may show the icons again.</summary>
		public void ResyncDesktopIcons() => _desktopIcons.Resync();

		public async Task Start()
		{
			// the WinEvent hooks only fire on a thread with a message loop, that is the UI thread (its managed id is not guaranteed to be 1)
			if (System.Windows.Application.Current?.Dispatcher.CheckAccess() != true)
				throw new NotSupportedException("Start has to be called on the main thread, otherwise events won't be fired.");

			WindowsManager.WindowCreated += WindowsManager_WindowCreated;
			WindowsManager.WindowUpdated += WindowsManager_WindowUpdated;
			WindowsManager.WindowDestroyed += WindowsManager_WindowDestroyed;
			WindowsManager.UntrackedFocus += WindowsManager_UntrackedFocus;

			await WindowsManager.Start();
		}

		internal void Stop()
		{
			WindowsManager.Stop();

			foreach (var scene in _scenes ?? new List<Scene>())
			{
				foreach (var w in scene.Windows)
					WindowStrategy.Show(w);
			}

			_desktopIcons.Stop();
		}

		private void WindowsManager_WindowUpdated(IWindow window, WindowUpdateType type)
		{
			if (_suspend)
				return;

			if (type == WindowUpdateType.Foreground)
				SwitchToSceneByWindow(window).SafeFireAndForget();
		}

		private void WindowsManager_UntrackedFocus(object? sender, IntPtr e)
		{
			if (_suspend)
				return;

			if (!_desktop.HasDesktopView)
				_desktop.TrySetDesktopView(e);

			if (_desktop.HasDesktopView && _desktop.DesktopViewHandle == e)
				SwitchTo(null).SafeFireAndForget();
		}

		private void WindowsManager_WindowDestroyed(IWindow window)
		{
			var removal = _registry.Remove(window.Handle.ToInt64());
			if (removal.GroupId is not Guid id || !_sceneById.TryGetValue(id, out var scene))
				return;

			scene.Remove(window);

			if (!removal.GroupRemoved)
			{
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
			}
			else
			{
				_scenes.Remove(scene);
				_sceneById.Remove(id);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Removed));
			}
		}

		public Scene FindSceneForWindow(IWindow window) => FindSceneForWindow(window.Handle);

		public Scene FindSceneForWindow(IntPtr handle)
		{
			var id = _registry.FindGroupId(handle.ToInt64());
			return id is Guid g && _sceneById.TryGetValue(g, out var scene) ? scene : null;
		}

		private async void WindowsManager_WindowCreated(IWindow window, bool firstCreate)
		{
			SwitchToSceneByNewWindow(window).SafeFireAndForget();
		}

		private async Task SwitchToSceneByWindow(IWindow window)
		{
			var scene = FindSceneForWindow(window);
			if (scene is null)
				scene = PlaceWindow(window, companion: false).Scene;

			await SwitchTo(scene);
		}

		private async Task SwitchToSceneByNewWindow(IWindow window)
		{
			var placed = PlaceWindow(window, IsCompanion(window));

			// a companion joins the current scene, nothing has to be hidden or shown for it
			if (!placed.Switch)
			{
				WindowStrategy.Show(window);
				return;
			}

			await SwitchTo(placed.Scene).ConfigureAwait(true);
		}

		private (Scene Scene, bool Switch) PlaceWindow(IWindow window, bool companion)
		{
			EnsureScenes();

			var placement = _registry.Place(window.Handle.ToInt64(), GetWindowGroupKey(window), companion);

			if (placement.Created)
			{
				var created = new Scene(placement.GroupId, GetWindowGroupKey(window), window);
				_scenes.Add(created);
				_sceneById[placement.GroupId] = created;
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(created, window, ChangeType.Created));
				return (created, placement.Switch);
			}

			var scene = _sceneById[placement.GroupId];
			if (!scene.Windows.Contains(window))
			{
				scene.Add(window);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
			}

			return (scene, placement.Switch);
		}

		private static bool IsCompanion(IWindow window) =>
			window is WindowsWindow w && AppIdentity.IsListed(App.Settings.Current.Stage.Companions, w.ProcessExecutable);

		public async Task SwitchTo(Scene? scene)
		{
			if (!_registry.TrySwitch(scene?.Id))
				return;

			try
			{
				_suspend = true;

				var otherWindows = GetSceneableWindows().Except(scene?.Windows ?? Array.Empty<IWindow>()).ToArray();

				var prior = _current;
				_current = scene;

				foreach (var s in _scenes)
					s.IsSelected = s.Equals(scene);

				if (scene is object)
				{
					foreach (var w in scene.Windows)
						WindowStrategy.Show(w);
				}

				foreach (var o in otherWindows)
					WindowStrategy.Hide(o);

				CurrentSceneSelectionChanged?.Invoke(this, new CurrentSceneSelectionChangedEventArgs(prior, _current));

				_desktopIcons.Apply(sceneActive: scene is object);
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

				var moved = _registry.Move(window.Handle.ToInt64(), targetScene.Id);
				if (moved is null)
					return Task.CompletedTask;

				sourceScene.Remove(window);
				targetScene.Add(window);

				SceneChanged?.Invoke(this, new SceneChangedEventArgs(sourceScene, window, ChangeType.Updated));
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(targetScene, window, ChangeType.Updated));

				if (moved.Value.SourceRemoved)
				{
					_scenes.Remove(sourceScene);
					_sceneById.Remove(sourceScene.Id);
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(sourceScene, window, ChangeType.Removed));
				}

				if (targetScene.Equals(_current))
				{
					WindowStrategy.Show(window);
					window.Focus();
				}
				else
				{
					WindowStrategy.Hide(window);

					// reset window position after move so that the window is back at the starting position on the new scene
					if (window is WindowsWindow w && w.PopLastLocation() is IWindowLocation l)
						Win32.SetWindowPos(window.Handle, IntPtr.Zero, l.X, l.Y, 0, 0, Win32.SetWindowPosFlags.IgnoreResize);
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
			EnsureScenes();
			return _scenes;
		}

		private void EnsureScenes()
		{
			if (_scenes is not null)
				return;

			var windows = (GetSceneableWindows() ?? Enumerable.Empty<IWindow>()).ToList();
			var byHandle = windows.ToDictionary(w => w.Handle.ToInt64());

			var snapshots = _registry.Initialize(windows.Select(w => (w.Handle.ToInt64(), GetWindowGroupKey(w))));

			_scenes = new List<Scene>();
			foreach (var group in snapshots)
			{
				var scene = new Scene(group.Id, group.Key, group.Windows.Select(h => byHandle[h]).ToArray());
				_scenes.Add(scene);
				_sceneById[group.Id] = scene;
			}
		}

		public IEnumerable<IWindow> GetCurrentWindows() => _current?.Windows ?? GetSceneableWindows();

		private static string GetWindowGroupKey(IWindow window) =>
			string.IsNullOrEmpty(window.AppKey) ? window.ProcessName.ToUpperInvariant() : window.AppKey;
	}
}
