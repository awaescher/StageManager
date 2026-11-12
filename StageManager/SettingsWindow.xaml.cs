using StageManager.Core;
using StageManager.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

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
		/// <summary>An app in one of the lists: what the user sees of it.</summary>
		private sealed record AppRow(string Entry, string Name, string Detail, ImageSource? Icon);

		private static readonly double[] Speeds = { 0.5, 0.75, 1.0, 1.5, 2.0, 3.0 };

		private static SettingsWindow? _instance;

		private bool _loading;

		private SettingsService Settings => App.Settings;

		public SettingsWindow()
		{
			InitializeComponent();

			Loaded += (_, _) =>
			{
				Settings.Changed += Settings_Changed;
				Loc.LanguageChanged += Loc_LanguageChanged;

				if (App.Hotkeys is object)
					App.Hotkeys.ConflictsChanged += Hotkeys_ConflictsChanged;
			};
			Closed += (_, _) =>
			{
				Settings.Changed -= Settings_Changed;
				Loc.LanguageChanged -= Loc_LanguageChanged;

				if (App.Hotkeys is object)
				{
					App.Hotkeys.ConflictsChanged -= Hotkeys_ConflictsChanged;
					App.Hotkeys.Suspended = false;
				}

				_instance = null;
			};

			nav.SelectedIndex = 0;
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

		private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var tag = (nav.SelectedItem as ListBoxItem)?.Tag as string;

			pageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
			pageSidebar.Visibility = tag == "sidebar" ? Visibility.Visible : Visibility.Collapsed;
			pageAnimations.Visibility = tag == "animations" ? Visibility.Visible : Visibility.Collapsed;
			pageKeyboard.Visibility = tag == "keyboard" ? Visibility.Visible : Visibility.Collapsed;
			pageApps.Visibility = tag == "apps" ? Visibility.Visible : Visibility.Collapsed;
			pageIcons.Visibility = tag == "icons" ? Visibility.Visible : Visibility.Collapsed;
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

		private void Loc_LanguageChanged(object? sender, EventArgs e)
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

				var languages = new List<Choice<string>> { new(Loc.Get("Language.System"), "") };
				languages.AddRange(Languages.All.Select(l => new Choice<string>(l.NativeName, l.Code)));
				Fill(language, languages);
				Select(language, current.Language);

				autostart.IsChecked = MainWindow.StartsWithWindows;

				Fill(hideStrategy, new[]
				{
					new Choice<HideStrategy>(Loc.Get("Hide.Park"), HideStrategy.Park),
					new Choice<HideStrategy>(Loc.Get("Hide.Minimize"), HideStrategy.Minimize)
				});
				Select(hideStrategy, current.HideStrategy);

				Fill(side, new[]
				{
					new Choice<SidebarSide>(Loc.Get("Common.Left"), SidebarSide.Left),
					new Choice<SidebarSide>(Loc.Get("Common.Right"), SidebarSide.Right)
				});
				Select(side, sidebar.Side);

				// the values the user can choose from, plus the current one if it was set by hand in settings.json
				Fill(sceneCount, SceneCounts.Append(sidebar.MaxScenes).Distinct().OrderBy(c => c)
					.Select(c => new Choice<int>(c.ToString(), c)));
				Select(sceneCount, sidebar.MaxScenes);

				Fill(windowsPerScene, Enumerable.Range(SidebarSettings.MinWindowsPerScene, SidebarSettings.MaxWindowsPerSceneLimit)
					.Select(c => new Choice<int>(c == 1 ? Loc.Get("Sidebar.NoStack") : c.ToString(), c)));
				Select(windowsPerScene, sidebar.MaxWindowsPerScene);

				var sizes = new List<Choice<(int, int)>> { new(Loc.Get("Sidebar.AutoThumbnail"), (0, 0)) };
				sizes.AddRange(ThumbnailSizes.Select(s => new Choice<(int, int)>($"{s.Width} x {s.Height}", (s.Width, s.Height))));
				var currentSize = (sidebar.ThumbnailWidth, sidebar.ThumbnailHeight);
				if (currentSize != (0, 0) && !sizes.Any(s => s.Value == currentSize))
					sizes.Add(new Choice<(int, int)>($"{currentSize.ThumbnailWidth} x {currentSize.ThumbnailHeight}", currentSize));
				Fill(thumbnailSize, sizes);
				Select(thumbnailSize, currentSize);

				var widths = new List<Choice<int>> { new(Loc.Get("Sidebar.AutoWidth"), 0) };
				widths.AddRange(SidebarWidths.Append(sidebar.Width).Where(w => w > 0).Distinct().OrderBy(w => w)
					.Select(w => new Choice<int>($"{w}", w)));
				Fill(sidebarWidth, widths);
				Select(sidebarWidth, sidebar.Width);
				dragToStage.IsChecked = sidebar.DragToStage;

				Fill(iconsMode, new[]
				{
					new Choice<DesktopIconsMode>(Loc.Get("Icons.Manage"), DesktopIconsMode.Manage),
					new Choice<DesktopIconsMode>(Loc.Get("Icons.Hidden"), DesktopIconsMode.AlwaysHidden),
					new Choice<DesktopIconsMode>(Loc.Get("Icons.Untouched"), DesktopIconsMode.Untouched)
				});
				Select(iconsMode, current.DesktopIcons.Mode);
				iconsRestore.IsChecked = current.DesktopIcons.RestoreOnExit;

				Fill(animationMode, new[]
				{
					new Choice<AnimationMode>(Loc.Get("Anim.Follow"), AnimationMode.System),
					new Choice<AnimationMode>(Loc.Get("Anim.Always"), AnimationMode.On),
					new Choice<AnimationMode>(Loc.Get("Anim.Never"), AnimationMode.Off)
				});
				Select(animationMode, current.Animations.Mode);

				// a speed set by hand in settings.json stays selectable
				Fill(animationSpeed, Speeds.Append(current.Animations.Speed).Distinct().OrderBy(s => s)
					.Select(s => new Choice<double>(s == 1.0 ? Loc.Get("Anim.Normal") : $"{s:0.##}x", s)));
				Select(animationSpeed, current.Animations.Speed);
				// the setting says "frame if Windows shows frames", the switch says the opposite: animate the contents anyway
				animationForce.IsChecked = !current.Animations.FrameIfWindowContentIsOff;

				hotkeysEnabled.IsChecked = current.Hotkeys.Enabled;
				hotkeyKeys.IsEnabled = current.Hotkeys.Enabled;
				if (HotkeyText.TryParse(current.Hotkeys.Keys, out var keys))
					hotkeyKeys.Keys = keys;

				UpdateAnimationInfo();
				UpdateLayoutInfo();
				UpdateHotkeyInfo();
				LoadExcludedApps();
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
			var screen = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
			var areaHeight = screen.Height / dpi;
			var layout = SidebarLayout.Calculate(sidebar, areaHeight);

			layoutInfo.Text = Loc.Format("Sidebar.PreviewInfo", layout.ThumbnailWidth.ToString("0"), layout.ThumbnailHeight.ToString("0"),
				layout.SidebarWidth.ToString("0"), areaHeight.ToString("0"));

			layoutWarningBox.Visibility = layout.IsLimited ? Visibility.Visible : Visibility.Collapsed;
			layoutWarning.Text = layout.IsLimited
				? Loc.Format("Sidebar.Limited", layout.EffectiveMaxScenes, sidebar.MaxScenes)
				: "";

			DrawPreview(sidebar, layout, screen.Width / dpi, areaHeight);
		}

		/// <summary>A small picture of the screen with the sidebar, drawn to scale from the same numbers the real sidebar uses.</summary>
		private void DrawPreview(SidebarSettings sidebar, SidebarLayoutResult layout, double screenWidth, double screenHeight)
		{
			// as large as the page allows, the shape of the screen stays
			var scale = Math.Min(150 / screenHeight, 360 / screenWidth);
			var height = Math.Round(screenHeight * scale);
			var width = Math.Round(screenWidth * scale);

			previewCanvas.Children.Clear();
			previewCanvas.Width = width;
			previewCanvas.Height = height;

			var accent = (Brush)FindResource("AccentFillColorDefaultBrush");
			var stroke = (Brush)FindResource("ControlStrongStrokeColorDefaultBrush");
			var fill = (Brush)FindResource("ControlFillColorDefaultBrush");
			var muted = (Brush)FindResource("ControlStrongFillColorDefaultBrush");

			var right = sidebar.Side == SidebarSide.Right;
			var barWidth = layout.SidebarWidth * scale;

			void Add(UIElement element, double x, double y)
			{
				Canvas.SetLeft(element, x);
				Canvas.SetTop(element, y);
				previewCanvas.Children.Add(element);
			}

			// the window of the stage
			var margin = 14 * scale * 4;
			var stageLeft = (right ? 0 : barWidth) + margin;
			var stageWidth = width - barWidth - 2 * margin;
			Add(new Rectangle { Width = Math.Max(4, stageWidth), Height = height - 2 * margin, RadiusX = 3, RadiusY = 3, Fill = fill, Stroke = stroke, StrokeThickness = 1 }, stageLeft, margin);
			Add(new Rectangle { Width = Math.Max(4, stageWidth), Height = Math.Min(6, height / 10), RadiusX = 3, RadiusY = 3, Fill = muted, Opacity = 0.5 }, stageLeft, margin);

			// the cards
			var shown = Math.Min(sidebar.MaxScenes, layout.EffectiveMaxScenes);
			var cardWidth = layout.ThumbnailWidth * scale;
			var cardHeight = layout.ThumbnailHeight * scale;
			var step = (layout.ThumbnailHeight + SidebarLayout.IconRowHeight + layout.CardGap) * scale;
			var total = shown * step - layout.CardGap * scale;
			var top = Math.Max(2, (height - total) / 2);
			var cardLeft = right
				? width - (SidebarLayout.EdgePadding * scale) - cardWidth
				: SidebarLayout.EdgePadding * scale;
			var stack = Math.Min(sidebar.MaxWindowsPerScene, 3);

			for (var i = 0; i < shown; i++)
			{
				var y = top + i * step;

				for (var s = stack - 1; s >= 0; s--)
				{
					var offset = s * 2.2;
					Add(new Rectangle
					{
						Width = Math.Max(3, cardWidth - (stack - 1) * 2.2),
						Height = Math.Max(3, cardHeight - (stack - 1) * 2.2),
						RadiusX = 2,
						RadiusY = 2,
						Fill = fill,
						Stroke = i == 0 ? accent : stroke,
						StrokeThickness = i == 0 ? 1.5 : 1
					}, cardLeft + (right ? -offset : offset), y + (stack - 1) * 2.2 - offset);
				}

				Add(new Ellipse { Width = 4, Height = 4, Fill = muted }, cardLeft + (right ? cardWidth - 5 : 1), y + cardHeight + 3);
			}

			previewFrame.Width = width + 2;
			previewFrame.Height = height + 2;
		}

		private void Change(Action<AppSettings> change)
		{
			if (_loading)
				return;

			Settings.Update(change);
		}

		private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (language.SelectedItem is Choice<string> c)
				Change(s => s.Language = c.Value);
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

			// where Windows shows the windows while dragging, there is nothing to choose
			animationForceRow.Visibility = AnimationPolicy.OffersContentChoice(system) ? Visibility.Visible : Visibility.Collapsed;

			var result = style switch
			{
				FlightStyle.Windows => Loc.Get("Anim.EffectWindows"),
				FlightStyle.Frames => Loc.Get("Anim.EffectFrames"),
				_ => Loc.Get("Anim.EffectNone")
			};

			var rows = new (string Label, string Value, string? Tone)[]
			{
				(Loc.Get("Anim.StatusAnimations"), Loc.Get(system.AnimationsEnabled ? "Anim.On" : "Anim.Off"), system.AnimationsEnabled ? "ok" : "off"),
				(Loc.Get("Anim.StatusContents"), Loc.Get(system.DragFullWindows ? "Anim.Shown" : "Anim.HiddenFrame"), system.DragFullWindows ? "ok" : "off"),
				(Loc.Get("Anim.StatusResult"), result, style == FlightStyle.None ? "off" : "ok")
			};

			animationStatus.Children.Clear();
			animationStatus.RowDefinitions.Clear();
			animationStatus.ColumnDefinitions.Clear();
			animationStatus.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			animationStatus.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });

			for (var i = 0; i < rows.Length; i++)
			{
				animationStatus.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

				var label = new TextBlock
				{
					Text = rows[i].Label,
					Margin = new Thickness(0, 5, 12, 5),
					TextWrapping = TextWrapping.Wrap,
					Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
				};
				Grid.SetRow(label, i);
				animationStatus.Children.Add(label);

				var dot = new Ellipse
				{
					Width = 8,
					Height = 8,
					Margin = new Thickness(0, 0, 8, 0),
					VerticalAlignment = VerticalAlignment.Center,
					Fill = (Brush)FindResource(rows[i].Tone == "ok" ? "SystemFillColorSuccessBrush" : "SystemFillColorNeutralBrush")
				};
				var value = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
				value.Children.Add(dot);
				value.Children.Add(new TextBlock { Text = rows[i].Value, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 });
				Grid.SetRow(value, i);
				Grid.SetColumn(value, 1);
				animationStatus.Children.Add(value);
			}
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

		private void AnimationForce_Click(object sender, RoutedEventArgs e)
		{
			var force = animationForce.IsChecked == true;
			Change(s => s.Animations.FrameIfWindowContentIsOff = !force);
		}

		private void HotkeysEnabled_Click(object sender, RoutedEventArgs e)
		{
			var enabled = hotkeysEnabled.IsChecked == true;
			Change(s => s.Hotkeys.Enabled = enabled);
		}

		private void HotkeyKeys_KeysChosen(object? sender, HotkeyModifiers keys)
		{
			Change(s => s.Hotkeys.Keys = HotkeyText.Format(keys));
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

			BuildCheatSheet(settings.Enabled, keys);

			var lines = new List<string>();

			if (settings.Enabled)
			{
				var failed = App.Hotkeys?.Failed ?? Array.Empty<HotkeyAction>();
				foreach (var conflict in HotkeyAdvice.Conflicts(keys, failed, Loc.KeyName))
					lines.Add(Loc.Format("Keyboard.Conflict", conflict.Keys, Loc.Get("Owner." + conflict.Owner)));

				var warning = HotkeyAdvice.Warning(keys);
				if (warning != HotkeyWarning.None)
					lines.Add(Loc.Get("Warn." + warning));
			}

			hotkeyWarningBox.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
			hotkeyWarning.Text = string.Join(Environment.NewLine, lines);
		}

		/// <summary>The keys of every action, written with the names on the keyboard of the user.</summary>
		private void BuildCheatSheet(bool enabled, HotkeyModifiers keys)
		{
			cheatSheet.Children.Clear();
			cheatSheet.Opacity = enabled ? 1 : 0.5;

			if (!enabled)
			{
				cheatSheet.Children.Add(new TextBlock { Text = Loc.Get("Keyboard.Off"), TextWrapping = TextWrapping.Wrap });
				return;
			}

			var rows = new (string Keys, string Text)[]
			{
				(HotkeyText.Describe(keys, HotkeyAction.Next, Loc.KeyName), Loc.Get("Keyboard.Next")),
				(HotkeyText.Describe(keys, HotkeyAction.Previous, Loc.KeyName), Loc.Get("Keyboard.Previous")),
				(HotkeyText.Format(keys, Loc.KeyName) + "+1...9", Loc.Get("Keyboard.Card")),
				(Loc.KeyName("Esc"), Loc.Get("Keyboard.Cancel"))
			};

			foreach (var (text, description) in rows)
			{
				var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
				row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

				row.Children.Add(new TextBlock { Text = description, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });

				var cap = new Border
				{
					Padding = new Thickness(10, 3, 10, 3),
					CornerRadius = new CornerRadius(4),
					BorderThickness = new Thickness(1),
					BorderBrush = (Brush)FindResource("ControlStrongStrokeColorDefaultBrush"),
					Background = (Brush)FindResource("ControlFillColorDefaultBrush"),
					Child = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas, Cascadia Mono"), FontSize = 13 }
				};
				Grid.SetColumn(cap, 1);
				row.Children.Add(cap);

				cheatSheet.Children.Add(row);
			}

			cheatSheet.Children.Add(new TextBlock
			{
				Text = Loc.Get("Keyboard.Release"),
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0, 10, 0, 0),
				Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
			});
		}

		// the apps that are left alone. The list holds file names, what is shown is the name and the icon of the app if it is running

		private void LoadExcludedApps()
		{
			var entries = Settings.Current.Stage.ExcludedApps;
			var running = RunningByFileName();

			var rows = entries
				.Select(entry => RowFor(entry, running))
				.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();

			var selected = appsList.SelectedItems.OfType<AppRow>().Select(r => r.Entry).ToHashSet(StringComparer.OrdinalIgnoreCase);
			appsList.ItemsSource = rows;

			foreach (var row in rows.Where(r => selected.Contains(r.Entry)))
				appsList.SelectedItems.Add(row);

			appsEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
		}

		private static Dictionary<string, RunningApp> RunningByFileName()
		{
			try
			{
				return AppInfo.Running()
					.GroupBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
			}
			catch (Exception ex)
			{
				Log.Warn($"The running apps could not be listed: {ex.Message}");
				return new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
			}
		}

		private static AppRow RowFor(string entry, IReadOnlyDictionary<string, RunningApp> running)
		{
			var fileName = ExcludedApps.EntryFor(entry);
			var match = running.TryGetValue(fileName, out var app) ? app
				: running.Values.FirstOrDefault(a => ExcludedApps.Contains(new[] { entry }, a.Path));

			if (match is not null)
				return new AppRow(entry, match.Name, match.Path, AppInfo.IconOf(match.Path));

			// an app that is not running: Windows still knows where it is installed
			var path = entry.Contains('\\') || entry.Contains('/') ? entry : AppInfo.ResolvePath(entry);
			var name = path is null ? fileName : AppInfo.DisplayNameOf(path, fileName);
			return new AppRow(entry, name, path ?? "", AppInfo.IconOf(path));
		}

		private void AppsList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == System.Windows.Input.Key.Delete && appsList.SelectedItems.Count > 0)
			{
				e.Handled = true;
				Include(appsList.SelectedItems.OfType<AppRow>().Select(r => r.Entry).ToList());
			}
		}

		/// <summary>The trash can of a row.</summary>
		private void AppsDelete_Click(object sender, RoutedEventArgs e)
		{
			e.Handled = true;

			if (sender is FrameworkElement { DataContext: AppRow row })
				Include(new List<string> { row.Entry });
		}

		/// <summary>Takes apps off the list: StageManager manages their windows again.</summary>
		private void Include(IReadOnlyList<string> entries)
		{
			if (entries.Count == 0)
				return;

			Change(s =>
			{
				foreach (var entry in entries)
					s.Stage.ExcludedApps = ExcludedApps.Without(s.Stage.ExcludedApps, entry);
			});
		}

		private void AppsAdd_Click(object sender, RoutedEventArgs e)
		{
			var excluded = Settings.Current.Stage.ExcludedApps;
			var apps = RunningByFileName().Values
				.Where(a => !ExcludedApps.Contains(excluded, a.Path))
				.Select(a => new AppRow(ExcludedApps.EntryFor(a.Path), a.Name, a.Path, AppInfo.IconOf(a.Path)))
				.ToList();

			appsRunning.ItemsSource = apps;
			appsRunning.SelectedItem = null;
			appsRunningEmpty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			appsPicker.IsOpen = true;
		}

		private void AppsRunning_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (appsRunning.SelectedItem is not AppRow row)
				return;

			appsPicker.IsOpen = false;
			Exclude(row.Entry);
		}

		private void AppsBrowse_Click(object sender, RoutedEventArgs e)
		{
			appsPicker.IsOpen = false;

			var dialog = new Microsoft.Win32.OpenFileDialog
			{
				Title = Loc.Get("Apps.Browse"),
				Filter = Loc.Get("Apps.Filter") + "|*.exe",
				CheckFileExists = true
			};

			if (dialog.ShowDialog(this) == true)
				Exclude(ExcludedApps.EntryFor(dialog.FileName));
		}

		private void Exclude(string entry)
		{
			if (string.IsNullOrWhiteSpace(entry))
				return;

			Change(s => s.Stage.ExcludedApps = ExcludedApps.With(s.Stage.ExcludedApps, entry));
		}

		private void Reset_Click(object sender, RoutedEventArgs e)
		{
			Settings.Reset();
		}
	}
}
