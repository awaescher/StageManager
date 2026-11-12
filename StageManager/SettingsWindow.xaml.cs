using StageManager.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for SettingsWindow.xaml
	/// </summary>
	public partial class SettingsWindow : Window
	{
		private sealed record Choice<T>(string Text, T Value)
		{
			public override string ToString() => Text;
		}

		private static readonly int[] SceneCounts = { 1, 2, 3, 4, 5, 6, 7, 8, 10, 12 };
		private static readonly int[] SidebarWidths = { 180, 200, 240, 280, 320, 400, 480 };
		private static readonly (int Width, int Height)[] ThumbnailSizes = { (128, 80), (160, 100), (192, 120), (240, 150), (288, 180) };

		private static SettingsWindow? _instance;

		private bool _loading;

		private SettingsService Settings => App.Settings;

		public SettingsWindow()
		{
			InitializeComponent();

			Fill(side, new[]
			{
				new Choice<SidebarSide>("Left", SidebarSide.Left),
				new Choice<SidebarSide>("Right", SidebarSide.Right)
			});

			Fill(hideStrategy, new[]
			{
				new Choice<HideStrategy>("Moving them out of the screen (recommended)", HideStrategy.Park),
				new Choice<HideStrategy>("Minimizing them", HideStrategy.Minimize)
			});

			Fill(iconsMode, new[]
			{
				new Choice<DesktopIconsMode>("Automatic: hide while a scene is active, show in the desktop view", DesktopIconsMode.Manage),
				new Choice<DesktopIconsMode>("Always hidden", DesktopIconsMode.AlwaysHidden),
				new Choice<DesktopIconsMode>("Never change", DesktopIconsMode.Untouched)
			});

			Fill(animationMode, new[]
			{
				new Choice<AnimationMode>("Follow Windows (default)", AnimationMode.System),
				new Choice<AnimationMode>("Always", AnimationMode.On),
				new Choice<AnimationMode>("Never", AnimationMode.Off)
			});

			Fill(animationSpeed, new[] { 0.5, 0.75, 1.0, 1.5, 2.0, 3.0 }
				.Select(s => new Choice<double>(s == 1.0 ? "Normal" : $"{s:0.##}x", s)));

			Loaded += (_, _) =>
			{
				Settings.Changed += Settings_Changed;

				if (App.Hotkeys is object)
					App.Hotkeys.ConflictsChanged += Hotkeys_ConflictsChanged;
			};
			Closed += (_, _) =>
			{
				Settings.Changed -= Settings_Changed;

				if (App.Hotkeys is object)
				{
					App.Hotkeys.ConflictsChanged -= Hotkeys_ConflictsChanged;
					App.Hotkeys.Suspended = false;
				}

				_instance = null;
			};

			LoadFromSettings();
		}

		/// <summary>Opens the window or brings the one that is already open to the front.</summary>
		public static void ShowSingle()
		{
			if (_instance is null)
				_instance = new SettingsWindow();

			_instance.Show();

			if (_instance.WindowState == WindowState.Minimized)
				_instance.WindowState = WindowState.Normal;

			_instance.Activate();
		}

		private static void Fill<T>(ComboBox box, IEnumerable<Choice<T>> choices)
		{
			box.Items.Clear();
			foreach (var choice in choices)
				box.Items.Add(choice);
		}

		private static void Select<T>(ComboBox box, T value) where T : notnull
		{
			box.SelectedItem = box.Items.OfType<Choice<T>>().FirstOrDefault(c => EqualityComparer<T>.Default.Equals(c.Value, value));
		}

		private void Settings_Changed(object? sender, AppSettings e)
		{
			Dispatcher.BeginInvoke(LoadFromSettings);
		}

		private void LoadFromSettings()
		{
			_loading = true;
			try
			{
				var current = Settings.Current;
				var sidebar = current.Sidebar;

				autostart.IsChecked = MainWindow.StartsWithWindows;
				Select(hideStrategy, current.HideStrategy);
				Select(side, sidebar.Side);

				// the values the user can choose from, plus the current one if it was set by hand in settings.json
				Fill(sceneCount, SceneCounts.Append(sidebar.MaxScenes).Distinct().OrderBy(c => c)
					.Select(c => new Choice<int>(c.ToString(), c)));
				Select(sceneCount, sidebar.MaxScenes);

				Fill(windowsPerScene, Enumerable.Range(SidebarSettings.MinWindowsPerScene, SidebarSettings.MaxWindowsPerSceneLimit)
					.Select(c => new Choice<int>(c == 1 ? "1 (no stack)" : c.ToString(), c)));
				Select(windowsPerScene, sidebar.MaxWindowsPerScene);

				var sizes = new List<Choice<(int, int)>> { new("Automatic (fit the number of scenes)", (0, 0)) };
				sizes.AddRange(ThumbnailSizes.Select(s => new Choice<(int, int)>($"{s.Width} x {s.Height}", (s.Width, s.Height))));
				var currentSize = (sidebar.ThumbnailWidth, sidebar.ThumbnailHeight);
				if (currentSize != (0, 0) && !sizes.Any(s => s.Value == currentSize))
					sizes.Add(new Choice<(int, int)>($"{currentSize.ThumbnailWidth} x {currentSize.ThumbnailHeight}", currentSize));
				Fill(thumbnailSize, sizes);
				Select(thumbnailSize, currentSize);

				var widths = new List<Choice<int>> { new("Automatic (fit the thumbnails)", 0) };
				widths.AddRange(SidebarWidths.Append(sidebar.Width).Where(w => w > 0).Distinct().OrderBy(w => w)
					.Select(w => new Choice<int>($"{w}", w)));
				Fill(sidebarWidth, widths);
				Select(sidebarWidth, sidebar.Width);
				dragToStage.IsChecked = sidebar.DragToStage;

				Select(iconsMode, current.DesktopIcons.Mode);
				iconsRestore.IsChecked = current.DesktopIcons.RestoreOnExit;

				Select(animationMode, current.Animations.Mode);

				// a speed set by hand in settings.json stays selectable
				var speeds = new[] { 0.5, 0.75, 1.0, 1.5, 2.0, 3.0 }.Append(current.Animations.Speed).Distinct().OrderBy(s => s)
					.Select(s => new Choice<double>(s == 1.0 ? "Normal" : $"{s:0.##}x", s));
				Fill(animationSpeed, speeds);
				Select(animationSpeed, current.Animations.Speed);
				animationFrames.IsChecked = current.Animations.FrameIfWindowContentIsOff;

				hotkeysEnabled.IsChecked = current.Hotkeys.Enabled;
				hotkeyKeys.IsEnabled = current.Hotkeys.Enabled;
				if (HotkeyText.TryParse(current.Hotkeys.Keys, out var keys))
					hotkeyKeys.Keys = keys;
				UpdateHotkeyInfo();

				UpdateAnimationInfo();

				UpdateLayoutInfo();
			}
			finally
			{
				_loading = false;
			}
		}

		private void UpdateLayoutInfo()
		{
			var sidebar = Settings.Current.Sidebar;
			var dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
			var areaHeight = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea.Height / dpi;
			var layout = SidebarLayout.Calculate(sidebar, areaHeight);

			layoutInfo.Text = $"Preview: thumbnails {layout.ThumbnailWidth:0} x {layout.ThumbnailHeight:0}, sidebar {layout.SidebarWidth:0} wide, calculated for a screen height of {areaHeight:0}.";

			layoutWarning.Visibility = layout.IsLimited ? Visibility.Visible : Visibility.Collapsed;
			layoutWarning.Text = layout.IsLimited
				? $"With this size only {layout.EffectiveMaxScenes} of {sidebar.MaxScenes} scenes fit on this screen. The others stay managed but are not shown in the sidebar."
				: "";
		}

		private void Change(Action<AppSettings> change)
		{
			if (_loading)
				return;

			Settings.Update(change);
		}

		private void Autostart_Click(object sender, RoutedEventArgs e)
		{
			MainWindow.StartsWithWindows = autostart.IsChecked == true;
		}

		private void HideStrategy_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (hideStrategy.SelectedItem is Choice<HideStrategy> c)
				Change(s => s.HideStrategy = c.Value);
		}

		private void Side_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (side.SelectedItem is Choice<SidebarSide> c)
				Change(s => s.Sidebar.Side = c.Value);
		}

		private void SceneCount_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (sceneCount.SelectedItem is Choice<int> c)
				Change(s => s.Sidebar.MaxScenes = c.Value);
		}

		private void WindowsPerScene_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (windowsPerScene.SelectedItem is Choice<int> c)
				Change(s => s.Sidebar.MaxWindowsPerScene = c.Value);
		}

		private void ThumbnailSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (thumbnailSize.SelectedItem is Choice<(int, int)> c)
				Change(s =>
				{
					s.Sidebar.ThumbnailWidth = c.Value.Item1;
					s.Sidebar.ThumbnailHeight = c.Value.Item2;
				});
		}

		private void SidebarWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (sidebarWidth.SelectedItem is Choice<int> c)
				Change(s => s.Sidebar.Width = c.Value);
		}

		private void DragToStage_Click(object sender, RoutedEventArgs e)
		{
			var enabled = dragToStage.IsChecked == true;
			Change(s => s.Sidebar.DragToStage = enabled);
		}

		private void IconsMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (iconsMode.SelectedItem is Choice<DesktopIconsMode> c)
				Change(s => s.DesktopIcons.Mode = c.Value);
		}

		private void IconsRestore_Click(object sender, RoutedEventArgs e)
		{
			var restore = iconsRestore.IsChecked == true;
			Change(s => s.DesktopIcons.RestoreOnExit = restore);
		}

		private void UpdateAnimationInfo()
		{
			var system = Native.SystemAnimation.Read();
			var style = AnimationPolicy.Decide(Settings.Current.Animations, system);

			var effect = style switch
			{
				FlightStyle.Windows => "The windows fly between the sidebar and the stage.",
				FlightStyle.Frames => "A frame flies in place of the windows.",
				_ => "Scenes switch without animation."
			};

			animationInfo.Text = $"Windows: animations {(system.AnimationsEnabled ? "on" : "off")}, window content while dragging {(system.DragFullWindows ? "shown" : "hidden (frame only)")}. {effect}";
		}

		private void AnimationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (animationMode.SelectedItem is Choice<AnimationMode> c)
				Change(s => s.Animations.Mode = c.Value);
		}

		private void AnimationSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (animationSpeed.SelectedItem is Choice<double> c)
				Change(s => s.Animations.Speed = c.Value);
		}

		private void AnimationFrames_Click(object sender, RoutedEventArgs e)
		{
			var frames = animationFrames.IsChecked == true;
			Change(s => s.Animations.FrameIfWindowContentIsOff = frames);
		}

		private void HotkeysEnabled_Click(object sender, RoutedEventArgs e)
		{
			var enabled = hotkeysEnabled.IsChecked == true;
			Change(s => s.Hotkeys.Enabled = enabled);
		}

		private void HotkeyKeys_KeysChosen(object? sender, HotkeyModifiers keys)
		{
			Change(s => s.Hotkeys.Keys = HotkeyText.Format(keys));
			UpdateHotkeyInfo();
		}

		// the registered keys would swallow the combination the user is about to press
		private void HotkeyKeys_CaptureStarted(object? sender, EventArgs e)
		{
			if (App.Hotkeys is object)
				App.Hotkeys.Suspended = true;
		}

		private void HotkeyKeys_CaptureEnded(object? sender, EventArgs e)
		{
			if (App.Hotkeys is object)
				App.Hotkeys.Suspended = false;

			UpdateHotkeyInfo();
		}

		private void Hotkeys_ConflictsChanged(object? sender, EventArgs e)
		{
			Dispatcher.BeginInvoke(UpdateHotkeyInfo);
		}

		private void UpdateHotkeyInfo()
		{
			var settings = Settings.Current.Hotkeys;

			if (!HotkeyText.TryParse(settings.Keys, out var keys))
				keys = HotkeyText.Default;

			var text = HotkeyText.Format(keys);
			hotkeyInfo.Text = settings.Enabled
				? $"Hold {text} and press Tab for the next scene, Shift+Tab for the previous one, or 1...9 for the card at that position. The scene opens when you let go, Esc cancels."
				: "The keyboard shortcuts are off.";

			var lines = new List<string>();

			if (settings.Enabled)
			{
				lines.AddRange(App.Hotkeys?.Conflicts ?? Array.Empty<string>());

				if (HotkeyAdvice.Warning(keys) is string warning)
					lines.Add(warning);
			}

			hotkeyWarning.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
			hotkeyWarning.Text = string.Join(Environment.NewLine, lines);
		}

		private void Reset_Click(object sender, RoutedEventArgs e)
		{
			Settings.Reset();
		}
	}
}
