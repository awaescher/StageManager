using StageManager.Core;

namespace StageManager.Tests;

public class DesktopIconsPolicyTests
{
	private sealed class FakeIcons : IDesktopIcons
	{
		private bool _visible;

		public FakeIcons(bool visible) => _visible = visible;

		public int Toggles { get; private set; }

		public bool Reachable { get; set; } = true;

		/// <summary>Windows reports the old state for a while after a toggle.</summary>
		public bool Lagging { get; set; }

		private bool _reported;

		public bool IsVisible => Lagging ? _reported : _visible;

		public bool ActuallyVisible => _visible;

		public bool Toggle()
		{
			if (!Reachable)
				return false;

			Toggles++;
			_visible = !_visible;
			return true;
		}

		public void SetByUser(bool visible) => _visible = _reported = visible;
	}

	private sealed class FakeState : IDesktopIconsState
	{
		public bool? Original { get; set; }

		public int Saves { get; private set; }

		public bool? LoadOriginal() => Original;

		public void SaveOriginal(bool visible)
		{
			Original = visible;
			Saves++;
		}

		public void Clear() => Original = null;
	}

	private readonly DesktopIconsSettings _settings = new();

	private (DesktopIconsPolicy Policy, FakeIcons Icons, FakeState State) Create(bool iconsVisible, FakeState? state = null)
	{
		var icons = new FakeIcons(iconsVisible);
		icons.SetByUser(iconsVisible);
		state ??= new FakeState();
		var policy = new DesktopIconsPolicy(icons, state, () => _settings);
		policy.Start();
		return (policy, icons, state);
	}

	[Fact]
	public void Manage_hides_while_a_scene_is_active_and_shows_in_the_desktop_view()
	{
		var (policy, icons, _) = Create(iconsVisible: true);

		policy.Apply(sceneActive: true);
		Assert.False(icons.ActuallyVisible);

		policy.Apply(sceneActive: false);
		Assert.True(icons.ActuallyVisible);
	}

	[Fact]
	public void Repeated_calls_toggle_only_once()
	{
		var (policy, icons, _) = Create(iconsVisible: true);

		policy.Apply(true);
		policy.Apply(true);
		policy.Reapply();
		policy.Apply(true);

		Assert.Equal(1, icons.Toggles);
	}

	[Fact]
	public void A_lagging_state_from_windows_does_not_cause_a_second_toggle()
	{
		var (policy, icons, _) = Create(iconsVisible: true);
		icons.Lagging = true;

		policy.Apply(true);
		policy.Apply(true);
		policy.Reapply();

		Assert.Equal(1, icons.Toggles);
		Assert.False(icons.ActuallyVisible);
	}

	[Fact]
	public void Icons_that_are_already_hidden_are_not_toggled()
	{
		var (policy, icons, _) = Create(iconsVisible: false);

		policy.Apply(true);

		Assert.Equal(0, icons.Toggles);
	}

	[Fact]
	public void Exit_restores_the_original_state_visible()
	{
		var (policy, icons, _) = Create(iconsVisible: true);
		policy.Apply(true);

		policy.Stop();

		Assert.True(icons.ActuallyVisible);
	}

	[Fact]
	public void Exit_keeps_icons_hidden_that_the_user_had_hidden_before()
	{
		var (policy, icons, _) = Create(iconsVisible: false);
		policy.Apply(false); // desktop view shows them
		Assert.True(icons.ActuallyVisible);
		policy.Apply(true);

		policy.Stop();

		Assert.False(icons.ActuallyVisible);
	}

	[Fact]
	public void Exit_without_RestoreOnExit_leaves_the_icons_as_they_are()
	{
		_settings.RestoreOnExit = false;
		var (policy, icons, _) = Create(iconsVisible: true);
		policy.Apply(true);

		policy.Stop();

		Assert.False(icons.ActuallyVisible);
		Assert.Equal(1, icons.Toggles);
	}

	[Fact]
	public void AlwaysHidden_ignores_the_desktop_view()
	{
		_settings.Mode = DesktopIconsMode.AlwaysHidden;
		var (policy, icons, _) = Create(iconsVisible: true);

		policy.Apply(false);
		Assert.False(icons.ActuallyVisible);

		policy.Apply(true);
		Assert.False(icons.ActuallyVisible);
		Assert.Equal(1, icons.Toggles);
	}

	[Fact]
	public void Untouched_never_toggles()
	{
		_settings.Mode = DesktopIconsMode.Untouched;
		var (policy, icons, state) = Create(iconsVisible: true);

		policy.Apply(true);
		policy.Apply(false);
		policy.Reapply();
		policy.Stop();

		Assert.Equal(0, icons.Toggles);
		Assert.Equal(0, state.Saves);
	}

