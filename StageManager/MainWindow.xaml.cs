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
using System.Windows.Threading;

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
		private SidebarMode _mode;
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
		private readonly CardDropTargets _dropTargets = new();
		private SceneModel? _mergeTarget;
		private FrameworkElement? _dragElement;
		private SceneModel? _dragCard;
		private Area _dragCardArea;
		private Point _dragGrabOffset;
		private CardDragPreview? _dragPreview;
		// read by the timer thread: the sidebar must not slide away while a card is dragged out of it or flies back
		private volatile bool _dragActive;

		public bool EnableWindowDropToScene = false;

		// switching scenes with the keyboard, UI thread only
		private HotkeyManager? _hotkeys;
		private readonly ScenePicker _picker = new();
		private SceneModel? _pickedModel;
		private HotkeyModifiers _hotkeyKeys = HotkeyText.Default;

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
			StartPresence();
			StartHotkeys();
		}

		protected override void OnClosed(EventArgs e)
		{
			_presenceTimer?.Stop();
			App.Settings.Changed -= Settings_Changed;
			SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;

			App.Hotkeys = null;
			_hotkeys?.Dispose();

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

			// windows a killed run left out of the screen come back before anything else looks at them, also the ones its journal missed
			Parking.Recover();
			RescueWindows();

			var windowsManager = new WindowsManager();
			SceneManager = new SceneManager(windowsManager);
			await SceneManager.Start().ConfigureAwait(true);

			SceneManager.CardAreaOf = CardArea;
			SceneManager.CardScale = _dpiScale;
			SceneManager.SidebarHandle = _thisHandle;
			SceneManager.CardLanded += SceneManager_CardLanded;

			SceneManager.SceneChanged += SceneManager_SceneChanged;
			SceneManager.CurrentSceneSelectionChanged += SceneManager_CurrentSceneSelectionChanged;

			AddInitialScenes();

			var foreground = Win32.GetForegroundWindow();
			var foregroundScene = SceneManager.FindSceneForWindow(foreground);
			if (foregroundScene is object)
				await SceneManager.SwitchTo(foregroundScene).ConfigureAwait(true);
		}

		private static void RescueWindows(bool running = false)
		{
			try
			{
				// while running, the windows of the hidden scenes are out of the screen on purpose
				var count = WindowRescue.Run(running ? Parking.IsParked : null);
				if (count > 0)
					Log.Warn($"{count} window(s) out of the screen were brought back");
			}
			catch (Exception ex)
			{
				Log.Error("Bringing back windows out of the screen failed", ex);
			}
		}

		private void MenuItem_Rescue_Click(object sender, RoutedEventArgs e)
		{
			RescueWindows(running: true);
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
				SceneManager?.ApplyExcludedApps();
				ApplyHotkeys();
			});
		}

		private void StartHotkeys()
		{
			_hotkeys = new HotkeyManager();
			_hotkeys.Triggered += Hotkeys_Triggered;
			App.Hotkeys = _hotkeys;
			ApplyHotkeys();
		}

		private void ApplyHotkeys()
		{
			var settings = App.Settings.Current.Hotkeys;

			if (!HotkeyText.TryParse(settings.Keys, out var keys))
				keys = HotkeyText.Default;

			if (!settings.Enabled || keys != _hotkeyKeys)
				CancelPick();

			_hotkeyKeys = keys;
			_hotkeys?.Apply(settings.Enabled, keys);
		}

		/// <summary>The cards in the order of the sidebar, without the ones that are only managed but not shown.</summary>
		private List<SceneModel> SidebarCards() => Scenes.Where(m => m.IsVisible).ToList();

		private void Hotkeys_Triggered(object? sender, HotkeyAction action)
		{
			if (SceneManager is null || _dragActive || _drag.IsPressed)
				return;

			if (action.Kind == HotkeyActionKind.Number)
			{
				CancelPick();
				var cards = SidebarCards();
				if (action.Number <= cards.Count)
					SwitchToCard(cards[action.Number - 1]);
				return;
			}

			// the most recently used first, like Alt+Tab: the first Tab goes to the scene before this one
			var candidates = SidebarCards().OrderByDescending(m => m.Updated).Select(m => m.Id).ToList();
			var picked = _picker.Step(candidates, backwards: action.Kind == HotkeyActionKind.Previous);
			SetPicked(picked is Guid id ? Scenes.FirstOrDefault(m => m.Id == id) : null);
		}

		private void SwitchToCard(SceneModel card)
		{
			SceneManager.SwitchTo(card.Scene).SafeFireAndForget(ex => Log.Error("Switching with the keyboard failed", ex));
		}

		private void SetPicked(SceneModel? model)
		{
			if (ReferenceEquals(model, _pickedModel))
				return;

			if (_pickedModel is object)
				_pickedModel.IsPicked = false;

			_pickedModel = model;

			if (model is object)
				model.IsPicked = true;
		}

		private void CancelPick()
		{
			_picker.End();
			SetPicked(null);
		}

		/// <summary>Letting go of the held keys switches to the picked scene, Escape cancels.</summary>
		private void UpdatePicking()
		{
			if (!_picker.IsActive)
				return;

			if (HeldKeys.IsEscapeDown())
			{
				CancelPick();
				return;
			}

			if (HeldKeys.AreDown(_hotkeyKeys))
				return;

			var model = _pickedModel;
			CancelPick();

			if (model is object && Scenes.Contains(model))
				SwitchToCard(model);
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

			Left = _geometry.GetLeft(Mode != SidebarMode.OffScreen) / _dpiScale;
			Top = _geometry.Area.Top / _dpiScale;
			Height = _geometry.Area.Height / _dpiScale;
		}

		private void ApplySide()
		{
			var right = App.Settings.Current.Sidebar.Side == SidebarSide.Right;

			scenesControl.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;

			// like on macOS the icons sit on the side of the screen edge, the close button on the other one
			IconAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
			CloseButtonAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right;
			scenesControl.Padding = right ? new Thickness(0, 0, SidebarLayout.EdgePadding, 0) : new Thickness(SidebarLayout.EdgePadding, 0, 0, 0);

			PositionWindow();
		}

		public static readonly DependencyProperty IconAlignmentProperty = DependencyProperty.Register(nameof(IconAlignment),
			typeof(HorizontalAlignment), typeof(MainWindow), new PropertyMetadata(HorizontalAlignment.Left));

		public static readonly DependencyProperty CloseButtonAlignmentProperty = DependencyProperty.Register(nameof(CloseButtonAlignment),
			typeof(HorizontalAlignment), typeof(MainWindow), new PropertyMetadata(HorizontalAlignment.Right));

		/// <summary>Where the app icons sit below a card.</summary>
		public HorizontalAlignment IconAlignment
		{
			get => (HorizontalAlignment)GetValue(IconAlignmentProperty);
			set => SetValue(IconAlignmentProperty, value);
		}

		public HorizontalAlignment CloseButtonAlignment
		{
			get => (HorizontalAlignment)GetValue(CloseButtonAlignmentProperty);
			set => SetValue(CloseButtonAlignmentProperty, value);
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
						if (AllScenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toUpdate && toUpdate.UpdateFromScene(e.Scene))
							RestackThumbnails(toUpdate);
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

		/// <summary>
		/// The thumbnails of a card lie on top of each other like its windows, the last one in front. DWM draws them in the order they
		/// were registered, so after the windows changed their order they are registered again, back to front. Once the card is laid out.
		/// </summary>
		private void RestackThumbnails(SceneModel model)
		{
			Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
			{
				if (scenesControl.ItemContainerGenerator.ContainerFromItem(model) is not FrameworkElement container
					|| FindThumbnailArea(container) is not ItemsControl area)
					return;

				foreach (var window in model.Windows)
				{
					if (area.ItemContainerGenerator.ContainerFromItem(window) is DependencyObject item && FindThumbnail(item) is DwmThumbnail thumbnail)
						thumbnail.BringThumbnailToFront();
				}
			});
		}

		private static DwmThumbnail? FindThumbnail(DependencyObject root)
		{
			if (root is DwmThumbnail thumbnail)
				return thumbnail;

			for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
			{
				if (FindThumbnail(VisualTreeHelper.GetChild(root, i)) is DwmThumbnail found)
					return found;
			}

			return null;
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

			// over another card in the sidebar: that card is the target, not the stage
			var target = state.IsArmed ? null : FindMergeTarget(p);
			SetMergeTarget(target);

			var rect = DragPreviewLayout.CardRect(p.X, p.Y, _dragGrabOffset.X, _dragGrabOffset.Y, _dragCardArea.Width, _dragCardArea.Height, state.Progress);
			_dragPreview.Update(rect, target is object ? 0 : DragPreviewLayout.HintOpacity(state.Progress, state.IsArmed));
		}

		private SceneModel? FindMergeTarget(Point p)
		{
			var cards = new List<(Guid, Area)>();
			foreach (var model in Scenes)
			{
				if (!model.IsVisible || ReferenceEquals(model, _dragCard))
					continue;

				if (CardBounds(model) is Area area)
					cards.Add((model.Id, area));
			}

			var id = _dropTargets.Update(p.X, p.Y, cards);
			return id is Guid g ? Scenes.FirstOrDefault(s => s.Id == g) : null;
		}

		/// <summary>The whole card with its icons, in physical pixels: the area a dragged card can be dropped on.</summary>
		private Area? CardBounds(SceneModel model)
		{
			if (scenesControl.ItemContainerGenerator.ContainerFromItem(model) is not FrameworkElement container
				|| VisualTreeHelper.GetChildrenCount(container) == 0
				|| VisualTreeHelper.GetChild(container, 0) is not FrameworkElement card
				|| card.ActualWidth <= 0 || card.ActualHeight <= 0)
				return null;

			var topLeft = card.PointToScreen(new Point(0, 0));
			return new Area(topLeft.X, topLeft.Y, topLeft.X + card.ActualWidth * _dpiScale, topLeft.Y + card.ActualHeight * _dpiScale);
		}

		private void SetMergeTarget(SceneModel? target)
		{
			if (ReferenceEquals(target, _mergeTarget))
				return;

			if (_mergeTarget is object)
				_mergeTarget.IsMergeTarget = false;

			_mergeTarget = target;

			if (target is object)
				target.IsMergeTarget = true;
		}

		private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			if (!_drag.IsPressed || !ReferenceEquals(sender, _dragElement))
				return;

			e.Handled = true;

			var card = _dragCard;
			var mergeInto = _mergeTarget;
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
					EndCardDrag(drop: false, mergeInto).SafeFireAndForget(ex => Log.Error("Returning a card failed", ex));
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

		private async Task EndCardDrag(bool drop, SceneModel? mergeInto = null)
		{
			var preview = _dragPreview;
			var card = _dragCard;
			var element = _dragElement;
			var cardArea = _dragCardArea;

			_dragPreview = null;
			_dragCard = null;
			_dragElement = null;
			SetMergeTarget(null);
			_dropTargets.Clear();

			try
			{
				if (card is null)
					return;

				var animations = App.Settings.Current.Animations;
				var duration = FlightMotion.Duration(animations.Speed);

				// without animations the windows only snap into place, the drag itself was feedback enough
				if (SystemAnimation.CurrentStyle() == FlightStyle.None)
					duration = TimeSpan.FromTicks(duration.Ticks / 3);

				if (!drop && mergeInto is object && preview is object && CardArea(mergeInto.Scene) is Area targetArea)
				{
					// the windows fly onto the other card, to the front of its stack where they will be after the merge
					card.CardOpacity = 0;
					var into = TimeSpan.FromTicks(duration.Ticks * 3 / 4);
					var first = mergeInto.Windows.Count;
					var total = first + card.Windows.Count;
					await Task.WhenAny(preview.Return(targetArea, into, first, total), Task.Delay(into + TimeSpan.FromMilliseconds(500))).ConfigureAwait(true);

					await SceneManager.MergeScenes(card.Scene, mergeInto.Scene).ConfigureAwait(true);

					// the merged card is drawn with the new windows before the flying ones go
					NativeMethods.DwmFlush();
					await Task.Delay(60).ConfigureAwait(true);
					NativeMethods.DwmFlush();
					return;
				}

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

		public SidebarMode Mode
		{
			get => _mode;
			set
			{
				if (value == _mode)
					return;

				_mode = value;

				// the sidebar always stays above the windows, also while it sits at its place: a window of the stage that comes to the
				// front, e.g. when it lands after a flight, must not cover it. Windows that need its place make it slide away instead
				if (value != SidebarMode.OffScreen && !_dragActive && SceneManager?.IsFlying != true)
					KeepOnTop();

				ApplyWindowMode();
			}
		}

		private void ApplyWindowMode()
		{
			var newLeft = _geometry.GetLeft(Mode != SidebarMode.OffScreen) / _dpiScale;
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
			// the hook only keeps the last position for the overlap check, it never calls into the UI: Windows silently removes
			// a low level hook that answers too slowly, and then the screen edge stopped working
			_mouse.X = e.Data.X;
			_mouse.Y = e.Data.Y;
		}

		private volatile bool _overlapped;

		private void OverlapCheck(object? _)
		{
			var manager = SceneManager;
			if (manager is null)
				return;

			// while the windows of the new scene are still parked nothing seems to cover the sidebar: it would slide in for the flight
			// and out again once a maximized window has landed. The check after the switch decides
			if (manager.IsSwitching)
				return;

			try
			{
				var geometry = _geometry;
				bool doesOverlap(IWindowLocation loc) => loc.State == Native.Window.WindowState.Maximized || (loc.State == Native.Window.WindowState.Normal && geometry.DoesNormalWindowOverlap(loc.X, loc.Width));

				var currentWindows = manager.GetCurrentWindows().ToArray(); // in case the enumeration changes
				_overlapped = currentWindows.Any(w => doesOverlap(w.Location));
			}
			catch (Exception ex)
			{
				Log.Error("The overlap check failed", ex);
			}
		}

		private SidebarPresence? _presence;
		private SidebarGeometry? _presenceGeometry;
		private readonly Stopwatch _clock = Stopwatch.StartNew();
		private DispatcherTimer? _presenceTimer;
		private bool _menuOpen;

		private void StartPresence()
		{
			_presenceTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
			_presenceTimer.Tick += (_, _) =>
			{
				UpdatePicking();
				UpdatePresence();
			};
			_presenceTimer.Start();
		}

		/// <summary>Shows, hides or lifts the sidebar. Runs on the UI thread and reads the pointer itself, it does not rely on any hook.</summary>
		private void UpdatePresence()
		{
			if (!Win32.GetCursorPos(out var cursor))
				return;

			var geometry = _geometry;
			if (!ReferenceEquals(geometry, _presenceGeometry))
			{
				_presenceGeometry = geometry;
				_presence = new SidebarPresence(geometry);
			}

			var hold = _dragActive || _drag.IsPressed || _menuOpen;
			var input = new SidebarInput(cursor.X, cursor.Y, _overlapped, hold, _clock.Elapsed.TotalSeconds, Summon: _picker.IsActive);
			Mode = _presence!.Decide(Mode, input);

			// a window that came to the front since may cover it, lift it above again. Not while windows fly or a card is dragged:
			// those overlays have to stay above the sidebar. Once they are gone it is lifted again
			if (Mode != SidebarMode.OffScreen && !_dragActive && SceneManager?.IsFlying != true)
			{
				var foreground = Win32.GetForegroundWindow();
				if (foreground != _liftedFor || !IsTopmost())
				{
					_liftedFor = foreground;
					KeepOnTop();
				}
			}
			else
			{
				_liftedFor = IntPtr.Zero;
			}
		}

		private bool IsTopmost() => _thisHandle == IntPtr.Zero || Win32.GetWindowExStyleLongPtr(_thisHandle).HasFlag(Win32.WS_EX.WS_EX_TOPMOST);

		private IntPtr _liftedFor;

		private void KeepOnTop()
		{
			if (_thisHandle == IntPtr.Zero)
				return;

			// Topmost alone is not enough: WPF does not set it again if it thinks it is set, and a window that becomes topmost later is above
			Win32.SetWindowPos(_thisHandle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
				Win32.SetWindowPosFlags.IgnoreMove | Win32.SetWindowPosFlags.IgnoreResize | Win32.SetWindowPosFlags.DoNotActivate);
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
			_menuOpen = true;
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
					case "exclude":
						FillExcludeMenu(item, card.Scene);
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

		/// <summary>
		/// "Exclude" for the apps of a card: one app is excluded right away, with several apps the menu names each of them. The card is
		/// gone afterwards, so it is not asked twice.
		/// </summary>
		private void FillExcludeMenu(MenuItem item, Scene scene)
		{
			var apps = SceneManager.ExcludableAppsOf(scene);

			item.Items.Clear();
			item.Visibility = apps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

			if (apps.Count == 1)
			{
				item.Header = Loc.Format("Card.ExcludeApp", apps[0].Name);
				item.Tag = "exclude";
				item.Click -= CardMenu_Exclude_Click;
				item.Click += CardMenu_Exclude_Click;
				item.DataContext = apps[0].Entry;
				return;
			}

			item.Header = Loc.Get("Card.Exclude");
			item.DataContext = null;
			item.Click -= CardMenu_Exclude_Click;

			foreach (var (entry, name, path) in apps)
			{
				var sub = new MenuItem { Header = name, DataContext = entry, Icon = AppIconOf(path) };
				sub.Click += CardMenu_Exclude_Click;
				item.Items.Add(sub);
			}
		}

		private static System.Windows.Controls.Image? AppIconOf(string? path) =>
			AppInfo.IconOf(path) is ImageSource source ? new System.Windows.Controls.Image { Source = source, Width = 16, Height = 16 } : null;

		private void CardMenu_Exclude_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not MenuItem { DataContext: string entry } || entry.Length == 0)
				return;

			e.Handled = true;
			App.Settings.Update(s => s.Stage.ExcludedApps = ExcludedApps.With(s.Stage.ExcludedApps, entry));
		}

		private void CardMenu_Closed(object sender, RoutedEventArgs e)
		{
			_menuOpen = false;
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
			_menuOpen = false;
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

			_menuOpen = true;
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

}
