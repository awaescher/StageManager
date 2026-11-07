using StageManager.Core;

namespace StageManager.Tests;

public class WindowFilterTests
{
	private static WindowFacts Normal(string cls = "Chrome_WidgetWin_1", string process = "chrome") =>
		new(cls, process, IsRoot: true, IsMinimized: false, Width: 800, Height: 600);

	[Fact]
	public void An_ordinary_window_is_a_candidate()
	{
		Assert.True(WindowFilter.IsCandidate(Normal()));
	}

	[Fact]
	public void Child_windows_are_ignored()
	{
		Assert.False(WindowFilter.IsCandidate(Normal() with { IsRoot = false }));
	}

	[Theory]
	[InlineData("Progman")]
	[InlineData("WorkerW")]
	[InlineData("Shell_TrayWnd")]
	[InlineData("Shell_SecondaryTrayWnd")]
	[InlineData("Windows.UI.Core.CoreWindow")]
	[InlineData("XamlExplorerHostIslandWindow")]
	[InlineData("DesktopWindowContentBridge")]
	[InlineData("TaskManagerWindow")]
	[InlineData("shell_traywnd")]
	public void Shell_classes_are_ignored(string cls)
	{
		Assert.False(WindowFilter.IsCandidate(Normal(cls)));
	}

	[Theory]
	[InlineData("SearchHost")]
	[InlineData("StartMenuExperienceHost")]
	[InlineData("ShellExperienceHost")]
	[InlineData("searchhost")]
	public void Shell_processes_are_ignored(string process)
	{
		Assert.False(WindowFilter.IsCandidate(Normal(process: process)));
	}

	[Theory]
	[InlineData(39, 600)]
	[InlineData(800, 29)]
	[InlineData(0, 0)]
	public void Tiny_windows_are_ignored(int width, int height)
	{
		Assert.False(WindowFilter.IsCandidate(Normal() with { Width = width, Height = height }));
	}

	[Fact]
	public void The_minimum_size_is_still_accepted()
	{
		Assert.True(WindowFilter.IsCandidate(Normal() with { Width = 40, Height = 30 }));
	}

	[Fact]
	public void Minimized_windows_are_not_judged_by_their_caption_bar_size()
	{
		Assert.True(WindowFilter.IsCandidate(Normal() with { IsMinimized = true, Width = 160, Height = 28 }));
		Assert.True(WindowFilter.IsCandidate(Normal() with { IsMinimized = true, Width = 0, Height = 0 }));
	}
}

public class AppIdentityTests
{
	[Theory]
	[InlineData(@"C:\Program Files\App\app.exe", @"C:\PROGRAM FILES\APP\APP.EXE")]
	[InlineData(@"  c:\x\y.exe  ", @"C:\X\Y.EXE")]
	[InlineData("", "")]
	[InlineData(null, "")]
	[InlineData("   ", "")]
	public void Key_is_the_upper_case_path(string? path, string expected)
	{
		Assert.Equal(expected, AppIdentity.KeyFromPath(path));
	}

	[Fact]
	public void Same_path_in_different_case_is_the_same_app()
	{
		Assert.Equal(AppIdentity.KeyFromPath(@"c:\a\code.exe"), AppIdentity.KeyFromPath(@"C:\A\CODE.EXE"));
	}

	[Fact]
	public void Same_process_name_in_different_folders_are_different_apps()
	{
		Assert.NotEqual(AppIdentity.KeyFromPath(@"C:\one\app.exe"), AppIdentity.KeyFromPath(@"C:\two\app.exe"));
	}

	[Theory]
	[InlineData("calc.exe", @"C:\Windows\System32\calc.exe", true)]
	[InlineData("CALC", @"C:\Windows\System32\calc.exe", true)]
	[InlineData("calc.exe", @"C:\Windows\System32\notcalc.exe", false)]
	[InlineData("calc", @"C:\Windows\System32\calc2.exe", false)]
	[InlineData(@"c:\windows\system32\CALC.exe", @"C:\Windows\System32\calc.exe", true)]
	[InlineData(@"C:\other\calc.exe", @"C:\Windows\System32\calc.exe", false)]
	[InlineData("  calc.exe  ", @"C:\Windows\System32\calc.exe", true)]
	public void Listed_by_file_name_or_path(string entry, string path, bool expected)
	{
		Assert.Equal(expected, AppIdentity.IsListed(new[] { entry }, path));
	}

	[Fact]
	public void Empty_input_is_never_listed()
	{
		Assert.False(AppIdentity.IsListed(null, @"C:\a.exe"));
		Assert.False(AppIdentity.IsListed(new[] { "a.exe" }, null));
		Assert.False(AppIdentity.IsListed(new[] { "a.exe" }, ""));
		Assert.False(AppIdentity.IsListed(new[] { "", "  ", null! }, @"C:\a.exe"));
		Assert.False(AppIdentity.IsListed(Array.Empty<string>(), @"C:\a.exe"));
	}

	[Fact]
	public void A_name_without_extension_matches_an_app_with_dots_in_its_name()
	{
		Assert.True(AppIdentity.IsListed(new[] { "ms-teams" }, @"C:\Program Files\WindowsApps\ms-teams.exe"));
		Assert.True(AppIdentity.IsListed(new[] { "my.app" }, @"C:\x\my.app.exe"));
	}
}

public class StageSettingsTests
{
	[Fact]
	public void Lists_are_cleaned()
	{
		var s = new AppSettings
		{
			Stage = { Companions = new List<string> { " calc.exe ", "CALC.EXE", "", "  ", "notepad" }, ExcludedApps = null! }
		}.Normalize();

		Assert.Equal(new[] { "calc.exe", "notepad" }, s.Stage.Companions);
		Assert.Empty(s.Stage.ExcludedApps);
	}

	[Fact]
	public void Lists_survive_a_round_trip()
	{
		var s = new AppSettings { Stage = { ExcludedApps = new List<string> { "teams.exe" } } }.Normalize();

		var back = AppSettings.Deserialize(s.Serialize())!.Normalize();

		Assert.Equal(new[] { "teams.exe" }, back.Stage.ExcludedApps);
	}

	[Fact]
	public void Defaults_are_empty()
	{
		var s = new AppSettings().Normalize();

		Assert.Empty(s.Stage.Companions);
		Assert.Empty(s.Stage.ExcludedApps);
	}
}
