using AsyncAwaitBestPractices;
using MahApps.Metro.Controls;
using Microsoft.Xaml.Behaviors.Core;
using SharpHook;
using StageManager.Model;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using workspacer;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private IntPtr _thisHandle;
		private TaskPoolGlobalHook _hook;

		public MainWindow()
		{
			InitializeComponent();

			this.DataContext = this;

			SwitchSceneCommand = new ActionCommand(async model => await SceneManager!.SwitchTo(((SceneModel)model).Scene));
		}

		protected override void OnInitialized(EventArgs e)
		{
			base.OnInitialized(e);

			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

			_hook = new TaskPoolGlobalHook();
			_hook.MouseReleased += OnMouseReleased;

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
			SceneManager.RequestWindowPreviewUpdate += SceneManager_RequestWindowPreviewUpdate;

			foreach (var scene in SceneManager.GetScenes())
			{
				var model = SceneModel.FromScene(scene);
				Scenes.Add(model);
			}
		}

		private void SceneManager_RequestWindowPreviewUpdate(object? sender, IWindow window)
		{
			var toUpdate = Scenes
				.Select(s => s.Windows.FirstOrDefault(w => w.Handle == window.Handle))
				.Where(w => w is object)
				.FirstOrDefault();

			if (toUpdate is object)
				toUpdate.UpdatePreview();
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

		private void SceneManager_SceneChanged(object sender, SceneChangeEventArgs e)
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

		private void OnMouseReleased(object? sender, MouseHookEventArgs e)
		{
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
	}
}