	[Fact]
	public void Switching_to_Untouched_at_runtime_undoes_own_changes()
	{
		var (policy, icons, _) = Create(iconsVisible: true);
		policy.Apply(true);
		Assert.False(icons.ActuallyVisible);

		_settings.Mode = DesktopIconsMode.Untouched;
		policy.Reapply();

		Assert.True(icons.ActuallyVisible);
	}

	[Fact]
	public void Switching_the_mode_at_runtime_takes_effect_immediately()
	{
		var (policy, icons, _) = Create(iconsVisible: true);
		policy.Apply(false);

		_settings.Mode = DesktopIconsMode.AlwaysHidden;
		policy.Reapply();
		Assert.False(icons.ActuallyVisible);

		_settings.Mode = DesktopIconsMode.Manage;
		policy.Reapply();
		Assert.True(icons.ActuallyVisible);
	}

	[Fact]
	public void The_original_state_is_saved_before_the_first_change_and_cleared_on_exit()
	{
		var (policy, _, state) = Create(iconsVisible: true);
		Assert.Null(state.Original);

		policy.Apply(true);
		Assert.True(state.Original);
		Assert.Equal(1, state.Saves);

		policy.Apply(false);
		policy.Apply(true);
		Assert.Equal(1, state.Saves);

		policy.Stop();
		Assert.Null(state.Original);
	}

	[Fact]
	public void After_a_crash_the_remembered_state_is_restored_on_exit()
	{
		// the previous run hid the icons and was killed
		var state = new FakeState { Original = true };
		var (policy, icons, _) = Create(iconsVisible: false, state);

		Assert.True(policy.OriginalVisible);

		policy.Stop();

		Assert.True(icons.ActuallyVisible);
	}

	[Fact]
	public void After_a_crash_Untouched_restores_the_icons_right_away()
	{
		_settings.Mode = DesktopIconsMode.Untouched;
		var state = new FakeState { Original = true };
		var (policy, icons, _) = Create(iconsVisible: false, state);

		policy.Reapply();

		Assert.True(icons.ActuallyVisible);
	}

	[Fact]
	public void After_a_crash_the_remembered_state_is_not_overwritten_by_the_hidden_state()
	{
		var state = new FakeState { Original = true };
		var (policy, _, _) = Create(iconsVisible: false, state);

		policy.Apply(true);

		Assert.True(state.Original);
		Assert.Equal(0, state.Saves);
	}

	[Fact]
	public void An_unreachable_desktop_is_retried_at_the_next_call()
	{
		var (policy, icons, _) = Create(iconsVisible: true);
		icons.Reachable = false;

		policy.Apply(true);
		Assert.Equal(0, icons.Toggles);

		icons.Reachable = true;
		policy.Apply(true);
		Assert.False(icons.ActuallyVisible);
	}

	[Fact]
	public void Resync_picks_up_a_state_changed_behind_our_back()
	{
		var (policy, icons, _) = Create(iconsVisible: true);
		policy.Apply(true);
		Assert.False(icons.ActuallyVisible);

		// the Explorer restarted and shows the icons again
		icons.SetByUser(true);
		policy.Resync();

		Assert.False(icons.ActuallyVisible);
		Assert.Equal(2, icons.Toggles);
	}

	[Fact]
	public void Nothing_happens_before_Start()
	{
		var icons = new FakeIcons(true);
		var policy = new DesktopIconsPolicy(icons, new FakeState(), () => _settings);

		policy.Apply(true);
		policy.Reapply();
		policy.Resync();
		policy.Stop();

		Assert.Equal(0, icons.Toggles);
	}

	[Fact]
	public void Stop_twice_is_harmless()
	{
		var (policy, icons, _) = Create(iconsVisible: true);
		policy.Apply(true);

		policy.Stop();
		policy.Stop();

		Assert.Equal(2, icons.Toggles);
	}
}

public class FileDesktopIconsStateTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "StageManagerTests", Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_dir))
			Directory.Delete(_dir, recursive: true);
	}

	[Fact]
	public void Nothing_is_remembered_by_default()
	{
		Assert.Null(new FileDesktopIconsState(Path.Combine(_dir, "icons.state")).LoadOriginal());
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void State_is_saved_and_cleared(bool visible)
	{
		var state = new FileDesktopIconsState(Path.Combine(_dir, "icons.state"));

		state.SaveOriginal(visible);
		Assert.Equal(visible, state.LoadOriginal());

		state.Clear();
		Assert.Null(state.LoadOriginal());
	}

	[Fact]
	public void Garbage_in_the_file_counts_as_nothing_remembered()
	{
		Directory.CreateDirectory(_dir);
		var path = Path.Combine(_dir, "icons.state");
		File.WriteAllText(path, "banana");

		Assert.Null(new FileDesktopIconsState(path).LoadOriginal());
	}

	[Fact]
	public void Clear_without_a_file_is_harmless()
	{
		new FileDesktopIconsState(Path.Combine(_dir, "none.state")).Clear();
	}
}
