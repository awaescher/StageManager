using StageManager.Core;

namespace StageManager.Tests;

public class HotkeyTests
{
	private const HotkeyModifiers Ctrl = HotkeyModifiers.Ctrl;
	private const HotkeyModifiers Alt = HotkeyModifiers.Alt;
	private const HotkeyModifiers Win = HotkeyModifiers.Win;

	[Fact]
	public void The_default_is_Ctrl_Alt_Win()
	{
		Assert.Equal("Ctrl+Alt+Win", HotkeyText.Format(HotkeyText.Default));
		Assert.Equal("Ctrl+Alt+Win", new AppSettings().Normalize().Hotkeys.Keys);
		Assert.True(new AppSettings().Hotkeys.Enabled);
	}

	[Theory]
	[InlineData("Ctrl+Alt+Win", Ctrl | Alt | Win)]
	[InlineData("win + ctrl", Ctrl | Win)]
	[InlineData("ALT", Alt)]
	[InlineData("Control+Windows", Ctrl | Win)]
	[InlineData("Strg+Alt", Ctrl | Alt)]
	public void Keys_are_read_in_any_order_and_case(string text, HotkeyModifiers expected)
	{
		Assert.True(HotkeyText.TryParse(text, out var keys));
		Assert.Equal(expected, keys);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("  ")]
	[InlineData("Tab")]
	[InlineData("Ctrl+Tab")]
	[InlineData("Shift")]
	[InlineData("Ctrl+Shift")]
	public void Text_without_usable_keys_is_refused(string? text)
	{
		Assert.False(HotkeyText.TryParse(text, out var keys));
		Assert.Equal(HotkeyModifiers.None, keys);
	}

	[Theory]
	[InlineData(Ctrl | Alt | Win)]
	[InlineData(Win)]
	[InlineData(Ctrl | Alt)]
	public void Format_and_parse_agree(HotkeyModifiers modifiers)
	{
		Assert.True(HotkeyText.TryParse(HotkeyText.Format(modifiers), out var back));
		Assert.Equal(modifiers, back);
	}

	[Fact]
	public void Keys_without_modifier_or_with_Shift_cannot_be_used()
	{
		Assert.False(HotkeyText.IsUsable(HotkeyModifiers.None));
		Assert.False(HotkeyText.IsUsable(HotkeyModifiers.Shift));
		Assert.False(HotkeyText.IsUsable(Ctrl | HotkeyModifiers.Shift));
		Assert.True(HotkeyText.IsUsable(Ctrl));
	}

	[Theory]
	[InlineData("nonsense")]
	[InlineData("")]
	[InlineData("Shift")]
	public void Broken_keys_in_the_settings_fall_back_to_the_default(string text)
	{
		var s = new AppSettings { Hotkeys = { Keys = text } }.Normalize();

		Assert.Equal("Ctrl+Alt+Win", s.Hotkeys.Keys);
	}

	[Fact]
	public void Keys_in_the_settings_are_written_in_a_fixed_order()
	{
		var s = new AppSettings { Hotkeys = { Keys = "win+alt" } }.Normalize();

		Assert.Equal("Alt+Win", s.Hotkeys.Keys);
	}

	[Fact]
	public void Missing_hotkey_section_gets_defaults()
	{
		var s = AppSettings.Deserialize("""{ "Sidebar": { "Side": "Right" } }""")!.Normalize();

		Assert.True(s.Hotkeys.Enabled);
		Assert.Equal("Ctrl+Alt+Win", s.Hotkeys.Keys);
	}

	[Fact]
	public void Actions_are_Tab_Shift_Tab_and_nine_digits()
	{
		var all = HotkeyAction.All().ToList();

		Assert.Equal(11, all.Count);
		Assert.Equal(HotkeyAction.Next, all[0]);
		Assert.Equal(HotkeyAction.Previous, all[1]);
		Assert.Equal(Enumerable.Range(1, 9), all.Skip(2).Select(a => a.Number));
	}

	[Fact]
	public void Actions_are_described_with_their_keys()
	{
		Assert.Equal("Ctrl+Alt+Win+Tab", HotkeyText.Describe(HotkeyText.Default, HotkeyAction.Next));
		Assert.Equal("Ctrl+Alt+Win+Shift+Tab", HotkeyText.Describe(HotkeyText.Default, HotkeyAction.Previous));
		Assert.Equal("Win+3", HotkeyText.Describe(Win, HotkeyAction.Card(3)));
	}

	[Fact]
	public void Known_owners_are_named()
	{
		Assert.Equal("the Windows window switcher", HotkeyAdvice.KnownOwner(Alt, HotkeyAction.Next));
		Assert.Equal("the Windows task view", HotkeyAdvice.KnownOwner(Win, HotkeyAction.Next));
		Assert.Equal("the apps on the Windows taskbar", HotkeyAdvice.KnownOwner(Win, HotkeyAction.Card(1)));
		Assert.Null(HotkeyAdvice.KnownOwner(Ctrl | Alt | Win, HotkeyAction.Next));
	}

	[Fact]
	public void Conflicts_of_unknown_owners_name_Windows_or_another_app()
	{
		var lines = HotkeyAdvice.Conflicts(Ctrl | Alt | Win, new[] { HotkeyAction.Next });

		Assert.Equal(new[] { "Ctrl+Alt+Win+Tab is already used by Windows or another app." }, lines);
	}

	[Fact]
	public void Conflicts_of_the_digits_are_one_line()
	{
		var digits = Enumerable.Range(1, 9).Select(HotkeyAction.Card);

		var lines = HotkeyAdvice.Conflicts(Win, digits);

		Assert.Equal(new[] { "Win+1...9 is already used by the apps on the Windows taskbar." }, lines);
	}

