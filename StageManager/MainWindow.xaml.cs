using AsyncAwaitBestPractices;
using MahApps.Metro.Controls;
using Microsoft.Xaml.Behaviors.Core;
using SharpHook;
using StageManager.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using workspacer;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private const int TIMERINTERVAL_MILLISECONDS = 500;

		private IntPtr _thisHandle;
		private TaskPoolGlobalHook _hook;
		private WindowMode _mode;
		private double _lastWidth;
		private Timer _overlapCheckTimer;
		private Point _mouse = new Point(0, 0);
		private SceneModel _removedCurrentScene;

		public MainWindow()
		{
			InitializeComponent();

			this.DataContext = this;

			_overlapCheckTimer = new Timer(OverlapCheck, null, 2500, TIMERINTERVAL_MILLISECONDS);
			SwitchSceneCommand = new ActionCommand(async model => await SceneManager!.SwitchTo(((SceneModel)model).Scene));
		}

		protected override void OnInitialized(EventArgs e)
		{
			base.OnInitialized(e);

			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
			_lastWidth = Width;

			_hook = new TaskPoolGlobalHook();

			_hook.MousePressed += OnMousePressed;
			_hook.MouseReleased += OnMouseReleased;
			_hook.MouseMoved += _hook_MouseMoved;

			Task.Run(() => _hook.Run());
		}

		protected override void OnClosed(EventArgs e)
		{
			_hook.MouseReleased -= OnMouseReleased;
			_hook.Dispose();

			base.OnClosed(e);

			PoorMansAsync.Wait(SceneManager.Reset, TimeSpan.FromSeconds(3));
			Environment.Exit(0);
		}

		protected override void OnContentRendered(EventArgs e)
		{
			base.OnContentRendered(e);
			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

			var windowsManager = new WindowsManager();
			SceneManager = new SceneManager(windowsManager);
			SceneManager.Reset().ConfigureAwait(true);
			SceneManager.Start().ConfigureAwait(true);

			SceneManager.SceneChanged += SceneManager_SceneChanged;
			SceneManager.CurrentSceneSelectionChanged += SceneManager_CurrentSceneSelectionChanged;
			SceneManager.RequestWindowPreviewUpdate += SceneManager_RequestWindowPreviewUpdate;

			foreach (var scene in SceneManager.GetScenes())
			{
				var model = SceneModel.FromScene(scene);
				Scenes.Add(model);
			}
		}

		private void SceneManager_CurrentSceneSelectionChanged(object? sender, CurrentSceneSelectionChangedEventArgs args)
		{
			var currentModel = Scenes.FirstOrDefault(m => m.Id == args.Current.Id);

			if (currentModel is object)
			{
				var currentIndex = Scenes.IndexOf(currentModel);
				Scenes.RemoveAt(currentIndex);

				if (_removedCurrentScene is object)
					Scenes.Insert(currentIndex, _removedCurrentScene);

				_removedCurrentScene = currentModel;
			}
		}

		private void SceneManager_RequestWindowPreviewUpdate(object? sender, IWindow window)
		{
			var toUpdate = Scenes.Union(new[]{ _removedCurrentScene })
				.Select(s => s?.Windows.FirstOrDefault(w => w.Handle == window.Handle))
				.Where(w => w is object)
				.FirstOrDefault();

			toUpdate?.UpdatePreview();
		}

		protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
		{
			base.OnRenderSizeChanged(sizeInfo);
			var area = this.GetMonitorWorkSize();
			this.Left = 0;
			this.Top = 0;
			//this.Width = area.Width / 15;
			this.Height = area.Height;
		}

		private void SceneManager_SceneChanged(object sender, SceneChangedEventArgs e)
		{
			this.Dispatcher.Invoke(() =>
			{
				switch (e.Change)
				{
					case ChangeType.Created:
						Scenes.Add(SceneModel.FromScene(e.Scene));
						break;
					case ChangeType.Updated:
						if (Scenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toUpdate)
							toUpdate.UpdateFromScene(e.Scene);
						break;
					case ChangeType.Removed:
						if (Scenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toRemove)
							Scenes.Remove(toRemove);
						break;
					default:
						break;
				}
			});
		}

		private void OnMousePressed(object? sender, MouseHookEventArgs e)
		{
			_overlapCheckTimer.Change(TimeSpan.Zero, TimeSpan.Zero);
		}

		private void OnMouseReleased(object? sender, MouseHookEventArgs e)
		{
			_overlapCheckTimer.Change(0, TIMERINTERVAL_MILLISECONDS);

			var foregroundWindow = workspacer.Win32.GetForegroundWindow();

			if (foregroundWindow == _thisHandle)
				return;

			var screenPoint = new Point(e.Data.X, e.Data.Y);
			this.Dispatcher.Invoke(() =>
			{
				var thisWindow = new WindowsWindow(_thisHandle);
				var pointOnWindow = new Point(screenPoint.X - thisWindow.Location.X, screenPoint.Y - thisWindow.Location.Y);

				var dpi = VisualTreeHelper.GetDpi(this);

				pointOnWindow.X /= dpi.DpiScaleX;
				pointOnWindow.Y /= dpi.DpiScaleY;

				SceneModel model = null;

				var element = VisualTreeHelper.HitTest(this, pointOnWindow)?.VisualHit;

				while (element is object)
				{
					if ((element as FrameworkElement)?.DataContext is SceneModel m)
					{
						model = m;
						break;
					}

					element = element.GetParentObject();
				}

				if (model is object)
				{
					SceneManager.MoveWindow(foregroundWindow, model.Scene).SafeFireAndForget();
				}
			});
		}

		public ObservableCollection<SceneModel> Scenes { get; } = new ObservableCollection<SceneModel>();

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

				var needsFocus = value == WindowMode.Flyover && _mode == WindowMode.OffScreen;

				_mode = value;

				if (needsFocus)
					Activate();

				ApplyWindowMode();
			}
		}

		private void ApplyWindowMode()
		{
			var newLeft = Mode == StageManager.WindowMode.OffScreen ? (-1 * Width) : 0.0;
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

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);

			if (e.Key == Key.Delete)
				Mode = Mode == WindowMode.OffScreen ? WindowMode.OnScreen : WindowMode.OffScreen;
		}

		private void _hook_MouseMoved(object? sender, MouseHookEventArgs e)
		{
			_mouse.X = e.Data.X;
			_mouse.Y = e.Data.Y;

			if (Mode == WindowMode.OffScreen && e.Data.X <= 44)
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
			bool doesOverlap(IWindowLocation loc) => loc.State == workspacer.WindowState.Maximized || (loc.State == workspacer.WindowState.Normal && (loc.X * 2) < _lastWidth);

			var anyOverlappingWindows = windows.Any(w => doesOverlap(w.Location));

			var containsMouse = _mouse.X <= _lastWidth;
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
	}

	public enum WindowMode
	{
		OnScreen,
		OffScreen,
		Flyover
	}
}
