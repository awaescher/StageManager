namespace StageManager.Core;

public readonly record struct SceneGroupSnapshot(Guid Id, IReadOnlyList<string> Keys, IReadOnlyList<long> Windows)
{
	/// <summary>The first app of the group.</summary>
	public string Key => Keys.Count > 0 ? Keys[0] : "";
}

/// <param name="Created">The window opened a new group.</param>
/// <param name="Switch">The caller should switch to the group. False for companion windows that join the current group.</param>
public readonly record struct Placement(Guid GroupId, bool Created, bool Switch);

/// <param name="GroupId">Null if the window was not known.</param>
public readonly record struct Removal(Guid? GroupId, bool GroupRemoved);

public readonly record struct MoveResult(Guid SourceId, bool SourceRemoved);

/// <param name="Windows">The windows that moved from the merged group into the target.</param>
public readonly record struct MergeResult(IReadOnlyList<long> Windows);

/// <param name="NewGroupId">The group the app has on its own now.</param>
/// <param name="Windows">The windows of the app.</param>
public readonly record struct SplitResult(Guid NewGroupId, IReadOnlyList<long> Windows);

/// <summary>
/// Keeps track of which window belongs to which scene and which scene is selected.
/// A scene (a card in the sidebar) holds the windows of one or more apps that are shown together.
/// Knows nothing about Win32 or WPF, windows are identified by a number (the handle).
/// </summary>
public sealed class SceneRegistry
{
	private sealed class Group
	{
		public Guid Id { get; } = Guid.NewGuid();

		public List<string> Keys { get; } = new();

		public List<long> Windows { get; } = new();

		public bool HasKey(string key) => Keys.Any(k => SameKey(k, key));
	}

	private readonly List<Group> _groups = new();
	private readonly Dictionary<long, string> _keyOfWindow = new();
	private readonly SwitchGuard _guard;

	public SceneRegistry(Func<DateTime>? clock = null, TimeSpan? reentrancyWindow = null)
	{
		_guard = new SwitchGuard(clock ?? (() => DateTime.UtcNow), reentrancyWindow ?? TimeSpan.FromSeconds(1));
	}

	/// <summary>The selected scene. Stays set when that scene disappears, until the next switch.</summary>
	public Guid? CurrentId { get; private set; }

	public IReadOnlyList<Guid> GroupIds => _groups.Select(g => g.Id).ToList();

	public bool IsInitialized { get; private set; }

	/// <summary>Creates one group per key for the windows that exist at start, in the order of their first appearance.</summary>
	public IReadOnlyList<SceneGroupSnapshot> Initialize(IEnumerable<(long Window, string Key)> windows)
	{
		_groups.Clear();
		_keyOfWindow.Clear();

		foreach (var (window, key) in windows)
		{
			if (_keyOfWindow.ContainsKey(window))
				continue;

			var group = FindByKey(key) ?? Add(key);
			group.Windows.Add(window);
			_keyOfWindow[window] = key;
		}

		IsInitialized = true;
		return _groups.Select(Snapshot).ToList();
	}

	/// <summary>
	/// Puts a new window into the group of its app, or opens a group for it.
	/// A companion window joins the current group instead and does not ask for a switch.
	/// </summary>
	public Placement Place(long window, string key, bool companion = false)
	{
		var known = FindGroupOf(window);
		if (known is not null)
			return new Placement(known.Id, Created: false, Switch: true);

		if (companion && CurrentId is Guid currentId && _groups.FirstOrDefault(g => g.Id == currentId) is Group current)
		{
			current.Windows.Add(window);
			_keyOfWindow[window] = key;
			if (!current.HasKey(key))
				current.Keys.Add(key);

			return new Placement(current.Id, Created: false, Switch: false);
		}

		var created = false;
		var group = FindByKey(key);
		if (group is null)
		{
			group = Add(key);
			created = true;
		}

		group.Windows.Add(window);
		_keyOfWindow[window] = key;
		return new Placement(group.Id, created, Switch: true);
	}

	public Removal Remove(long window)
	{
		var group = FindGroupOf(window);
		if (group is null)
			return new Removal(null, false);

		group.Windows.Remove(window);
		_keyOfWindow.Remove(window, out var key);
		DropKeyIfUnused(group, key);

		if (group.Windows.Count > 0)
			return new Removal(group.Id, GroupRemoved: false);

		_groups.Remove(group);
		return new Removal(group.Id, GroupRemoved: true);
	}

	/// <summary>Moves a single window into another group. Its app follows if the group does not have it yet.</summary>
	/// <returns>Null if nothing was moved: unknown window, unknown target or same group.</returns>
	public MoveResult? Move(long window, Guid target)
	{
		var source = FindGroupOf(window);
		var targetGroup = _groups.FirstOrDefault(g => g.Id == target);

		if (source is null || targetGroup is null || source == targetGroup)
			return null;

		var key = _keyOfWindow.GetValueOrDefault(window);

		source.Windows.Remove(window);
		targetGroup.Windows.Add(window);

		if (key is not null && !targetGroup.HasKey(key))
			targetGroup.Keys.Add(key);

		DropKeyIfUnused(source, key);

		var removed = source.Windows.Count == 0;
		if (removed)
			_groups.Remove(source);

		return new MoveResult(source.Id, removed);
	}

