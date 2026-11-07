namespace StageManager.Core;

public readonly record struct SceneGroupSnapshot(Guid Id, string Key, IReadOnlyList<long> Windows);

/// <param name="Created">The window opened a new group.</param>
/// <param name="Switch">The caller should switch to the group. False for companion windows that join the current group.</param>
public readonly record struct Placement(Guid GroupId, bool Created, bool Switch);

/// <param name="GroupId">Null if the window was not known.</param>
public readonly record struct Removal(Guid? GroupId, bool GroupRemoved);

public readonly record struct MoveResult(Guid SourceId, bool SourceRemoved);

/// <summary>
/// Keeps track of which window belongs to which scene and which scene is selected.
/// Knows nothing about Win32 or WPF, windows are identified by a number (the handle).
/// </summary>
public sealed class SceneRegistry
{
	private sealed class Group
	{
		public Guid Id { get; } = Guid.NewGuid();

		public required string Key { get; init; }

		public List<long> Windows { get; } = new();
	}

	private readonly List<Group> _groups = new();
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

		foreach (var (window, key) in windows)
		{
			if (FindGroupOf(window) is not null)
				continue;

			var group = FindByKey(key) ?? Add(key);
			group.Windows.Add(window);
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
		return new Placement(group.Id, created, Switch: true);
	}

	public Removal Remove(long window)
	{
		var group = FindGroupOf(window);
		if (group is null)
			return new Removal(null, false);

		group.Windows.Remove(window);

		if (group.Windows.Count > 0)
			return new Removal(group.Id, GroupRemoved: false);

		_groups.Remove(group);
		return new Removal(group.Id, GroupRemoved: true);
	}

	/// <returns>Null if nothing was moved: unknown window, unknown target or same group.</returns>
	public MoveResult? Move(long window, Guid target)
	{
		var source = FindGroupOf(window);
		var targetGroup = _groups.FirstOrDefault(g => g.Id == target);

		if (source is null || targetGroup is null || source == targetGroup)
			return null;

		source.Windows.Remove(window);
		targetGroup.Windows.Add(window);

		var removed = source.Windows.Count == 0;
		if (removed)
			_groups.Remove(source);

		return new MoveResult(source.Id, removed);
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

	private Group Add(string key)
	{
		var group = new Group { Key = key };
		_groups.Add(group);
		return group;
	}

	private Group? FindGroupOf(long window) => _groups.FirstOrDefault(g => g.Windows.Contains(window));

	private Group? FindByKey(string key) => _groups.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

	private static SceneGroupSnapshot Snapshot(Group group) => new(group.Id, group.Key, group.Windows.ToList());
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