	[Fact]
	public void Scattered_digits_are_listed()
	{
		var lines = HotkeyAdvice.Conflicts(Ctrl | Alt | Win, new[] { HotkeyAction.Card(2), HotkeyAction.Card(5) });

		Assert.Single(lines);
		Assert.StartsWith("Ctrl+Alt+Win+2, 5 is already used", lines[0]);
	}

	[Fact]
	public void Nothing_failed_means_no_conflicts()
	{
		Assert.Empty(HotkeyAdvice.Conflicts(Ctrl | Alt | Win, Array.Empty<HotkeyAction>()));
	}

	[Theory]
	[InlineData(HotkeyModifiers.Ctrl, true)]
	[InlineData(HotkeyModifiers.Alt, true)]
	[InlineData(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, true)]
	[InlineData(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win, false)]
	[InlineData(HotkeyModifiers.Win, false)]
	public void Keys_that_take_shortcuts_from_apps_get_a_warning(HotkeyModifiers keys, bool warns)
	{
		Assert.Equal(warns, HotkeyAdvice.Warning(keys) is not null);
	}

	private static readonly Guid A = Guid.NewGuid();
	private static readonly Guid B = Guid.NewGuid();
	private static readonly Guid C = Guid.NewGuid();

	[Fact]
	public void The_picker_starts_at_the_first_scene_and_goes_on()
	{
		var picker = new ScenePicker();
		var scenes = new[] { A, B, C };

		Assert.False(picker.IsActive);
		Assert.Equal(A, picker.Step(scenes, backwards: false));
		Assert.Equal(B, picker.Step(scenes, backwards: false));
		Assert.Equal(C, picker.Step(scenes, backwards: false));
		Assert.Equal(A, picker.Step(scenes, backwards: false));
	}

	[Fact]
	public void Backwards_starts_at_the_last_scene()
	{
		var picker = new ScenePicker();
		var scenes = new[] { A, B, C };

		Assert.Equal(C, picker.Step(scenes, backwards: true));
		Assert.Equal(B, picker.Step(scenes, backwards: true));
		Assert.Equal(A, picker.Step(scenes, backwards: true));
		Assert.Equal(C, picker.Step(scenes, backwards: true));
	}

	[Fact]
	public void Forwards_and_backwards_can_be_mixed()
	{
		var picker = new ScenePicker();
		var scenes = new[] { A, B, C };

		picker.Step(scenes, backwards: false);
		picker.Step(scenes, backwards: false);

		Assert.Equal(A, picker.Step(scenes, backwards: true));
	}

	[Fact]
	public void The_list_is_frozen_while_picking()
	{
		var picker = new ScenePicker();
		picker.Step(new[] { A, B, C }, backwards: false);

		// the sidebar reorders itself meanwhile
		Assert.Equal(B, picker.Step(new[] { C, B, A }, backwards: false));
	}

	[Fact]
	public void Nothing_to_pick_stays_inactive()
	{
		var picker = new ScenePicker();

		Assert.Null(picker.Step(Array.Empty<Guid>(), backwards: false));
		Assert.False(picker.IsActive);
		Assert.Null(picker.Picked);
	}

	[Fact]
	public void A_single_scene_stays_picked()
	{
		var picker = new ScenePicker();

		Assert.Equal(A, picker.Step(new[] { A }, backwards: false));
		Assert.Equal(A, picker.Step(new[] { A }, backwards: false));
		Assert.Equal(A, picker.Step(new[] { A }, backwards: true));
	}

	[Fact]
	public void Ending_starts_over_with_the_new_list()
	{
		var picker = new ScenePicker();
		picker.Step(new[] { A, B }, backwards: false);

		picker.End();

		Assert.False(picker.IsActive);
		Assert.Null(picker.Picked);
		Assert.Equal(C, picker.Step(new[] { C, A }, backwards: false));
	}

	[Fact]
	public void The_keyboard_brings_the_sidebar_up_like_the_screen_edge()
	{
		var geometry = new SidebarGeometry(new Area(0, 0, 1920, 1040), 280, SidebarSide.Left);
		var presence = new SidebarPresence(geometry);

		var hidden = presence.Decide(SidebarMode.OffScreen, new SidebarInput(900, 500, true, false, 10, Summon: false));
		var summoned = presence.Decide(SidebarMode.OffScreen, new SidebarInput(900, 500, true, false, 10, Summon: true));

		Assert.Equal(SidebarMode.OffScreen, hidden);
		Assert.Equal(SidebarMode.Flyover, summoned);
	}

	[Fact]
	public void The_sidebar_stays_while_the_keyboard_holds_it_and_goes_after_letting_go()
	{
		var geometry = new SidebarGeometry(new Area(0, 0, 1920, 1040), 280, SidebarSide.Left);
		var presence = new SidebarPresence(geometry);

		var mode = presence.Decide(SidebarMode.OffScreen, new SidebarInput(900, 500, true, false, 10, Summon: true));
		mode = presence.Decide(mode, new SidebarInput(900, 500, true, false, 12, Summon: true));
		Assert.Equal(SidebarMode.Flyover, mode);

		mode = presence.Decide(mode, new SidebarInput(900, 500, true, false, 12.1));
		Assert.Equal(SidebarMode.Flyover, mode);

		mode = presence.Decide(mode, new SidebarInput(900, 500, true, false, 13));
		Assert.Equal(SidebarMode.OffScreen, mode);
	}
}
