using AsyncAwaitBestPractices;
using StageManager.Core;
using StageManager.Native;
using StageManager.Native.Interop;
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

		private IWindowStrategy WindowStrategy { get; } = new ScreenOffsetWindowStrategy(); // alternatives: NormalizeAndMinimizeWindowStrategy/OpacityWindowStrategy/ShowAndHideWindowStrategy

		public WindowsManager WindowsManager { get; }

		/// <summary>The scene on the stage. Null in the desktop view.</summary>
		public Scene Current => _current;

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

		// the list of excluded apps the windows were sorted by the last time
		private IReadOnlyList<string> _knownExcluded = Array.Empty<string>();

		/// <summary>
		/// Applies a changed list of excluded apps right away: the windows of an app that is excluded now leave their cards and come back
		/// to the screen if they were hidden, the windows of an app that is not excluded any more get a card.
		/// </summary>
		public void ApplyExcludedApps()
		{
			var current = App.Settings.Current.Stage.ExcludedApps.ToList();
			var previous = _knownExcluded;

			if (previous.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
				return;

			_knownExcluded = current;

			if (_scenes is null)
				return;

			var (removed, added) = WindowsManager.Reevaluate(previous);

			foreach (var window in removed)
			{
				// a hidden window is out of the screen, nobody would bring it back once it is forgotten
				if (Parking.IsParked(window.Handle))
					WindowStrategy.Show(window);

				WindowsManager_WindowDestroyed(window);
			}

			Scene? last = null;
			foreach (var window in added)
			{
				var placed = PlaceWindow(window, companion: false);
				if (placed.Switch)
					last = placed.Scene;
			}

			// it is on the screen already, its scene takes the stage like a scene of a window that was just opened
			if (last is object)
				SwitchTo(last).SafeFireAndForget(ex => Log.Error("Taking an app back in failed", ex));
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

			_knownExcluded = App.Settings.Current.Stage.ExcludedApps.ToList();

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

			// whatever the scenes know about, nothing may stay out of the screen
			Parking.RestoreAll();
			WindowRescue.Run();

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

			// a click on the wallpaper gives the focus to the desktop: like on macOS the windows step aside and the desktop shows
			var className = new System.Text.StringBuilder(256);
			Win32.GetClassName(e, className, className.Capacity);

			if (DesktopView.IsDesktopClass(className.ToString()))
				SwitchTo(null).SafeFireAndForget(ex => Log.Error("Showing the desktop failed", ex));
		}

		private void WindowsManager_WindowDestroyed(IWindow window)
		{
			// a window that only hides or cloaks itself (Windows Terminal does) still exists. StageManager forgets it, so it must not
			// stay out of the screen: once it shows again nobody would bring it back
			if (Win32.IsWindow(window.Handle))
				Parking.Unpark(window.Handle);
			else
				Parking.Forget(window.Handle);

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

			// the window that was activated is in front: on the card, while the others fly in and once they landed.
			// On the stage the order is read from the screen when the scene leaves it
			if (!ReferenceEquals(scene, _current))
				Restack(scene, ZOrder.ToFront(HandlesOf(scene), new[] { window.Handle.ToInt64() }));

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

			var placement = _registry.Place(window.Handle.ToInt64(), GroupKeyOf(window), companion);

			if (placement.Created)
			{
				var created = new Scene(placement.GroupId, GroupKeyOf(window), window);
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

		/// <summary>Screen rectangle of the thumbnail area of a card in physical pixels, null if the card is not on screen. Set by the sidebar.</summary>
		public Func<Scene, Area?>? CardAreaOf { get; set; }

		/// <summary>How tall the card of a scene is in physical pixels. Cards are as tall as their windows. Set by the sidebar.</summary>
		public Func<Scene, double>? CardHeightOf { get; set; }

		/// <summary>Physical pixels per unit of the sidebar layout. Set by the sidebar.</summary>
		public double CardScale { get; set; } = 1;

		/// <summary>The sidebar window. Flying windows pass below it, a window that flies to the stage must not cover it. Set by the sidebar.</summary>
		public IntPtr SidebarHandle { get; set; }

		/// <summary>The windows of a scene that left the stage have arrived on its card, the card can show them now.</summary>
		public event EventHandler<Scene>? CardLanded;

		private Task _flight = Task.CompletedTask;
		private FlightOverlay? _overlay;

		/// <summary>The desktop is shown: a click on the wallpaper deselected the scene. False at start and as soon as a scene is selected.</summary>
		public bool IsDesktopView { get; private set; }

		/// <summary>Windows are flying on an overlay at the moment.</summary>
		public bool IsFlying => _overlay is not null;

		/// <summary>
		/// A switch is under way: the windows of the new scene are not on the stage yet, the ones of the old scene may be still. What is
		/// on the screen now says nothing about what will be there.
		/// </summary>
		public bool IsSwitching => _suspend || _overlay is not null;

		/// <param name="animate">False if the windows have been shown flying already, e.g. by dragging their card onto the stage.</param>
		public async Task SwitchTo(Scene? scene, bool animate = true)
		{
			// a switch that is still waiting for its overlay to show has to start flying first
			await _takeoff.ConfigureAwait(true);

			// a switch that comes in while windows still fly lets them land first
			if (_overlay is not null)
			{
				_overlay.Skip();
				await _flight.ConfigureAwait(true);
			}

			if (!_registry.TrySwitch(scene?.Id))
				return;

			IsDesktopView = scene is null;

			var takeoff = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			try
			{
				_suspend = true;
				_takeoff = takeoff.Task;

				var otherWindows = GetSceneableWindows().Except(scene?.Windows ?? Array.Empty<IWindow>()).ToArray();

				var prior = _current;

				// the scene that leaves keeps the order its windows lie on the screen, its card and its next return show them that way
				if (prior is object)
					KeepScreenOrder(prior);
				_current = scene;

				foreach (var s in _scenes)
					s.IsSelected = s.Equals(scene);

				var animated = animate ? PrepareFlight(prior, scene, otherWindows) : null;

				// nothing may vanish before its flying copy is on the screen: not the windows, not the card in the sidebar
				if (animated is not null)
					await animated.Overlay.WhenOnScreen().ConfigureAwait(true);

				// with an animation the windows of the scene show up when the flight lands, not before it takes off
				if (scene is object && animated is null)
					ShowInOrder(scene.Windows);

				foreach (var o in otherWindows)
					WindowStrategy.Hide(o);

				CurrentSceneSelectionChanged?.Invoke(this, new CurrentSceneSelectionChangedEventArgs(prior, _current, animated?.PriorCardWaits == true));

				_desktopIcons.Apply(sceneActive: scene is object);

				if (animated is not null)
					await RunFlight(animated, scene!, prior, takeoff).ConfigureAwait(true);
			}
			finally
			{
				takeoff.TrySetResult();
				_suspend = false;
			}
		}

		private Task _takeoff = Task.CompletedTask;

		private sealed record PreparedFlight(FlightOverlay Overlay, IReadOnlyList<IntPtr> Windows, bool PriorCardWaits);

		/// <summary>
		/// Puts the windows of the scenes on a transparent overlay at the position they have now. Must run before the real windows
		/// are shown or hidden, because DWM keeps showing the picture of a window it takes a thumbnail of even after the window is minimized.
		/// </summary>
		private PreparedFlight? PrepareFlight(Scene? prior, Scene? scene, IWindow[] otherWindows)
		{
			if (scene is null)
				return null;

			var style = SystemAnimation.CurrentStyle();
			if (style == FlightStyle.None)
				return null;

			var card = CardAreaOf?.Invoke(scene);
			if (card is not Area cardArea || cardArea.Width < 8 || cardArea.Height < 8)
				return null;

			var monitor = WindowGeometry.BoundsOf(cardArea);
			if (!FlightPlanner.Intersects(monitor, cardArea))
				return null;

			var overlay = new FlightOverlay(monitor, style, SidebarHandle);
			var involved = new List<IntPtr>();
			var priorCardWaits = false;
			var maxVisible = App.Settings.Current.Sidebar.MaxWindowsPerScene;

			try
			{
				overlay.Show();

				// the card of the prior scene takes the place of the card of the new scene, both stacks are laid out on that place
				// exactly like the sidebar draws them, so the flying windows and the card match when one takes over from the other
				var priorWindows = (prior?.Windows ?? Enumerable.Empty<IWindow>()).ToArray();
				var alignment = CardAlignment.For(App.Settings.Current.Sidebar.Side);
				// the card of the scene that leaves may be taller or lower than the one that comes. The stack of the sidebar is centered, so the
				// new card ends up around the same center
				var priorArea = cardArea;
				if (prior is object && CardHeightOf?.Invoke(prior) is double priorHeight && priorHeight > 0)
				{
					var middle = (cardArea.Top + cardArea.Bottom) / 2;
					priorArea = new Area(cardArea.Left, middle - priorHeight / 2, cardArea.Right, middle + priorHeight / 2);
				}

				var priorStack = DragPreviewLayout.OnStack(priorArea, priorWindows.Select(w => WindowGeometry.FrameSizeOf(w.Handle)).ToArray(), maxVisible, CardScale, alignment);

				// the windows that leave fly where they stand to the card, the ones that come fly from the card to where they will show
				for (var i = 0; i < priorWindows.Length; i++)
				{
					var w = priorWindows[i];
					if (!otherWindows.Contains(w))
						continue;

					if (!Win32.IsWindowVisible(w.Handle) || Win32.IsIconic(w.Handle) || Parking.IsParked(w.Handle))
						continue;

					if (WindowGeometry.FrameOf(w.Handle) is not Area frame || !FlightPlanner.Intersects(monitor, frame))
						continue;

					// a window that is not shown on the card fades out on the way
					if (overlay.Add(w.Handle, FlightPlanner.OfFrame(frame), priorStack[i]))
					{
						involved.Add(w.Handle);
						priorCardWaits = true;
					}
				}

				var windows = scene.Windows.ToArray();
				var stack = DragPreviewLayout.OnStack(cardArea, windows.Select(w => WindowGeometry.FrameSizeOf(w.Handle)).ToArray(), maxVisible, CardScale, alignment);

				for (var i = 0; i < windows.Length; i++)
				{
					var w = windows[i];

					// a window that is on screen already (activated from the taskbar or with Alt+Tab) has arrived, it must not fly there once more
					if (Win32.IsWindowVisible(w.Handle) && !Win32.IsIconic(w.Handle) && !Parking.IsParked(w.Handle))
						continue;

					var frame = Parking.OriginalFrameOf(w.Handle) ?? WindowGeometry.FrameOf(w.Handle);
					if (frame is not Area target || !FlightPlanner.Intersects(monitor, target))
						continue;

					// a window that is not shown on the card fades in on the way
					if (overlay.Add(w.Handle, stack[i], FlightPlanner.OfFrame(target)))
						involved.Add(w.Handle);
				}
			}
			catch (Exception ex)
			{
				Log.Error("The animation could not be prepared", ex);
				overlay.Finish();
				return null;
			}

			if (!overlay.HasFlights)
			{
				overlay.Finish();
				return null;
			}

			// the animation Windows plays when a window is shown or minimized would run below ours
			foreach (var h in involved.Union(scene.Windows.Select(w => w.Handle)).Union(otherWindows.Select(w => w.Handle)))
				NativeMethods.SetTransitionsDisabled(h, true);

			_overlay = overlay;
			return new PreparedFlight(overlay, involved, priorCardWaits);
		}

		/// <summary>How long the real windows have to paint themselves under the overlay before it goes. Both show the same picture, so the overlap cannot be seen.</summary>
		private static readonly TimeSpan LandingDelay = TimeSpan.FromMilliseconds(90);

		private async Task RunFlight(PreparedFlight flight, Scene scene, Scene? prior, TaskCompletionSource takeoff)
		{
			var duration = FlightMotion.Duration(App.Settings.Current.Animations.Speed);
			var landed = false;
			var cardLanded = false;

			void Land()
			{
				// the card shows its windows the moment their flying copies arrive on it, both lie exactly on top of each other
				if (!cardLanded && flight.PriorCardWaits && prior is object)
				{
					cardLanded = true;
					CardLanded?.Invoke(this, prior);
				}

				// a newer switch may have taken over while this flight was still in the air: then these windows stay hidden
				if (landed || !ReferenceEquals(_current, scene))
					return;

				landed = true;
				ShowInOrder(scene.Windows);
			}

			try
			{
				_flight = flight.Overlay.Fly(duration);
				takeoff.TrySetResult();

				// never leave the overlay on the screen, whatever happens to the flight
				await Task.WhenAny(_flight, Task.Delay(duration + TimeSpan.FromMilliseconds(1500))).ConfigureAwait(true);

				Land();

				// the real windows and the card are drawn under the overlay before it goes, both show the same picture
				NativeMethods.DwmFlush();
				await Task.Delay(LandingDelay).ConfigureAwait(true);
				NativeMethods.DwmFlush();
			}
			catch (Exception ex)
			{
				Log.Error("The animation failed", ex);
			}
			finally
			{
				// the windows of the scene have to show up even if the animation failed
				Land();

				try
				{
					flight.Overlay.Finish();
				}
				catch (Exception ex)
				{
					Log.Error("The animation overlay could not be closed", ex);
				}

				// only forget the flight if no newer switch has replaced it in the meantime
				if (ReferenceEquals(_overlay, flight.Overlay))
				{
					_overlay = null;
					_flight = Task.CompletedTask;
				}

				// the windows animate on their own again when the user minimizes them
				foreach (var h in GetSceneableWindows().Select(w => w.Handle))
					NativeMethods.SetTransitionsDisabled(h, false);
			}
		}

		private static IReadOnlyList<long> HandlesOf(Scene scene) => scene.Windows.Select(w => w.Handle.ToInt64()).ToList();

		/// <summary>Gives the windows of a scene a new order, back to front. The card follows it.</summary>
		private void Restack(Scene scene, IReadOnlyList<long> backToFront)
		{
			if (scene.Reorder(backToFront.Select(h => new IntPtr(h)).ToList()))
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, scene.Windows.LastOrDefault(), ChangeType.Updated));
		}

		/// <summary>Takes the order in which the windows of a scene lie on the screen now. Windows that are not on the screen keep their place.</summary>
		private void KeepScreenOrder(Scene scene)
		{
			var onScreen = scene.Windows
				.Select(w => w.Handle)
				.Where(h => Win32.IsWindowVisible(h) && !Win32.IsIconic(h) && !Parking.IsParked(h))
				.Select(h => h.ToInt64())
				.ToHashSet();

			if (onScreen.Count < 2)
				return;

			var topToBottom = WindowStack.TopToBottom().Where(onScreen.Contains).ToList();
			Restack(scene, ZOrder.FromScreen(HandlesOf(scene), topToBottom));
		}

		/// <summary>Shows windows and lays them on top of everything else in their order, the last one in front, as the card and the flight showed them.</summary>
		private void ShowInOrder(IEnumerable<IWindow> backToFront)
		{
			var windows = backToFront.ToList();

			foreach (var w in windows)
				WindowStrategy.Show(w);

			WindowStack.Restack(windows.Select(w => w.Handle));
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
					// back to the place the window was dragged from, before it is hidden: a hidden window is out of the screen
					if (window is WindowsWindow w && w.PopLastLocation() is IWindowLocation l)
						Win32.SetWindowPos(window.Handle, IntPtr.Zero, l.X, l.Y, 0, 0, Win32.SetWindowPosFlags.IgnoreResize);

					WindowStrategy.Hide(window);
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

		/// <summary>
		/// Adds all windows and apps of a scene to the scene on the stage. They stay together as one scene from now on.
		/// In the desktop view there is no stage, then the scene is just selected.
		/// </summary>
		public Task JoinToCurrent(Scene source, bool animate = true)
		{
			if (source is null || source.Equals(_current))
				return Task.CompletedTask;

			if (_current is null)
				return SwitchTo(source, animate);

			try
			{
				_suspend = true;

				var target = _current;
				if (_registry.Merge(source.Id, target.Id) is null)
					return Task.CompletedTask;

				var moving = source.Windows.ToList();
				foreach (var w in moving)
				{
					source.Remove(w);
					target.Add(w);
				}

				_scenes.Remove(source);
				_sceneById.Remove(source.Id);

				SceneChanged?.Invoke(this, new SceneChangedEventArgs(target, moving.FirstOrDefault(), ChangeType.Updated));
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(source, moving.FirstOrDefault(), ChangeType.Removed));

				// they come on top of the stage in the order they had on their card, like they flew there. The front one is activated:
				// the user just put them there to work with them, and only the foreground window is sure to stay in front of the others
				ShowInOrder(moving);
				moving.LastOrDefault()?.Focus();

				return Task.CompletedTask;
			}
			finally
			{
				_suspend = false;
			}
		}

		/// <summary>
		/// Puts all windows and apps of one card into another card in the sidebar. Neither is on the stage, so no window moves.
		/// Merging into the scene on the stage is <see cref="JoinToCurrent"/>.
		/// </summary>
		public Task MergeScenes(Scene source, Scene target)
		{
			if (source is null || target is null || source.Equals(target))
				return Task.CompletedTask;

			if (target.Equals(_current))
				return JoinToCurrent(source);

			if (source.Equals(_current))
				return Task.CompletedTask;

			try
			{
				_suspend = true;

				if (_registry.Merge(source.Id, target.Id) is null)
					return Task.CompletedTask;

				var moving = source.Windows.ToList();
				foreach (var w in moving)
				{
					source.Remove(w);
					target.Add(w);
				}

				_scenes.Remove(source);
				_sceneById.Remove(source.Id);

				SceneChanged?.Invoke(this, new SceneChangedEventArgs(target, moving.FirstOrDefault(), ChangeType.Updated));
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(source, moving.FirstOrDefault(), ChangeType.Removed));

				return Task.CompletedTask;
			}
			finally
			{
				_suspend = false;
			}
		}

		/// <summary>Takes one app out of a scene that holds several apps and gives it a card of its own.</summary>
		public Task SplitApp(Scene scene, string appKey)
		{
			if (scene is null || _registry.Split(scene.Id, appKey) is not SplitResult split)
				return Task.CompletedTask;

			try
			{
				_suspend = true;

				var windows = scene.Windows.Where(w => split.Windows.Contains(w.Handle.ToInt64())).ToArray();
				foreach (var w in windows)
					scene.Remove(w);

				var created = new Scene(split.NewGroupId, appKey, windows);
				_scenes.Insert(_scenes.IndexOf(scene) + 1, created);
				_sceneById[created.Id] = created;

				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, windows.FirstOrDefault(), ChangeType.Updated));
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(created, windows.FirstOrDefault(), ChangeType.Created));

				// an app that leaves the stage is no longer shown
				if (scene.Equals(_current))
				{
					foreach (var w in windows)
						WindowStrategy.Hide(w);
				}

				return Task.CompletedTask;
			}
			finally
			{
				_suspend = false;
			}
		}

		/// <summary>Asks all windows of a scene to close, like the close button of each window does. Apps can still ask to save their work.</summary>
		public void CloseScene(Scene scene)
		{
			if (scene is null)
				return;

			foreach (var w in scene.Windows.ToArray())
				w.Close();
		}

		/// <summary>The apps of a scene that can be excluded: the entry for the list, a name to show and the executable.</summary>
		public static IReadOnlyList<(string Entry, string Name, string? Path)> ExcludableAppsOf(Scene scene) =>
			scene.Windows
				.GroupBy(GroupKeyOf, StringComparer.OrdinalIgnoreCase)
				.Select(g =>
				{
					var first = g.First();
					var path = (first as WindowsWindow)?.ProcessExecutable;
					var entry = ExcludedApps.EntryFor(path);
					return (Entry: string.IsNullOrEmpty(entry) ? first.ProcessFileName : entry, Name: AppInfo.DisplayNameOf(path, first.ProcessName), Path: path);
				})
				.Where(a => !string.IsNullOrEmpty(a.Entry))
				.ToList();

		/// <summary>The apps a scene consists of, with the key that identifies them and a name to show.</summary>
		public static IReadOnlyList<(string Key, string Name)> AppsOf(Scene scene) =>
			scene.Windows
				.GroupBy(GroupKeyOf, StringComparer.OrdinalIgnoreCase)
				.Select(g => (g.Key, g.First().ProcessName))
				.ToList();

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

			var snapshots = _registry.Initialize(windows.Select(w => (w.Handle.ToInt64(), GroupKeyOf(w))));

			// the windows of each scene lie back to front like they do on the screen, minimized ones included: Windows keeps their place
			var topToBottom = WindowStack.TopToBottom();

			_scenes = new List<Scene>();
			foreach (var group in snapshots)
			{
				var ordered = ZOrder.FromScreen(group.Windows, topToBottom);
				var scene = new Scene(group.Id, group.Key, ordered.Select(h => byHandle[h]).ToArray());
				_scenes.Add(scene);
				_sceneById[group.Id] = scene;
			}
		}

		/// <summary>The windows that decide if the sidebar has to get out of the way. Parked windows are not on the screen, they do not count.</summary>
		public IEnumerable<IWindow> GetCurrentWindows() =>
			(_current?.Windows ?? GetSceneableWindows()).Where(w => !Parking.IsParked(w.Handle));

		public static string GroupKeyOf(IWindow window) =>
			string.IsNullOrEmpty(window.AppKey) ? window.ProcessName.ToUpperInvariant() : window.AppKey;
	}
}
