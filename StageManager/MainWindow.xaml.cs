using AsyncAwaitBestPractices;
using Microsoft.Xaml.Behaviors.Core;
using Microsoft.Win32;
using SharpHook;
using StageManager.Core;
using StageManager.Model;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Interop;
using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private const int TIMERINTERVAL_MILLISECONDS = 500;
		private const string APP_NAME = "StageManager";
		private IntPtr _thisHandle;
		private TaskPoolGlobalHook _hook;
		private WindowMode _mode;
		// physical pixels, written on the UI thread only but read by the mouse hook and timer threads
		private volatile SidebarGeometry _geometry = new(new Area(0, 0, 0, 0), 0, SidebarSide.Left);
		private double _dpiScale = 1;
		private int _taskbarCreatedMessage;
		private SidebarLayoutResult _layout = new(150, 100, 280, 6, false);
		private Timer _overlapCheckTimer;
		private Point _mouse = new Point(0, 0);
		private SceneModel _removedCurrentScene;

		// dragging a card out of the sidebar, UI thread only
		private readonly CardDragTracker _drag = new();
		private FrameworkElement? _dragElement;
		private SceneModel? _dragCard;
		private Area _dragCardArea;
		private Point _dragGrabOffset;
		private CardDragPreview? _dragPreview;
		// read by the timer thread: the sidebar must not slide away while a card is dragged out of it or flies back
		private volatile bool _dragActive;

		public bool EnableWindowDropToScene = false;

		public MainWindow()
		{
			InitializeComponent();

			DataContext = this;

			_overlapCheckTimer = new Timer(OverlapCheck, null, 2500, TIMERINTERVAL_MILLISECONDS);

			SwitchSceneCommand = new ActionCommand(async model =>
			{
				var scene = ((SceneModel)model).Scene;

				// Shift adds the app to the stage instead of replacing it
				if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
					await SceneManager!.JoinToCurrent(scene);
				else
					await SceneManager!.SwitchTo(scene);
			});
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);

			// the Explorer broadcasts this message after a restart, the desktop icons may be visible again then
			_taskbarCreatedMessage = Win32.RegisterWindowMessage("TaskbarCreated");
			((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this)).AddHook(WndProc);
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
				Dispatcher.BeginInvoke(() => SceneManager?.ResyncDesktopIcons());

			return IntPtr.Zero;
		}

		protected override void OnInitialized(EventArgs e)
		{
			base.OnInitialized(e);

			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

			ApplySide();

			App.Settings.Changed += Settings_Changed;
			SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

			StartHook();	
		}

		protected override void OnClosed(EventArgs e)
		{
			App.Settings.Changed -= Settings_Changed;
			SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;

			StopHook();

			trayIcon.Dispose();

			SceneManager.Stop();

			base.OnClosed(e);

			Environment.Exit(0);
		}

		protected override async void OnContentRendered(EventArgs e)
		{
			base.OnContentRendered(e);

			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

			// windows a killed run left out of the screen come back before anything else looks at them
			Parking.Recover();

			var windowsManager = new WindowsManager();
			SceneManager = new SceneManager(windowsManager);
			await SceneManager.Start().ConfigureAwait(true);

			SceneManager.CardAreaOf = CardArea;
			SceneManager.CardScale = _dpiScale;
			SceneManager.CardLanded += SceneManager_CardLanded;

			SceneManager.SceneChanged += SceneManager_SceneChanged;
			SceneManager.CurrentSceneSelectionChanged += SceneManager_CurrentSceneSelectionChanged;

			AddInitialScenes();

			var foreground = Win32.GetForegroundWindow();
			var foregroundScene = SceneManager.FindSceneForWindow(foreground);
			if (foregroundScene is object)
				await SceneManager.SwitchTo(foregroundScene).ConfigureAwait(true);
		}

		private void AddInitialScenes()
		{
			var initialScenes = SceneManager.GetScenes().ToArray();
			for (int i = 0; i < initialScenes.Length; i++)
			{
				var model = SceneModel.FromScene(initialScenes[i]);
				ApplyLayout(model);
				model.IsVisible = i <= MaxScenes; // i is zero based, so it should be i+1 but one scene gets selected (and removed from the sidebar) that makes i+0 again
				Scenes.Add(model);
			}
		}

		/// <summary>
		/// Where the thumbnails of a card are on the screen, in physical pixels. Null if the card is not in the sidebar at the moment.
		/// </summary>
		private Area? CardArea(Scene scene)
		{
			if (!IsVisible)
				return null;

			var model = Scenes.FirstOrDefault(s => s.Id == scene.Id);
			if (model is null)
				return null;

			if (scenesControl.ItemContainerGenerator.ContainerFromItem(model) is not FrameworkElement container)
				return null;

			var thumbs = FindThumbnailArea(container);
			if (thumbs is null || thumbs.ActualWidth <= 0 || thumbs.ActualHeight <= 0)
				return null;

			var topLeft = thumbs.PointToScreen(new Point(0, 0));
			var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
			return new Area(topLeft.X, topLeft.Y, topLeft.X + thumbs.ActualWidth * scale, topLeft.Y + thumbs.ActualHeight * scale);
		}

		/// <summary>The control that holds the thumbnails of a card.</summary>
		private static FrameworkElement? FindThumbnailArea(DependencyObject root)
		{
			for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
			{
				var child = VisualTreeHelper.GetChild(root, i);

				if (child is ItemsControl { Name: "thumbnailArea" } area)
					return area;

				if (FindThumbnailArea(child) is FrameworkElement found)
					return found;
			}

			return null;
		}

		private void SceneManager_CurrentSceneSelectionChanged(object? sender, CurrentSceneSelectionChangedEventArgs args)
		{
			var currentModel = args.Current is null ? null : Scenes.FirstOrDefault(m => m.Id == args.Current.Id);

			if (currentModel is object)
			{
				var currentIndex = Scenes.IndexOf(currentModel);
				Scenes.RemoveAt(currentIndex);

				if (_removedCurrentScene is object)
					Scenes.Insert(currentIndex, _removedCurrentScene);
			}
			else
			{
				if (_removedCurrentScene is object)
					Scenes.Add(_removedCurrentScene);
			}

			_removedCurrentScene = currentModel;

			// the windows of the prior scene are still flying to this card, it shows them when they arrive
			if (args.PriorCardWaits && args.Prior is object && AllScenes.FirstOrDefault(m => m.Id == args.Prior.Id) is SceneModel priorModel)
				priorModel.CardOpacity = 0;

			// a card that waited for windows that never arrived must not stay invisible
			if (currentModel is object)
				currentModel.CardOpacity = 1;

			SyncVisibilityByUpdatedTimeStamp();
		}

		private void SceneManager_CardLanded(object? sender, Scene scene)
		{
			if (AllScenes.FirstOrDefault(m => m.Id == scene.Id) is SceneModel model && !ReferenceEquals(model, _dragCard))
				model.CardOpacity = 1;
		}

		protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
		{
			base.OnRenderSizeChanged(sizeInfo);
			PositionWindow();
		}

		protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
		{
			base.OnDpiChanged(oldDpi, newDpi);
			PositionWindow();
		}

		private void Settings_Changed(object? sender, AppSettings e)
		{
			Dispatcher.BeginInvoke(() =>
			{
				ApplySide();
				SyncVisibilityByUpdatedTimeStamp();
				SceneManager?.ApplyDesktopIconSettings();
			});
		}

		private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
		{
			Dispatcher.BeginInvoke(PositionWindow);
		}

		private void UpdateGeometry()
		{
			_dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;

			// WPF works in device independent units, everything else (work area, mouse hook, window locations) in physical pixels
			var workArea = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;

			var sidebar = App.Settings.Current.Sidebar;
			_layout = SidebarLayout.Calculate(sidebar, workArea.Height / _dpiScale);

			if (SceneManager is object)
				SceneManager.CardScale = _dpiScale;

			if (Width != _layout.SidebarWidth)
				Width = _layout.SidebarWidth;

			_geometry = new SidebarGeometry(
				new Area(workArea.Left, workArea.Top, workArea.Right, workArea.Bottom),
				_layout.SidebarWidth * _dpiScale,
				sidebar.Side);

			foreach (var scene in AllScenes)
				ApplyLayout(scene);
		}

		private void ApplyLayout(SceneModel? scene)
		{
			scene?.ApplyLayout(_layout.ThumbnailWidth, _layout.ThumbnailHeight, App.Settings.Current.Sidebar.MaxWindowsPerScene);
		}

		private void PositionWindow()
		{
			UpdateGeometry();

			// drop a held animation, otherwise it would override the position set below
			BeginAnimation(LeftProperty, null);

			Left = _geometry.GetLeft(Mode != WindowMode.OffScreen) / _dpiScale;
			Top = _geometry.Area.Top / _dpiScale;
			Height = _geometry.Area.Height / _dpiScale;
		}

		private void ApplySide()
		{
			var right = App.Settings.Current.Sidebar.Side == SidebarSide.Right;

			scenesControl.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
			scenesControl.Padding = right ? new Thickness(0, 0, SidebarLayout.EdgePadding, 0) : new Thickness(SidebarLayout.EdgePadding, 0, 0, 0);

			PositionWindow();
		}

		/// <summary>
		/// Number of scenes that fit into the sidebar. Can be lower than the configured number on small screens.
		/// </summary>
		private int MaxScenes => _layout.EffectiveMaxScenes;

		private void SceneManager_SceneChanged(object sender, SceneChangedEventArgs e)
		{
			this.Dispatcher.Invoke(() =>
			{
				switch (e.Change)
				{
					case ChangeType.Created:
						var created = SceneModel.FromScene(e.Scene);
						ApplyLayout(created);
						Scenes.Add(created);
						SyncVisibilityByUpdatedTimeStamp();
						break;
					case ChangeType.Updated:
						if (AllScenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toUpdate)
							toUpdate.UpdateFromScene(e.Scene);
						break;
					case ChangeType.Removed:
						if (AllScenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toRemove)
						{
							if (toRemove.Equals(_removedCurrentScene))
								_removedCurrentScene = null;
							else
								Scenes.Remove(toRemove);
						}
						SyncVisibilityByUpdatedTimeStamp();
						break;
				}
			});
		}

		private void OnMousePressed(object? sender, MouseHookEventArgs e)
		{
			// only the left button drags a card
			if (e.Data.Button != SharpHook.Native.MouseButton.Button1)
				return;

			// if it's allowed to drag windows into scenes, we cannot hide the scenes
			if (EnableWindowDropToScene)
				_overlapCheckTimer.Change(TimeSpan.Zero, TimeSpan.Zero);
		}

		private void OnMouseReleased(object? sender, MouseHookEventArgs e)
		{
			if (e.Data.Button != SharpHook.Native.MouseButton.Button1)
				return;

			// if it's allowed to drag windows into scenes, we cannot hide the scenes
			if (EnableWindowDropToScene)
			{
				_overlapCheckTimer.Change(0, TIMERINTERVAL_MILLISECONDS);

				var foregroundWindow = Win32.GetForegroundWindow();

				if (foregroundWindow == _thisHandle)
					return;

				var screenPoint = new Point(e.Data.X, e.Data.Y);
				this.Dispatcher.Invoke(() =>
				{
					var sceneModel = FindSceneByPoint(screenPoint);

					if (sceneModel is object)
						SceneManager.MoveWindow(foregroundWindow, sceneModel.Scene).SafeFireAndForget();
				});
			}
		}

		private const int VK_ESCAPE = 0x1B;

		/// <summary>Pointer in physical screen pixels, the unit of the sidebar geometry and of window positions.</summary>
		private static Point ScreenPoint(FrameworkElement element, MouseEventArgs e) => element.PointToScreen(e.GetPosition(element));

		private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (sender is not FrameworkElement { DataContext: SceneModel card } element)
				return;

			e.Handled = true;

			// a card that still flies back or to the stage cannot be grabbed again
			if (_dragActive)
				return;

			if (!element.CaptureMouse())
				return;

			var p = ScreenPoint(element, e);
			_drag.Press(p.X, p.Y);
			_dragElement = element;
			_dragCard = card;
		}

		private void Card_MouseMove(object sender, MouseEventArgs e)
		{
			if (!_drag.IsPressed || !ReferenceEquals(sender, _dragElement))
				return;

			// the button went up without telling us, or Escape was pressed while the window has no keyboard focus
			if (e.LeftButton != MouseButtonState.Pressed || (Win32.GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
			{
				CancelCardDrag();
				return;
			}

			if (!App.Settings.Current.Sidebar.DragToStage)
				return;

			var p = ScreenPoint(_dragElement!, e);
			var wasDragging = _drag.IsDragging;
			var state = _drag.Move(p.X, p.Y, _geometry.DistanceOutside(p.X, p.Y));

			if (!state.IsDragging)
				return;

			if (!wasDragging)
				BeginCardDrag(p);

			if (_dragPreview is null)
				return;

			var rect = DragPreviewLayout.CardRect(p.X, p.Y, _dragGrabOffset.X, _dragGrabOffset.Y, _dragCardArea.Width, _dragCardArea.Height, state.Progress);
			_dragPreview.Update(rect, DragPreviewLayout.HintOpacity(state.Progress, state.IsArmed));
		}

		private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (!_drag.IsPressed || !ReferenceEquals(sender, _dragElement))
				return;

			e.Handled = true;

			var card = _dragCard;
			var result = _drag.Release();

			// releasing the capture raises LostMouseCapture, the drag is over already then
			_dragElement?.ReleaseMouseCapture();

			switch (result)
			{
				case DragEnd.Click:
					ClearCardDrag();
					if (card is object)
						SwitchSceneCommand.Execute(card);
					break;
				case DragEnd.Drop:
					EndCardDrag(drop: true).SafeFireAndForget(ex => Log.Error("Dropping a card failed", ex));
					break;
				default:
					EndCardDrag(drop: false).SafeFireAndForget(ex => Log.Error("Returning a card failed", ex));
					break;
			}
		}

		private void Card_LostMouseCapture(object sender, MouseEventArgs e)
		{
			// another window took the mouse, e.g. Alt+Tab, a menu or a dialog: never move windows then
			if (_drag.IsPressed && ReferenceEquals(sender, _dragElement))
				CancelCardDrag();
		}

		protected override void OnPreviewKeyDown(KeyEventArgs e)
		{
			if (e.Key == Key.Escape && _drag.IsPressed)
			{
				e.Handled = true;
				CancelCardDrag();
				return;
			}

			base.OnPreviewKeyDown(e);
		}

		private void CancelCardDrag()
		{
			if (!_drag.IsPressed)
				return;

			_drag.Cancel();
			_dragElement?.ReleaseMouseCapture();
			EndCardDrag(drop: false).SafeFireAndForget(ex => Log.Error("Returning a card failed", ex));
		}

		/// <summary>The press became a drag: the windows of the card come off the sidebar and follow the pointer.</summary>
		private void BeginCardDrag(Point pointer)
		{
			if (_dragCard is null || _dragElement is null || CardArea(_dragCard.Scene) is not Area card)
				return;

			_dragActive = true;
			_dragCardArea = card;
			_dragGrabOffset = new Point(pointer.X - (card.Left + card.Width / 2), pointer.Y - (card.Top + card.Height / 2));

			var settings = App.Settings.Current;
			var style = AnimationPolicy.DecideDragPreview(settings.Animations, SystemAnimation.Read());
			var margin = 16 * _dpiScale;
			var stage = _geometry.StageArea;
			var hint = new Area(stage.Left + margin, stage.Top + margin, stage.Right - margin, stage.Bottom - margin);
			var windows = _dragCard.Scene.Windows.Select(w => w.Handle).ToArray();

			_dragPreview = CardDragPreview.Start(windows, card, hint, style, settings.Sidebar.MaxWindowsPerScene, _dpiScale);

			// the card stays in the sidebar as a faint placeholder, like on macOS, once the windows show above it
			if (_dragPreview is CardDragPreview preview)
				DimWhenLifted(preview, _dragCard).SafeFireAndForget();
		}

		private async Task DimWhenLifted(CardDragPreview preview, SceneModel card)
		{
			await preview.WhenOnScreen().ConfigureAwait(true);

			if (ReferenceEquals(_dragPreview, preview))
				card.CardOpacity = 0.3;
		}

		private async Task EndCardDrag(bool drop)
		{
			var preview = _dragPreview;
			var card = _dragCard;
			var element = _dragElement;
			var cardArea = _dragCardArea;

			_dragPreview = null;
			_dragCard = null;
			_dragElement = null;

			try
			{
				if (card is null)
					return;

				var animations = App.Settings.Current.Animations;
				var duration = FlightMotion.Duration(animations.Speed);

				// without animations the windows only snap into place, the drag itself was feedback enough
				if (SystemAnimation.CurrentStyle() == FlightStyle.None)
					duration = TimeSpan.FromTicks(duration.Ticks / 3);

				if (drop)
				{
					var handles = card.Scene.Windows.Select(w => w.Handle).ToArray();
					if (preview is object)
					{
						foreach (var h in handles)
							NativeMethods.SetTransitionsDisabled(h, true);

						// the placeholder goes while the windows leave for the stage
						card.CardOpacity = 0;

						await Task.WhenAny(preview.Drop(duration), Task.Delay(duration + TimeSpan.FromMilliseconds(500))).ConfigureAwait(true);
					}

					// the preview has already flown the windows to their places, the switch must not fly them once more
					await SceneManager.JoinToCurrent(card.Scene, animate: preview is null).ConfigureAwait(true);

					// the real windows are drawn under the preview before it goes, both show the same picture
					if (preview is object)
					{
						NativeMethods.DwmFlush();
						await Task.Delay(90).ConfigureAwait(true);
						NativeMethods.DwmFlush();

						foreach (var h in handles)
							NativeMethods.SetTransitionsDisabled(h, false);
					}
				}
				else if (preview is object)
				{
					var target = CardArea(card.Scene) ?? cardArea;
					var back = TimeSpan.FromTicks(duration.Ticks * 3 / 4);
					await Task.WhenAny(preview.Return(target, back), Task.Delay(back + TimeSpan.FromMilliseconds(500))).ConfigureAwait(true);

					// the card takes over under the windows that landed on it, then they go
					card.CardOpacity = 1;
					NativeMethods.DwmFlush();
					await Task.Delay(50).ConfigureAwait(true);
					NativeMethods.DwmFlush();
				}
			}
			finally
			{
				try
				{
					preview?.Finish();
				}
				catch (Exception ex)
				{
					Log.Error("The drag preview could not be closed", ex);
				}

				if (card is object)
					card.CardOpacity = 1;

				_dragActive = false;
			}
		}

		private void ClearCardDrag()
		{
			_dragElement = null;
			_dragCard = null;
		}

		private SceneModel FindSceneByPoint(Point p)
		{
			var thisWindow = new WindowsWindow(_thisHandle);
			var pointOnWindow = new Point(p.X - thisWindow.Location.X, p.Y - thisWindow.Location.Y);

			var dpi = VisualTreeHelper.GetDpi(this);

			pointOnWindow.X /= dpi.DpiScaleX;
			pointOnWindow.Y /= dpi.DpiScaleY;

			SceneModel model = null;

			var element = VisualTreeHelper.HitTest(this, pointOnWindow)?.VisualHit;

			while (element is not null)
			{
				if (element is FrameworkElement { DataContext: SceneModel m })
				{
					model = m;
					break;
				}

				element = element.GetParentObject();
			}

			return model;
		}
		private void SyncVisibilityByUpdatedTimeStamp()
		{
			var scenes = Scenes.OrderByDescending(s => s.Updated).ToArray();
			for (int i = 0; i < scenes.Length; i++)
				scenes[i].IsVisible = i < MaxScenes;
		}

		public ObservableCollection<SceneModel> Scenes { get; } = new ObservableCollection<SceneModel>();

		public IEnumerable<SceneModel> AllScenes => _removedCurrentScene is null ? Scenes : Scenes.Append(_removedCurrentScene);

		public ICommand SwitchSceneCommand { get; }

		public SceneManager SceneManager { get; private set; }

		public IntPtr Handle => _thisHandle;

		public WindowMode Mode
		{
			get => _mode;
			set
			{
				if (value == _mode)
					return;

				_mode = value;

				this.Topmost = value == WindowMode.Flyover;

				ApplyWindowMode();
			}
		}

		private void ApplyWindowMode()
		{
			var newLeft = _geometry.GetLeft(Mode != WindowMode.OffScreen) / _dpiScale;
			if (Left == newLeft)
				return;

			var isIncoming = newLeft > Left;
			var easingMode = isIncoming ? EasingMode.EaseOut : EasingMode.EaseIn;

			var animation = new DoubleAnimationUsingKeyFrames();
			animation.Duration = new Duration(TimeSpan.FromSeconds(0.5));
			var easingFunction = new PowerEase { EasingMode = easingMode };
			animation.KeyFrames.Add(new EasingDoubleKeyFrame(Left, KeyTime.FromPercent(0)));
			animation.KeyFrames.Add(new EasingDoubleKeyFrame(newLeft, KeyTime.FromPercent(1.0), easingFunction));

			BeginAnimation(LeftProperty, animation);
		}

		private void StartHook()
		{
			_hook = new TaskPoolGlobalHook();

			_hook.MousePressed += OnMousePressed;
			_hook.MouseReleased += OnMouseReleased;
			_hook.MouseMoved += _hook_MouseMoved;

			Task.Run(_hook.Run);
		}

		private void StopHook()
		{
			_hook.MousePressed -= OnMousePressed;
			_hook.MouseReleased -= OnMouseReleased;
			_hook.MouseMoved -= _hook_MouseMoved;

			try
			{
				_hook.Dispose();
			}
			catch (HookException)
			{
			}
		}

		private void _hook_MouseMoved(object? sender, MouseHookEventArgs e)
		{
			_mouse.X = e.Data.X;
			_mouse.Y = e.Data.Y;

			if (Mode == WindowMode.OffScreen && _geometry.IsInTriggerZone(e.Data.X))
			{
				Dispatcher.Invoke(() => Mode = WindowMode.Flyover);
			}
		}

		private void OverlapCheck(object? _)
		{
			var currentWindows = SceneManager.GetCurrentWindows().ToArray(); // in case the enumeration changes
			UpdateModeByWindows(currentWindows);
		}

		private void UpdateModeByWindows(IEnumerable<IWindow> windows)
		{
			if (_dragActive)
				return;

			var geometry = _geometry;

			bool doesOverlap(IWindowLocation loc) => loc.State == Native.Window.WindowState.Maximized || (loc.State == Native.Window.WindowState.Normal && geometry.DoesNormalWindowOverlap(loc.X, loc.Width));

			var anyOverlappingWindows = windows.Any(w => doesOverlap(w.Location));

			var containsMouse = geometry.IsOverSidebar(_mouse.X);
			var setMode = Mode == WindowMode.OnScreen && !containsMouse
							|| Mode == WindowMode.OffScreen
							|| (Mode == WindowMode.Flyover && !containsMouse);

			if (setMode)
			{
				Dispatcher.Invoke(() =>
				{
					Mode = anyOverlappingWindows ? WindowMode.OffScreen : WindowMode.OnScreen;
				});
			}
		}

		private void NavigateToProjectPage()
		{
			Process.Start(new ProcessStartInfo("https://github.com/awaescher/StageManager")
			{
				UseShellExecute = true
			});
		}

		public static bool StartsWithWindows
		{ 
			get => AutoStart.IsStartup(APP_NAME);
			set => AutoStart.SetStartup(APP_NAME, value);
		}

		private void MenuItem_Settings_Click(object sender, RoutedEventArgs e)
		{
			SettingsWindow.ShowSingle();
		}

		private void CloseScene_Click(object sender, RoutedEventArgs e)
		{
			e.Handled = true;

			if (sender is FrameworkElement { DataContext: SceneModel model })
				SceneManager.CloseScene(model.Scene);
		}

		private static SceneModel? CardOf(object sender) =>
			sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: SceneModel m } } ? m
			: sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: SceneModel m2 } } } ? m2
			: null;

		private void CardMenu_Opened(object sender, RoutedEventArgs e)
		{
			StopHook();
			CancelCardDrag();

			if (sender is not ContextMenu menu || CardOf(menu) is not SceneModel card)
				return;

			var apps = SceneManager.AppsOf(card.Scene);
			var canAdd = SceneManager.Current is object;

			foreach (var item in menu.Items.OfType<MenuItem>())
			{
				switch (item.Tag as string)
				{
					case "add":
						item.IsEnabled = canAdd;
						break;
					case "split":
						item.Items.Clear();
						item.Visibility = apps.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
						foreach (var (key, name) in apps)
						{
							var sub = new MenuItem { Header = name, Tag = key };
							sub.Click += CardMenu_Split_Click;
							item.Items.Add(sub);
						}
						break;
				}
			}
		}

		private void CardMenu_Closed(object sender, RoutedEventArgs e)
		{
			StartHook();
		}

		private void CardMenu_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not MenuItem item || CardOf(item) is not SceneModel card)
				return;

			switch (item.Tag as string)
			{
				case "add":
					SceneManager.JoinToCurrent(card.Scene).SafeFireAndForget();
					break;
				case "close":
					SceneManager.CloseScene(card.Scene);
					break;
			}
		}

		private void CardMenu_Split_Click(object sender, RoutedEventArgs e)
		{
			if (sender is MenuItem { Tag: string key, Parent: MenuItem { Parent: ContextMenu menu } } && CardOf(menu) is SceneModel card)
				SceneManager.SplitApp(card.Scene, key).SafeFireAndForget();
		}

		private void MenuItem_ProjectPage_Click(object sender, RoutedEventArgs e)
		{
			NavigateToProjectPage();
		}

		private void MenuItem_Quit_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void ContextMenu_Closed(object sender, RoutedEventArgs e)
		{
			StartHook();
		}

		private void ContextMenu_Opened(object sender, RoutedEventArgs e)
		{
			var sidebar = App.Settings.Current.Sidebar;
			var icons = App.Settings.Current.DesktopIcons;
			menuSideLeft.IsChecked = sidebar.Side == SidebarSide.Left;
			menuSideRight.IsChecked = sidebar.Side == SidebarSide.Right;

			menuSceneCount.Items.Clear();
			foreach (var count in SceneCountChoices.Append(sidebar.MaxScenes).Distinct().OrderBy(c => c))
			{
				var item = new MenuItem { Header = count.ToString(), Tag = count, IsCheckable = false, IsChecked = count == sidebar.MaxScenes };
				item.Click += MenuItem_SceneCount_Click;
				menuSceneCount.Items.Add(item);
			}

			menuIconsManage.IsChecked = icons.Mode == DesktopIconsMode.Manage;
			menuIconsHidden.IsChecked = icons.Mode == DesktopIconsMode.AlwaysHidden;
			menuIconsUntouched.IsChecked = icons.Mode == DesktopIconsMode.Untouched;

			StopHook();
		}

		private static readonly int[] SceneCountChoices = { 2, 3, 4, 5, 6, 8, 10, 12 };

		private void MenuItem_Icons_Click(object sender, RoutedEventArgs e)
		{
			if (sender is MenuItem { Tag: string tag } && Enum.TryParse<DesktopIconsMode>(tag, out var mode))
				App.Settings.Update(s => s.DesktopIcons.Mode = mode);
		}

		private void MenuItem_SceneCount_Click(object sender, RoutedEventArgs e)
		{
			if (sender is MenuItem { Tag: int count })
				App.Settings.Update(s => s.Sidebar.MaxScenes = count);
		}

		private void MenuItem_SideLeft_Click(object sender, RoutedEventArgs e)
		{
			App.Settings.Update(s => s.Sidebar.Side = SidebarSide.Left);
		}

		private void MenuItem_SideRight_Click(object sender, RoutedEventArgs e)
		{
			App.Settings.Update(s => s.Sidebar.Side = SidebarSide.Right);
		}
	}

	public enum WindowMode
	{
		OnScreen,
		OffScreen,
		Flyover
	}
}
