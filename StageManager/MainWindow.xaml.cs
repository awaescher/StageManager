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
		private SceneModel _mouseDownScene;

		public bool EnableWindowDropToScene = false;
		public bool EnableWindowPullToScene = true;

		public MainWindow()
		{
			InitializeComponent();

			DataContext = this;

			_overlapCheckTimer = new Timer(OverlapCheck, null, 2500, TIMERINTERVAL_MILLISECONDS);

			SwitchSceneCommand = new ActionCommand(async model => await SceneManager!.SwitchTo(((SceneModel)model).Scene));
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

			var windowsManager = new WindowsManager();
			SceneManager = new SceneManager(windowsManager);
			await SceneManager.Start().ConfigureAwait(true);

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

			SyncVisibilityByUpdatedTimeStamp();
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
			// if it's allowed to drag windows into scenes, we cannot hide the scenes
			if (EnableWindowDropToScene)
				_overlapCheckTimer.Change(TimeSpan.Zero, TimeSpan.Zero);

			var foregroundWindow = Win32.GetForegroundWindow();
			if (foregroundWindow != _thisHandle)
				return;

			if (EnableWindowPullToScene)
			{
				var screenPoint = new Point(e.Data.X, e.Data.Y);
				this.Dispatcher.Invoke(() =>
				{
					_mouseDownScene = FindSceneByPoint(screenPoint);
				});
			}
		}

		private void OnMouseReleased(object? sender, MouseHookEventArgs e)
		{
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

			if (EnableWindowPullToScene)
			{
				if (_geometry.IsPulledOutOfSidebar(e.Data.X) && _mouseDownScene is object)
				{
					this.Dispatcher.Invoke(() =>
					{
						SceneManager.PopWindowFrom(_mouseDownScene.Scene).SafeFireAndForget();
					});
				}
			}
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
