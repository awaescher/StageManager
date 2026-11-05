using StageManager.Core;

namespace StageManager.Tests;

public sealed class SettingsTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "StageManagerTests", Guid.NewGuid().ToString("N"));

	private string FilePath => Path.Combine(_dir, "settings.json");

	public void Dispose()
	{
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	[Fact]
	public void Defaults_are_the_current_behaviour()
	{
		var s = new AppSettings().Normalize();

		Assert.Equal(DesktopIconsMode.Manage, s.DesktopIcons.Mode);
		Assert.True(s.DesktopIcons.RestoreOnExit);
		Assert.Equal(SidebarSide.Left, s.Sidebar.Side);
		Assert.Equal(6, s.Sidebar.MaxScenes);
		Assert.Equal(3, s.Sidebar.MaxWindowsPerScene);
		Assert.Equal(0, s.Sidebar.Width);
		Assert.Equal(0, s.Sidebar.ThumbnailWidth);
	}

	[Theory]
	[InlineData(0, 0, 0, 0)]
	[InlineData(200, 0, 0, 0)]
	[InlineData(0, 150, 0, 0)]
	[InlineData(200, 150, 200, 150)]
	[InlineData(5000, 10, 640, 48)]
	public void Thumbnail_size_needs_both_edges(int width, int height, int expectedWidth, int expectedHeight)
	{
		var s = new AppSettings { Sidebar = { ThumbnailWidth = width, ThumbnailHeight = height } }.Normalize();

		Assert.Equal(expectedWidth, s.Sidebar.ThumbnailWidth);
		Assert.Equal(expectedHeight, s.Sidebar.ThumbnailHeight);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(100, 160)]
	[InlineData(300, 300)]
	[InlineData(9000, 480)]
	public void Sidebar_width_is_zero_or_within_limits(int value, int expected)
	{
		Assert.Equal(expected, new AppSettings { Sidebar = { Width = value } }.Normalize().Sidebar.Width);
	}

	[Theory]
	[InlineData(0, 1)]
	[InlineData(3, 3)]
	[InlineData(50, 6)]
	public void Windows_per_scene_is_clamped(int value, int expected)
	{
		Assert.Equal(expected, new AppSettings { Sidebar = { MaxWindowsPerScene = value } }.Normalize().Sidebar.MaxWindowsPerScene);
	}

	[Theory]
	[InlineData(0.2, 1.0)]
	[InlineData(1.6, 1.6)]
	[InlineData(9, 2.5)]
	[InlineData(double.NaN, 1.6)]
	public void Aspect_is_clamped(double value, double expected)
	{
		Assert.Equal(expected, new AppSettings { Sidebar = { ThumbnailAspect = value } }.Normalize().Sidebar.ThumbnailAspect);
	}

	[Theory]
	[InlineData(-5, 1)]
	[InlineData(0, 1)]
	[InlineData(6, 6)]
	[InlineData(12, 12)]
	[InlineData(99, 12)]
	public void MaxScenes_is_clamped(int value, int expected)
	{
		var s = new AppSettings { Sidebar = { MaxScenes = value } }.Normalize();

		Assert.Equal(expected, s.Sidebar.MaxScenes);
	}

	[Fact]
	public void Unknown_side_falls_back_to_left()
	{
		var s = new AppSettings { Sidebar = { Side = (SidebarSide)42 } }.Normalize();

		Assert.Equal(SidebarSide.Left, s.Sidebar.Side);
	}

	[Fact]
	public void Unknown_desktop_icons_mode_falls_back_to_manage()
	{
		var s = new AppSettings { DesktopIcons = { Mode = (DesktopIconsMode)42 } }.Normalize();

		Assert.Equal(DesktopIconsMode.Manage, s.DesktopIcons.Mode);
	}

	[Fact]
	public void Desktop_icons_mode_is_written_as_text_and_read_back()
	{
		var store = new SettingsStore(FilePath);
		store.Save(new AppSettings { DesktopIcons = { Mode = DesktopIconsMode.AlwaysHidden, RestoreOnExit = false } });

		Assert.Contains("\"AlwaysHidden\"", File.ReadAllText(FilePath));

		var loaded = store.Load();
		Assert.Equal(DesktopIconsMode.AlwaysHidden, loaded.DesktopIcons.Mode);
		Assert.False(loaded.DesktopIcons.RestoreOnExit);
	}

	[Fact]
	public void Missing_file_is_created_with_defaults()
	{
		var loaded = new SettingsStore(FilePath).Load();

		Assert.Equal(SidebarSide.Left, loaded.Sidebar.Side);
		Assert.True(File.Exists(FilePath));
	}

	[Fact]
	public void Saved_settings_are_read_back_and_enums_are_written_as_text()
	{
		var store = new SettingsStore(FilePath);
		store.Save(new AppSettings { Sidebar = { Side = SidebarSide.Right, MaxScenes = 4 } });

		Assert.Contains("\"Right\"", File.ReadAllText(FilePath));

		var loaded = store.Load();
		Assert.Equal(SidebarSide.Right, loaded.Sidebar.Side);
		Assert.Equal(4, loaded.Sidebar.MaxScenes);
	}

	[Fact]
	public void Save_leaves_no_temp_file_behind()
	{
		new SettingsStore(FilePath).Save(new AppSettings());

		Assert.False(File.Exists(FilePath + ".tmp"));
	}

	[Fact]
	public void Partial_file_gets_defaults_for_missing_values()
	{
		Directory.CreateDirectory(_dir);
		File.WriteAllText(FilePath, """{ "Sidebar": { "Side": "Right" } }""");

		var loaded = new SettingsStore(FilePath).Load();

		Assert.Equal(SidebarSide.Right, loaded.Sidebar.Side);
		Assert.Equal(6, loaded.Sidebar.MaxScenes);
	}

	[Fact]
	public void Comments_and_trailing_commas_are_tolerated()
	{
		Directory.CreateDirectory(_dir);
		File.WriteAllText(FilePath, """
			{
				// my favourite side
				"Sidebar": { "Side": "Right", },
			}
			""");

		Assert.Equal(SidebarSide.Right, new SettingsStore(FilePath).Load().Sidebar.Side);
	}

	[Fact]
	public void Broken_file_is_backed_up_and_replaced_by_defaults()
	{
		Directory.CreateDirectory(_dir);
		File.WriteAllText(FilePath, "{ this is not json");
		var warnings = new List<string>();

		var loaded = new SettingsStore(FilePath, warnings.Add).Load();

		Assert.Equal(SidebarSide.Left, loaded.Sidebar.Side);
		Assert.True(File.Exists(FilePath + ".bad"));
		Assert.Equal("{ this is not json", File.ReadAllText(FilePath + ".bad"));
		Assert.Single(warnings);
	}

	[Fact]
	public void Unknown_enum_text_counts_as_broken_file()
	{
		Directory.CreateDirectory(_dir);
		File.WriteAllText(FilePath, """{ "Sidebar": { "Side": "Top" } }""");

		var loaded = new SettingsStore(FilePath).Load();

		Assert.Equal(SidebarSide.Left, loaded.Sidebar.Side);
		Assert.True(File.Exists(FilePath + ".bad"));
	}

	[Fact]
	public void Update_saves_and_raises_Changed_once()
	{
		var service = new SettingsService(new SettingsStore(FilePath));
		var raised = new List<AppSettings>();
		service.Changed += (_, s) => raised.Add(s);

		service.Update(s => s.Sidebar.Side = SidebarSide.Right);

		Assert.Single(raised);
		Assert.Equal(SidebarSide.Right, service.Current.Sidebar.Side);
		Assert.Equal(SidebarSide.Right, new SettingsStore(FilePath).Load().Sidebar.Side);
	}

	[Fact]
	public void Update_without_effective_change_does_not_raise_Changed()
	{
		var service = new SettingsService(new SettingsStore(FilePath));
		var raised = 0;
		service.Changed += (_, _) => raised++;

		service.Update(s => s.Sidebar.Side = SidebarSide.Left);
		service.Update(s => s.Sidebar.MaxScenes = 500);
		service.Update(s => s.Sidebar.MaxScenes = 500);

		Assert.Equal(1, raised);
		Assert.Equal(12, service.Current.Sidebar.MaxScenes);
	}

	[Fact]
	public void Update_does_not_modify_the_snapshot_handed_out_before()
	{
		var service = new SettingsService(new SettingsStore(FilePath));
		var before = service.Current;

		service.Update(s => s.Sidebar.Side = SidebarSide.Right);

		Assert.Equal(SidebarSide.Left, before.Sidebar.Side);
	}

	[Fact]
	public void Reset_restores_the_defaults_saves_and_raises_Changed()
	{
		var service = new SettingsService(new SettingsStore(FilePath));
		service.Update(s =>
		{
			s.Sidebar.Side = SidebarSide.Right;
			s.Sidebar.MaxScenes = 3;
			s.DesktopIcons.Mode = DesktopIconsMode.AlwaysHidden;
		});
		var raised = 0;
		service.Changed += (_, _) => raised++;

		service.Reset();

		Assert.Equal(1, raised);
		Assert.Equal(SidebarSide.Left, service.Current.Sidebar.Side);
		Assert.Equal(6, service.Current.Sidebar.MaxScenes);
		Assert.Equal(DesktopIconsMode.Manage, service.Current.DesktopIcons.Mode);
		Assert.Equal(SidebarSide.Left, new SettingsStore(FilePath).Load().Sidebar.Side);
	}

	[Fact]
	public void Reset_on_defaults_does_not_raise_Changed()
	{
		var service = new SettingsService(new SettingsStore(FilePath));
		var raised = 0;
		service.Changed += (_, _) => raised++;

		service.Reset();

		Assert.Equal(0, raised);
	}

	[Fact]
	public void Changed_hands_out_the_new_settings()
	{
		var service = new SettingsService(new SettingsStore(FilePath));
		AppSettings? received = null;
		service.Changed += (_, s) => received = s;

		service.Update(s => s.Sidebar.MaxScenes = 4);

		Assert.NotNull(received);
		Assert.Equal(4, received!.Sidebar.MaxScenes);
	}}
