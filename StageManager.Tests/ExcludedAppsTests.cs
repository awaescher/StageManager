using StageManager.Core;

namespace StageManager.Tests;

public class ExcludedAppsTests
{
	[Theory]
	[InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome.exe")]
	[InlineData(@"C:\Users\me\AppData\Local\Discord\app-1.0.9\Discord.exe", "Discord.exe")]
	[InlineData("/usr/bin/tool", "tool")]
	[InlineData("calc.exe", "calc.exe")]
	[InlineData("  calc.exe  ", "calc.exe")]
	[InlineData("", "")]
	[InlineData(null, "")]
	public void The_entry_for_an_app_is_its_file_name(string? path, string expected)
	{
		Assert.Equal(expected, ExcludedApps.EntryFor(path));
	}

	[Fact]
	public void An_app_is_added_to_the_end()
	{
		var list = ExcludedApps.With(new[] { "a.exe" }, "b.exe");

		Assert.Equal(new[] { "a.exe", "b.exe" }, list);
	}

	[Theory]
	[InlineData("calc.exe")]
	[InlineData("CALC.EXE")]
	[InlineData("calc")]
	[InlineData(@"C:\Windows\System32\calc.exe")]
	public void An_app_that_is_named_already_is_not_added_twice(string entry)
	{
		var list = ExcludedApps.With(new[] { "calc.exe" }, entry);

		Assert.Equal(new[] { "calc.exe" }, list);
	}

	[Fact]
	public void Different_apps_with_similar_names_are_both_kept()
	{
		var list = ExcludedApps.With(new[] { "chrome.exe" }, "chromedriver.exe");

		Assert.Equal(2, list.Count);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Nothing_is_added_for_an_empty_entry(string? entry)
	{
		Assert.Equal(new[] { "a.exe" }, ExcludedApps.With(new[] { "a.exe" }, entry));
	}

	[Fact]
	public void Adding_cleans_the_list()
	{
		var list = ExcludedApps.With(new[] { " a.exe ", "A.EXE", "", "  " }, "b.exe");

		Assert.Equal(new[] { "a.exe", "b.exe" }, list);
	}

	[Fact]
	public void Adding_to_nothing_makes_a_list()
	{
		Assert.Equal(new[] { "a.exe" }, ExcludedApps.With(null, "a.exe"));
	}

	[Fact]
	public void Adding_does_not_change_the_list_it_got()
	{
		var original = new List<string> { "a.exe" };

		ExcludedApps.With(original, "b.exe");

		Assert.Single(original);
	}

	[Theory]
	[InlineData("b.exe")]
	[InlineData("B.EXE")]
	[InlineData("  b.exe ")]
	public void An_entry_is_removed_whatever_its_case(string entry)
	{
		Assert.Equal(new[] { "a.exe", "c.exe" }, ExcludedApps.Without(new[] { "a.exe", "b.exe", "c.exe" }, entry));
	}

	[Fact]
	public void Removing_an_unknown_entry_changes_nothing()
	{
		Assert.Equal(new[] { "a.exe" }, ExcludedApps.Without(new[] { "a.exe" }, "z.exe"));
	}

	[Fact]
	public void Removing_does_not_touch_other_entries_with_the_same_start()
	{
		Assert.Equal(new[] { "chromedriver.exe" }, ExcludedApps.Without(new[] { "chrome.exe", "chromedriver.exe" }, "chrome.exe"));
	}

	[Fact]
	public void The_entry_of_an_app_excludes_it_from_the_next_check()
	{
		var path = @"C:\Program Files\Teams\current\Teams.exe";
		var list = ExcludedApps.With(null, ExcludedApps.EntryFor(path));

		Assert.True(ExcludedApps.Contains(list, path));
		Assert.True(ExcludedApps.Contains(list, @"C:\Users\me\AppData\Local\Teams\app-2\Teams.exe"));
		Assert.False(ExcludedApps.Contains(list, @"C:\Windows\notepad.exe"));
	}

	[Fact]
	public void The_list_survives_the_settings_file()
	{
		var s = new AppSettings { Stage = { ExcludedApps = ExcludedApps.With(new[] { "a.exe" }, "b.exe") } }.Normalize();

		var back = AppSettings.Deserialize(s.Serialize())!.Normalize();

		Assert.Equal(new[] { "a.exe", "b.exe" }, back.Stage.ExcludedApps);
	}
}
