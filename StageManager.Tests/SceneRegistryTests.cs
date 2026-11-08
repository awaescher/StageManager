using StageManager.Core;

namespace StageManager.Tests;

public class SceneRegistryTests
{
	private DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

	private SceneRegistry Create() => new(() => _now, TimeSpan.FromSeconds(1));

	[Fact]
	public void Initialize_groups_windows_by_key_in_order_of_appearance()
	{
		var r = Create();

		var groups = r.Initialize(new (long, string)[] { (1, "A"), (2, "B"), (3, "A"), (4, "C") });

		Assert.Equal(new[] { "A", "B", "C" }, groups.Select(g => g.Key));
		Assert.Equal(new long[] { 1, 3 }, groups[0].Windows);
		Assert.True(r.IsInitialized);
	}

	[Fact]
	public void Initialize_treats_keys_case_insensitive_and_ignores_duplicate_windows()
	{
		var groups = Create().Initialize(new (long, string)[] { (1, "a"), (2, "A"), (1, "A") });

		var group = Assert.Single(groups);
		Assert.Equal(new long[] { 1, 2 }, group.Windows);
	}

	[Fact]
	public void Initialize_twice_starts_over()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });

		var groups = r.Initialize(new (long, string)[] { (2, "B") });

		Assert.Equal("B", Assert.Single(groups).Key);
	}

	[Fact]
	public void A_new_window_of_a_known_app_joins_its_group()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });

		var p = r.Place(2, "A");

		Assert.False(p.Created);
		Assert.True(p.Switch);
		Assert.Equal(r.FindGroupId(1), p.GroupId);
		Assert.Equal(new long[] { 1, 2 }, r.WindowsOf(p.GroupId));
	}

	[Fact]
	public void A_new_window_of_an_unknown_app_opens_a_group()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });

		var p = r.Place(2, "B");

		Assert.True(p.Created);
		Assert.True(p.Switch);
		Assert.Equal(2, r.GroupIds.Count);
	}

	[Fact]
	public void Placing_a_known_window_again_changes_nothing()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });

		var p = r.Place(1, "B");

		Assert.False(p.Created);
		Assert.Equal(r.FindGroupId(1), p.GroupId);
		Assert.Single(r.GroupIds);
	}

	[Fact]
	public void A_companion_joins_the_current_group_and_does_not_switch()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B") });
		var a = r.FindGroupId(1)!.Value;
		r.TrySwitch(a);

		var p = r.Place(3, "CALC", companion: true);

		Assert.Equal(a, p.GroupId);
		Assert.False(p.Created);
		Assert.False(p.Switch);
		Assert.Equal(new long[] { 1, 3 }, r.WindowsOf(a));
		Assert.Equal(2, r.GroupIds.Count);
	}

	[Fact]
	public void A_companion_without_a_current_scene_gets_its_own_group()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });

		var p = r.Place(3, "CALC", companion: true);

		Assert.True(p.Created);
		Assert.True(p.Switch);
	}

	[Fact]
	public void Remove_keeps_a_group_with_windows_left()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "A") });

		var removal = r.Remove(1);

		Assert.False(removal.GroupRemoved);
		Assert.Equal(r.FindGroupId(2), removal.GroupId);
	}

	[Fact]
	public void Remove_of_the_last_window_removes_the_group()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B") });
		var a = r.FindGroupId(1);

		var removal = r.Remove(1);

		Assert.True(removal.GroupRemoved);
		Assert.Equal(a, removal.GroupId);
		Assert.Single(r.GroupIds);
		Assert.Null(r.FindGroupId(1));
	}

	[Fact]
	public void Remove_of_an_unknown_window_is_harmless()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });

		var removal = r.Remove(99);

		Assert.Null(removal.GroupId);
		Assert.False(removal.GroupRemoved);
		Assert.Single(r.GroupIds);
	}

	[Fact]
	public void A_new_window_after_the_group_was_removed_opens_a_new_group()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });
		var old = r.FindGroupId(1);
		r.Remove(1);

		var p = r.Place(5, "A");

		Assert.True(p.Created);
		Assert.NotEqual(old, p.GroupId);
	}

	[Fact]
	public void Move_changes_the_group_and_removes_an_emptied_source()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B") });
		var a = r.FindGroupId(1)!.Value;
		var b = r.FindGroupId(2)!.Value;

		var result = r.Move(1, b);

		Assert.NotNull(result);
		Assert.Equal(a, result!.Value.SourceId);
		Assert.True(result.Value.SourceRemoved);
		Assert.Equal(new long[] { 2, 1 }, r.WindowsOf(b));
		Assert.Single(r.GroupIds);
	}

	[Fact]
	public void Move_keeps_a_source_with_windows_left()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "A"), (3, "B") });
		var b = r.FindGroupId(3)!.Value;

		var result = r.Move(1, b);

		Assert.False(result!.Value.SourceRemoved);
		Assert.Equal(2, r.GroupIds.Count);
	}

	[Fact]
	public void Move_to_the_same_or_an_unknown_group_does_nothing()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });
		var a = r.FindGroupId(1)!.Value;

		Assert.Null(r.Move(1, a));
		Assert.Null(r.Move(1, Guid.NewGuid()));
		Assert.Null(r.Move(99, a));
	}

	[Fact]
	public void Switch_selects_a_scene_once()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });
		var a = r.FindGroupId(1);

		Assert.True(r.TrySwitch(a));
		Assert.Equal(a, r.CurrentId);
		Assert.False(r.TrySwitch(a));
	}

	[Fact]
	public void Switch_to_the_desktop_view()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });
		r.TrySwitch(r.FindGroupId(1));

		Assert.True(r.TrySwitch(null));
		Assert.Null(r.CurrentId);
		Assert.False(r.TrySwitch(null));
	}

	[Fact]
	public void An_app_that_switches_back_right_after_it_was_hidden_is_ignored()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "TEAMS"), (2, "B") });
		var teams = r.FindGroupId(1);
		var b = r.FindGroupId(2);
		r.TrySwitch(teams);

		Assert.True(r.TrySwitch(b));

		_now += TimeSpan.FromMilliseconds(300);
		Assert.False(r.TrySwitch(teams));
		Assert.Equal(b, r.CurrentId);
	}

	[Fact]
	public void Switching_back_is_allowed_after_the_guard_time()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "TEAMS"), (2, "B") });
		var teams = r.FindGroupId(1);
		r.TrySwitch(teams);
		r.TrySwitch(r.FindGroupId(2));

		_now += TimeSpan.FromSeconds(1.5);

		Assert.True(r.TrySwitch(teams));
		Assert.Equal(teams, r.CurrentId);
	}

	[Fact]
	public void The_desktop_view_is_never_blocked()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });
		r.TrySwitch(r.FindGroupId(1));

		Assert.True(r.TrySwitch(null));
	}

	[Fact]
	public void The_first_switch_is_never_blocked()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });

		Assert.True(r.TrySwitch(r.FindGroupId(1)));
	}

	[Fact]
	public void Finding_groups_by_key_ignores_case()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, @"C:\A\APP.EXE") });

		Assert.NotNull(r.FindGroupIdByKey(@"c:\a\app.exe"));
		Assert.Null(r.FindGroupIdByKey(@"c:\b\app.exe"));
	}

	[Fact]
	public void A_group_knows_the_apps_of_its_windows()
	{
		var r = Create();
		var groups = r.Initialize(new (long, string)[] { (1, "A"), (2, "A") });

		Assert.Equal(new[] { "A" }, groups[0].Keys);
		Assert.Equal("A", groups[0].Key);
	}

	[Fact]
	public void Merge_puts_everything_into_the_target_and_removes_the_source()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B"), (3, "B") });
		var a = r.FindGroupId(1)!.Value;
		var b = r.FindGroupId(2)!.Value;

		var merged = r.Merge(b, a);

		Assert.NotNull(merged);
		Assert.Equal(new long[] { 2, 3 }, merged!.Value.Windows);
		Assert.Equal(new long[] { 1, 2, 3 }, r.WindowsOf(a));
		Assert.Equal(new[] { "A", "B" }, r.KeysOf(a));
		Assert.Single(r.GroupIds);
		Assert.Equal(a, r.FindGroupId(3));
	}

	[Fact]
	public void A_merged_scene_stays_together()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B"), (3, "C") });
		var a = r.FindGroupId(1)!.Value;
		r.Merge(r.FindGroupId(2)!.Value, a);

		// leaving and coming back selects the same scene with both apps
		r.TrySwitch(a);
		r.TrySwitch(r.FindGroupId(3));
		_now += TimeSpan.FromSeconds(2);
		r.TrySwitch(a);

		Assert.Equal(a, r.CurrentId);
		Assert.Equal(new long[] { 1, 2 }, r.WindowsOf(a));
	}

	[Fact]
	public void A_new_window_of_a_merged_app_joins_the_merged_scene()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B") });
		var a = r.FindGroupId(1)!.Value;
		r.Merge(r.FindGroupId(2)!.Value, a);

		var p = r.Place(3, "B");

		Assert.False(p.Created);
		Assert.Equal(a, p.GroupId);
	}

	[Fact]
	public void Merge_with_the_same_or_an_unknown_group_does_nothing()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });
		var a = r.FindGroupId(1)!.Value;

		Assert.Null(r.Merge(a, a));
		Assert.Null(r.Merge(Guid.NewGuid(), a));
		Assert.Null(r.Merge(a, Guid.NewGuid()));
		Assert.Single(r.GroupIds);
	}

	[Fact]
	public void Merging_the_current_scene_makes_the_target_current()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B") });
		var a = r.FindGroupId(1)!.Value;
		var b = r.FindGroupId(2)!.Value;
		r.TrySwitch(b);

		r.Merge(b, a);

		Assert.Equal(a, r.CurrentId);
	}

	[Fact]
	public void Split_gives_an_app_its_own_scene_right_after_the_group()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B"), (3, "C") });
		var a = r.FindGroupId(1)!.Value;
		r.Merge(r.FindGroupId(2)!.Value, a);
		var c = r.FindGroupId(3)!.Value;

		var split = r.Split(a, "B");

		Assert.NotNull(split);
		Assert.Equal(new long[] { 2 }, split!.Value.Windows);
		Assert.Equal(new long[] { 1 }, r.WindowsOf(a));
		Assert.Equal(new[] { "A" }, r.KeysOf(a));
		Assert.Equal(new[] { "B" }, r.KeysOf(split.Value.NewGroupId));
		Assert.Equal(new[] { a, split.Value.NewGroupId, c }, r.GroupIds);
	}

	[Fact]
	public void Split_takes_all_windows_of_the_app()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B"), (3, "B"), (4, "A") });
		var a = r.FindGroupId(1)!.Value;
		r.Merge(r.FindGroupId(2)!.Value, a);

		var split = r.Split(a, "b");

		Assert.Equal(new long[] { 2, 3 }, split!.Value.Windows);
		Assert.Equal(new long[] { 1, 4 }, r.WindowsOf(a));
	}

	[Fact]
	public void Split_of_a_group_with_a_single_app_does_nothing()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "A") });
		var a = r.FindGroupId(1)!.Value;

		Assert.Null(r.Split(a, "A"));
		Assert.Single(r.GroupIds);
	}

	[Fact]
	public void Split_of_an_unknown_app_or_group_does_nothing()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B") });
		var a = r.FindGroupId(1)!.Value;
		r.Merge(r.FindGroupId(2)!.Value, a);

		Assert.Null(r.Split(a, "X"));
		Assert.Null(r.Split(Guid.NewGuid(), "A"));
		Assert.Equal(new long[] { 1, 2 }, r.WindowsOf(a));
	}

	[Fact]
	public void Closing_the_last_window_of_an_app_removes_the_app_from_the_scene()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B"), (3, "B") });
		var a = r.FindGroupId(1)!.Value;
		r.Merge(r.FindGroupId(2)!.Value, a);

		r.Remove(2);
		Assert.Equal(new[] { "A", "B" }, r.KeysOf(a));

		r.Remove(3);
		Assert.Equal(new[] { "A" }, r.KeysOf(a));
		Assert.Null(r.Split(a, "B"));
	}

	[Fact]
	public void A_new_window_of_a_closed_app_opens_a_new_scene_again()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B") });
		var a = r.FindGroupId(1)!.Value;
		r.Merge(r.FindGroupId(2)!.Value, a);
		r.Remove(2);

		var p = r.Place(5, "B");

		Assert.True(p.Created);
		Assert.NotEqual(a, p.GroupId);
	}

	[Fact]
	public void Moving_a_window_takes_its_app_along_and_drops_it_from_the_source()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "B"), (3, "B") });
		var a = r.FindGroupId(1)!.Value;
		var b = r.FindGroupId(2)!.Value;

		r.Move(1, b);

		Assert.Equal(new[] { "B", "A" }, r.KeysOf(b));
		Assert.Empty(r.KeysOf(a));
		Assert.Single(r.GroupIds);
	}

	[Fact]
	public void Moving_one_of_two_windows_keeps_the_app_in_both_scenes()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A"), (2, "A"), (3, "B") });
		var a = r.FindGroupId(1)!.Value;
		var b = r.FindGroupId(3)!.Value;

		r.Move(1, b);

		Assert.Equal(new[] { "A" }, r.KeysOf(a));
		Assert.Equal(new[] { "B", "A" }, r.KeysOf(b));
	}

	[Fact]
	public void A_companion_adds_its_app_to_the_current_scene()
	{
		var r = Create();
		r.Initialize(new (long, string)[] { (1, "A") });
		var a = r.FindGroupId(1)!.Value;
		r.TrySwitch(a);

		r.Place(2, "CALC", companion: true);

		Assert.Equal(new[] { "A", "CALC" }, r.KeysOf(a));
	}}