	/// <summary>
	/// Puts all windows and apps of one group into another and removes the first. The merged group stays together as one scene.
	/// </summary>
	/// <returns>Null if nothing was merged: unknown group or same group.</returns>
	public MergeResult? Merge(Guid source, Guid target)
	{
		var sourceGroup = _groups.FirstOrDefault(g => g.Id == source);
		var targetGroup = _groups.FirstOrDefault(g => g.Id == target);

		if (sourceGroup is null || targetGroup is null || sourceGroup == targetGroup)
			return null;

		var moved = sourceGroup.Windows.ToList();
		targetGroup.Windows.AddRange(moved);

		foreach (var key in sourceGroup.Keys)
		{
			if (!targetGroup.HasKey(key))
				targetGroup.Keys.Add(key);
		}

		_groups.Remove(sourceGroup);

		if (CurrentId == source)
			CurrentId = target;

		return new MergeResult(moved);
	}

	/// <summary>Takes an app out of a group that holds more than one app and gives it a scene of its own, right after the group.</summary>
	/// <returns>Null if the group has only this app or does not have it.</returns>
	public SplitResult? Split(Guid group, string key)
	{
		var source = _groups.FirstOrDefault(g => g.Id == group);
		if (source is null || source.Keys.Count < 2 || !source.HasKey(key))
			return null;

		var moved = source.Windows
			.Where(w => _keyOfWindow.TryGetValue(w, out var k) && SameKey(k, key))
			.ToList();

		if (moved.Count == 0)
			return null;

		source.Windows.RemoveAll(moved.Contains);
		source.Keys.RemoveAll(k => SameKey(k, key));

		var created = new Group();
		created.Keys.Add(key);
		created.Windows.AddRange(moved);
		_groups.Insert(_groups.IndexOf(source) + 1, created);

		return new SplitResult(created.Id, moved);
	}

	/// <summary>Selects a scene. Null selects the desktop view.</summary>
	/// <returns>False if the scene is selected already or an app switched back right after it was hidden.</returns>
	public bool TrySwitch(Guid? target)
	{
		if (target == CurrentId)
			return false;

		if (_guard.IsBlocked(target, CurrentId))
			return false;

		CurrentId = target;
		return true;
	}

	public Guid? FindGroupId(long window) => FindGroupOf(window)?.Id;

	public Guid? FindGroupIdByKey(string key) => FindByKey(key)?.Id;

	public IReadOnlyList<long> WindowsOf(Guid group) => _groups.FirstOrDefault(g => g.Id == group)?.Windows.ToList() ?? new List<long>();

	public IReadOnlyList<string> KeysOf(Guid group) => _groups.FirstOrDefault(g => g.Id == group)?.Keys.ToList() ?? new List<string>();

	private Group Add(string key)
	{
		var group = new Group();
		group.Keys.Add(key);
		_groups.Add(group);
		return group;
	}

	private void DropKeyIfUnused(Group group, string? key)
	{
		if (key is null)
			return;

		var stillUsed = group.Windows.Any(w => _keyOfWindow.TryGetValue(w, out var k) && SameKey(k, key));
		if (!stillUsed)
			group.Keys.RemoveAll(k => SameKey(k, key));
	}

	private Group? FindGroupOf(long window) => _groups.FirstOrDefault(g => g.Windows.Contains(window));

	private Group? FindByKey(string key) => _groups.FirstOrDefault(g => g.HasKey(key));

	private static bool SameKey(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	private static SceneGroupSnapshot Snapshot(Group group) => new(group.Id, group.Keys.ToList(), group.Windows.ToList());
}

/// <summary>
/// Some apps activate one of their windows right after it was hidden, like Teams does with the small window of a running call.
/// Switching back to the scene that was just left is ignored for a short time, otherwise two scenes would fight for the screen.
/// </summary>
internal sealed class SwitchGuard
{
	private readonly Func<DateTime> _clock;
	private readonly TimeSpan _window;
	private Guid? _lockedId;
	private DateTime _lockedUntil;

	public SwitchGuard(Func<DateTime> clock, TimeSpan window)
	{
		_clock = clock;
		_window = window;
	}

	public bool IsBlocked(Guid? target, Guid? current)
	{
		if (target is null)
			return false;

		var now = _clock();

		if (_lockedId == target && now < _lockedUntil)
			return true;

		if (current is not null)
		{
			_lockedId = current;
			_lockedUntil = now + _window;
		}

		return false;
	}
}
