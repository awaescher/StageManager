using AsyncAwaitBestPractices;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using StageManager.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace StageManager
{
	public partial class SceneManager
	{
		private readonly Desktop _desktop;
		private List<Scene> _scenes;
		private Scene _current;
		private bool _suspend = false;
		private const int REENTRANCY_LOCK_MS = 1000;
		private Guid? _reentrancyLockSceneId;
		private DateTime _reentrancyLockUntil;
		private Dispatcher _dispatcher;

		public event EventHandler<SceneChangedEventArgs> SceneChanged;
		public event EventHandler<CurrentSceneSelectionChangedEventArgs> CurrentSceneSelectionChanged;

		private IWindowStrategy WindowStrategy { get; } = new NormalizeAndMinimizeWindowStrategy(); // new WindowNormalizeStrategy/OpacityWindowStrategy/ShowAndHideWindowStrategy

		public WindowsManager WindowsManager { get; }

		public SceneManager(WindowsManager windowsManager)
		{
			WindowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
			_desktop = new Desktop();
			_desktop.HideIcons();
		}

		public async Task Start()
		{
			if (Thread.CurrentThread.ManagedThreadId != 1)
				throw new NotSupportedException("Start has to be called on the main thread, otherwise events won't be fired.");

			_dispatcher = Dispatcher.CurrentDispatcher;
			InitializeSnapGroups();

			// TEMPORARY: SafeFireAndForget swallows exceptions silently otherwise
			SafeFireAndForgetExtensions.SetDefaultExceptionHandling(ex => DebugLog.Write("EXCEPTION", ex.ToString()));
			AppDomain.CurrentDomain.UnhandledException += (_, e) => DebugLog.Write("EXCEPTION", "unhandled " + e.ExceptionObject);
			DebugLog.Write("START", $"log file {DebugLog.FilePath}");

			WindowsManager.WindowCreated += WindowsManager_WindowCreated;
			WindowsManager.WindowUpdated += WindowsManager_WindowUpdated;
			WindowsManager.WindowDestroyed += WindowsManager_WindowDestroyed;
			WindowsManager.UntrackedFocus += WindowsManager_UntrackedFocus;

			await WindowsManager.Start();
		}

		internal void Stop()
		{
			WindowsManager.Stop();

			foreach (var scene in _scenes)
			{
				foreach (var w in scene.Windows)
					WindowStrategy.Show(w);
			}

			_desktop.ShowIcons();
		}

		private void WindowsManager_WindowUpdated(IWindow window, WindowUpdateType type)
		{
			if (_suspend)
				return;

			switch (type)
			{
				case WindowUpdateType.Foreground:
					HandleForeground(window);
					break;
				case WindowUpdateType.MoveStart:
					HandleSnapGroupMoveStart(window);
					break;
				case WindowUpdateType.MoveEnd:
					HandleSnapGroupLocationChanged(window, isMoveEnd: true);
					break;
				case WindowUpdateType.LocationChanged:
					HandleSnapGroupLocationChanged(window, isMoveEnd: false);
					HandlePossibleSnapAssistPlacement(window);
					break;
				case WindowUpdateType.MinimizeEnd:
					HandlePossibleSnapAssistPlacement(window);
					break;
			}
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
			HandleWindowGone(window);
		}

		/// <summary>
		/// Removes a window that was destroyed or hidden by its application from its scene.
		/// </summary>
		private void HandleWindowGone(IWindow window)
		{
			var scene = FindSceneForWindow(window);
			if (scene is null)
				return;

			RemoveWindowFromScene(scene, window);

			// a detached window keeps its own scene only until it is closed
			_ownSceneWindows.Remove(window.Handle);

			if (scene.IsSnapGroup)
				DissolveSnapGroupIfTooSmall(scene, switchToRemainingWindow: true);
		}

		/// <summary>
		/// Removes a window from a scene, raises the change events and removes the scene if it became empty.
		/// </summary>
		private void RemoveWindowFromScene(Scene scene, IWindow window)
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

		private void AddWindowToScene(Scene scene, IWindow window)
		{
			scene.Add(window);
			SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
		}

		/// <summary>
		/// Windows that were detached from their process scene and keep their own scene until they are closed.
		/// </summary>
		private readonly HashSet<IntPtr> _ownSceneWindows = new HashSet<IntPtr>();

		/// <summary>
		/// Processes (by process name) whose windows get an own scene each instead of sharing one process scene,
		/// e.g. browsers. Prepared to be filled from the settings later.
		/// </summary>
		public ISet<string> ProcessesWithOwnScenePerWindow { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Determines whether a window gets a scene of its own instead of sharing the scene of its process.
		/// </summary>
		private bool WantsOwnScene(IWindow window) => _ownSceneWindows.Contains(window.Handle)
													|| ProcessesWithOwnScenePerWindow.Contains(GetWindowGroupKey(window));

		/// <summary>
		/// Adds a window to the scene it belongs to when it is not part of a snap group: usually the scene of its
		/// process, or a new scene of its own if the window was detached or its process is configured that way.
		/// </summary>
		private Scene AddWindowToHomeScene(IWindow window)
		{
			if (WantsOwnScene(window))
			{
				var ownScene = Scene.CreateSingleWindowScene(window);
				_scenes.Add(ownScene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(ownScene, window, ChangeType.Created));
				return ownScene;
			}

			var key = GetWindowGroupKey(window);
			var scene = FindSceneForProcess(key);

			if (scene is null)
			{
				scene = new Scene(key, window);
				_scenes.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));
			}
			else
			{
				AddWindowToScene(scene, window);
			}

			return scene;
		}

		public Scene FindSceneForWindow(IWindow window) => FindSceneForWindow(window.Handle);

		public Scene FindSceneForWindow(IntPtr handle) => _scenes?.FirstOrDefault(s => s.Windows.Any(w => w.Handle == handle));

		private Scene FindSceneForProcess(string processName) => _scenes.FirstOrDefault(s => s.IsProcessScene && string.Equals(s.Key, processName, StringComparison.OrdinalIgnoreCase));

		private async void WindowsManager_WindowCreated(IWindow window, bool firstCreate)
		{
			SwitchToSceneByNewWindow(window).SafeFireAndForget();
		}

		private async Task SwitchToSceneByWindow(IWindow window)
		{
			var scene = FindSceneForWindow(window) ?? AddWindowToHomeScene(window);

			await SwitchTo(scene);
		}

		private async Task SwitchToSceneByNewWindow(IWindow window)
		{
			var scene = FindSceneForWindow(window) ?? AddWindowToHomeScene(window);

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
			if (scene is null)
				return false;

			// Windows activates the next window in the z-order when the active window gets minimized.
			// This can be a (minimized) window of the scene that was just hidden, which must not switch back.
			if (Guid.Equals(scene.Id, _reentrancyLockSceneId) && DateTime.UtcNow < _reentrancyLockUntil)
				return true;

			if (_current is object)
			{
				// a timestamp instead of a delayed reset, so that an older reset cannot release a newer lock
				_reentrancyLockSceneId = _current.Id;
				_reentrancyLockUntil = DateTime.UtcNow.AddMilliseconds(REENTRANCY_LOCK_MS);
			}

			return false;
		}

		public async Task SwitchTo(Scene? scene)
		{
			if (object.Equals(scene, _current))
				return;

			if (IsReentrancy(scene))
			{
				DebugLog.Write("SWITCH", $"skipped (reentrancy) to {DescribeScene(scene)}");
				return;
			}

			var stopwatch = System.Diagnostics.Stopwatch.StartNew();
			DebugLog.Write("SWITCH", $"{DescribeScene(_current)} -> {DescribeScene(scene)}");

			try
			{
				_suspend = true;

				if (scene is object)
				{
					PruneGoneWindows(scene);

					// the scene might have been removed because all of its windows were gone
					if (!_scenes.Contains(scene))
						return;
				}

				var otherWindows = GetSceneableWindows().Except(scene?.Windows ?? Array.Empty<IWindow>()).ToArray();

				var prior = _current;

				// remember the exact layout of a snap group before its windows get minimized
				if (prior is { IsSnapGroup: true })
					prior.CaptureLayout();

				_current = scene;

				foreach (var s in _scenes)
					s.IsSelected = s.Equals(scene);

				if (scene is object)
				{
					var restoredFromMinimized = scene.Windows.Where(w => Win32.IsIconic(w.Handle)).Select(w => w.Handle).ToHashSet();

					foreach (var w in scene.Windows)
						WindowStrategy.Show(w);

					if (scene.IsSnapGroup)
						RestoreSnapLayout(scene, restoredFromMinimized);
				}

				foreach (var o in otherWindows)
					WindowStrategy.Hide(o);

				CurrentSceneSelectionChanged?.Invoke(this, new CurrentSceneSelectionChangedEventArgs(prior, _current));

				if (scene is null)
					_desktop.ShowIcons();
				else
					_desktop.HideIcons();
			}
			finally
			{
				_suspend = false;
				DebugLog.Write("SWITCH", $"done in {stopwatch.ElapsedMilliseconds} ms");
			}
		}

		/// <summary>
		/// Removes windows from a scene that were destroyed or hidden by their application
		/// without the corresponding window event being received.
		/// </summary>
		private void PruneGoneWindows(Scene scene)
		{
			var gone = scene.Windows.OfType<WindowsWindow>().Where(w => !w.IsVisibleOrMinimized).ToArray();

			foreach (var window in gone)
			{
				DebugLog.Write("PRUNE", $"{DebugLog.Describe(window.Handle)} from {DescribeScene(scene)}");
				// raises WindowDestroyed if the window is still tracked
				WindowsManager.Forget(window.Handle);

				if (FindSceneForWindow(window) is object)
					HandleWindowGone(window);
			}
		}

		public Task MoveWindow(Scene sourceScene, IWindow window, Scene targetScene)
		{
			try
			{
				_suspend = true;

				if (sourceScene is null || sourceScene.Equals(targetScene))
					return Task.CompletedTask;

				DebugLog.Write("MOVE", $"{DebugLog.Describe(window.Handle)} from {DescribeScene(sourceScene)} to {DescribeScene(targetScene)}");
				RemoveWindowFromScene(sourceScene, window);
				AddWindowToScene(targetScene, window);

				if (sourceScene.IsSnapGroup)
					DissolveSnapGroupIfTooSmall(sourceScene, switchToRemainingWindow: false);

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
			DebugLog.Write("PULL", $"pop {(window is null ? "<none>" : DebugLog.Describe(window.Handle))} from {DescribeScene(sourceScene)} into {DescribeScene(_current)}");

			if (window is object)
				await MoveWindow(sourceScene, window, _current).ConfigureAwait(false);
		}

		/// <summary>
		/// Takes a window out of its scene and puts it back into the scene of its process.
		/// A snap group with less than two remaining windows is dissolved.
		/// </summary>
		public async Task DetachWindow(IWindow window)
		{
			var scene = FindSceneForWindow(window);
			if (scene is null)
				return;

			if (scene.Kind == SceneKind.SingleWindow)
			{
				DebugLog.Write("DETACH", $"{DebugLog.Describe(window.Handle)} already has its own scene, nothing to do");
				return;
			}

			if (scene.IsProcessScene && string.Equals(scene.Key, GetWindowGroupKey(window), StringComparison.OrdinalIgnoreCase))
			{
				if (scene.Windows.Count() < 2)
				{
					DebugLog.Write("DETACH", $"{DebugLog.Describe(window.Handle)} is the only window of its process scene, nothing to do");
					return;
				}

				// detached from its own process scene: the window gets a scene of its own until it is closed
				_ownSceneWindows.Add(window.Handle);
			}

			DebugLog.Write("DETACH", $"{DebugLog.Describe(window.Handle)} from {DescribeScene(scene)}");

			var remaining = scene.Windows.Where(w => w.Handle != window.Handle).ToArray();
			Scene target;

			try
			{
				_suspend = true;

				RemoveWindowFromScene(scene, window);
				target = AddWindowToHomeScene(window);

				if (scene.IsSnapGroup)
					DissolveSnapGroupIfTooSmall(scene, switchToRemainingWindow: false);
			}
			finally
			{
				_suspend = false;
			}

			if (_current is object && !_scenes.Contains(_current))
			{
				// the current scene was dissolved, continue with the scene of the remaining windows
				var next = remaining.Select(FindSceneForWindow).FirstOrDefault(s => s is object);
				await SwitchTo(next);
			}
			else if (!target.Equals(_current))
			{
				WindowStrategy.Hide(window);
			}
		}

		private IEnumerable<IWindow> GetSceneableWindows() => WindowsManager?.Windows?.Where(w => !string.IsNullOrEmpty(w.ProcessFileName)
																								&& !string.IsNullOrEmpty(w.Title)
																								&& (w is not WindowsWindow ww || ww.IsVisibleOrMinimized));

		public IEnumerable<Scene> GetScenes()
		{
			if (_scenes is null)
			{
				var windows = GetSceneableWindows().ToArray();

				_scenes = windows
							.Where(w => !WantsOwnScene(w))
							.GroupBy(GetWindowGroupKey)
							.Select(group => new Scene(group.Key, group.ToArray()))
							.Concat(windows.Where(WantsOwnScene).Select(Scene.CreateSingleWindowScene))
							.ToList();
			}

			return _scenes;
		}

		public IEnumerable<IWindow> GetCurrentWindows() => _current?.Windows ?? GetSceneableWindows();

		private string GetWindowGroupKey(IWindow window) => window.ProcessName;

		// TEMPORARY: logging helper
		private static string DescribeScene(Scene? scene)
		{
			if (scene is null)
				return "<desktop>";

			var kind = scene.Kind switch
			{
				SceneKind.SnapGroup => "group",
				SceneKind.SingleWindow => "single",
				_ => scene.Key
			};
			return $"{kind}({string.Join(", ", scene.Windows.Select(w => DebugLog.Describe(w.Handle)))})";
		}
	}
}
