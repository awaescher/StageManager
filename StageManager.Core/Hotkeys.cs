namespace StageManager.Core;

/// <summary>The values are the MOD_* flags of RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
	None = 0,
	Alt = 1,
	Ctrl = 2,

	/// <summary>Not usable as part of the held keys: Shift together with Tab goes back.</summary>
	Shift = 4,

	Win = 8
}

public enum HotkeyActionKind
{
	/// <summary>The next scene, the most recently used first.</summary>
	Next,

	/// <summary>The previous scene, the least recently used first.</summary>
	Previous,

	/// <summary>The card at a position of the sidebar.</summary>
	Number
}

/// <param name="Number">For <see cref="HotkeyActionKind.Number"/> the position of the card, starting at 1.</param>
public readonly record struct HotkeyAction(HotkeyActionKind Kind, int Number = 0)
{
	public static readonly HotkeyAction Next = new(HotkeyActionKind.Next);
	public static readonly HotkeyAction Previous = new(HotkeyActionKind.Previous);

	public static HotkeyAction Card(int number) => new(HotkeyActionKind.Number, number);

	public static IEnumerable<HotkeyAction> All() =>
		new[] { Next, Previous }.Concat(Enumerable.Range(1, HotkeyText.MaxNumber).Select(Card));
}

/// <summary>Reads and writes the keys that are held to switch scenes, as text like "Ctrl+Alt+Win".</summary>
public static class HotkeyText
{
	public const HotkeyModifiers Default = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win;

	public const int MaxNumber = 9;

	private static readonly (HotkeyModifiers Flag, string Name)[] Names =
	{
		(HotkeyModifiers.Ctrl, "Ctrl"),
		(HotkeyModifiers.Alt, "Alt"),
		(HotkeyModifiers.Shift, "Shift"),
		(HotkeyModifiers.Win, "Win")
	};

	/// <summary>True if the keys can be held to switch scenes: at least one modifier, and not Shift, which goes back.</summary>
	public static bool IsUsable(HotkeyModifiers modifiers) =>
		modifiers != HotkeyModifiers.None && !modifiers.HasFlag(HotkeyModifiers.Shift);

	/// <param name="name">Gives the name of a key ("Ctrl", "Alt", "Shift", "Win", "Tab") on the keyboard of the user. Null writes the invariant names, which is what the settings file holds.</param>
	public static string Format(HotkeyModifiers modifiers, Func<string, string>? name = null) =>
		string.Join("+", Names.Where(n => modifiers.HasFlag(n.Flag)).Select(n => name?.Invoke(n.Name) ?? n.Name));

	/// <summary>Reads "Ctrl+Alt+Win" in any order and case. "Control" and "Windows" are accepted as well.</summary>
	public static bool TryParse(string? text, out HotkeyModifiers modifiers)
	{
		modifiers = HotkeyModifiers.None;

		if (string.IsNullOrWhiteSpace(text))
			return false;

		foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			var flag = part.ToUpperInvariant() switch
			{
				"CTRL" or "CONTROL" or "STRG" => HotkeyModifiers.Ctrl,
				"ALT" => HotkeyModifiers.Alt,
				"SHIFT" => HotkeyModifiers.Shift,
				"WIN" or "WINDOWS" or "META" => HotkeyModifiers.Win,
				_ => HotkeyModifiers.None
			};

			if (flag == HotkeyModifiers.None)
			{
				modifiers = HotkeyModifiers.None;
				return false;
			}

			modifiers |= flag;
		}

		if (IsUsable(modifiers))
			return true;

		modifiers = HotkeyModifiers.None;
		return false;
	}

	public static string Describe(HotkeyModifiers modifiers, HotkeyAction action, Func<string, string>? name = null)
	{
		var shift = name?.Invoke("Shift") ?? "Shift";
		var tab = name?.Invoke("Tab") ?? "Tab";

		return action.Kind switch
		{
			HotkeyActionKind.Next => $"{Format(modifiers, name)}+{tab}",
			HotkeyActionKind.Previous => $"{Format(modifiers, name)}+{shift}+{tab}",
			_ => $"{Format(modifiers, name)}+{action.Number}"
		};
	}
}

/// <summary>Who is known to use a key combination. Windows only says "taken", not by whom.</summary>
public enum HotkeyOwner
{
	Unknown,
	WindowSwitcher,
	TaskView,
	Taskbar,
	TaskbarJumpLists
}

/// <summary>A hint for keys that work but get in the way of other apps.</summary>
public enum HotkeyWarning
{
	None,
	CtrlAlone,
	AltAlone,
	CtrlAlt
}

/// <param name="Keys">The keys, e.g. "Win+1...9" when several digits are taken.</param>
public readonly record struct HotkeyConflict(string Keys, HotkeyOwner Owner);

/// <summary>What is known about key combinations that Windows and common apps use. Returns codes, the texts are up to the UI.</summary>
public static class HotkeyAdvice
{
	public static HotkeyOwner KnownOwner(HotkeyModifiers modifiers, HotkeyAction action)
	{
		var tab = action.Kind != HotkeyActionKind.Number;

		return (modifiers, tab) switch
		{
			(HotkeyModifiers.Alt, true) => HotkeyOwner.WindowSwitcher,
			(HotkeyModifiers.Win, true) => HotkeyOwner.TaskView,
			(HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, true) => HotkeyOwner.WindowSwitcher,
			(HotkeyModifiers.Win, false) => HotkeyOwner.Taskbar,
			(HotkeyModifiers.Alt | HotkeyModifiers.Win, false) => HotkeyOwner.TaskbarJumpLists,
			(HotkeyModifiers.Win | HotkeyModifiers.Ctrl, false) => HotkeyOwner.Taskbar,
			_ => HotkeyOwner.Unknown
		};
	}

	/// <summary>One entry for each key combination that could not be taken. All digits that failed are summed up in one entry.</summary>
	/// <param name="name">Gives the name of a key on the keyboard of the user, see <see cref="HotkeyText.Format"/>.</param>
	public static IReadOnlyList<HotkeyConflict> Conflicts(HotkeyModifiers modifiers, IEnumerable<HotkeyAction> failed, Func<string, string>? name = null)
	{
		var list = failed.ToList();
		var conflicts = new List<HotkeyConflict>();

		foreach (var action in list.Where(a => a.Kind != HotkeyActionKind.Number))
			conflicts.Add(new HotkeyConflict(HotkeyText.Describe(modifiers, action, name), KnownOwner(modifiers, action)));

		var numbers = list.Where(a => a.Kind == HotkeyActionKind.Number).Select(a => a.Number).OrderBy(n => n).ToList();
		if (numbers.Count > 0)
			conflicts.Add(new HotkeyConflict(Format(modifiers, numbers, name), KnownOwner(modifiers, HotkeyAction.Card(numbers[0]))));

		return conflicts;
	}

	public static HotkeyWarning Warning(HotkeyModifiers modifiers) => modifiers switch
	{
		HotkeyModifiers.Ctrl => HotkeyWarning.CtrlAlone,
		HotkeyModifiers.Alt => HotkeyWarning.AltAlone,
		HotkeyModifiers.Ctrl | HotkeyModifiers.Alt => HotkeyWarning.CtrlAlt,
		_ => HotkeyWarning.None
	};

	private static string Format(HotkeyModifiers modifiers, List<int> numbers, Func<string, string>? name)
	{
		var prefix = HotkeyText.Format(modifiers, name) + "+";
		var consecutive = numbers.Count > 2 && numbers.Zip(numbers.Skip(1), (a, b) => b - a).All(d => d == 1);
		return consecutive
			? $"{prefix}{numbers[0]}...{numbers[^1]}"
			: prefix + string.Join(", ", numbers);
	}
}
/// <summary>
/// The scene a held hotkey has picked while Tab is pressed again and again. Pure logic: the list is frozen when picking starts, so the
/// order does not jump while the sidebar changes under it.
/// </summary>
public sealed class ScenePicker
{
	private IReadOnlyList<Guid> _candidates = Array.Empty<Guid>();
	private int _index = -1;

	public bool IsActive => _index >= 0;

	public Guid? Picked => IsActive ? _candidates[_index] : null;

	/// <param name="candidates">The scenes to choose from, most recently used first. Only read when picking starts.</param>
	/// <returns>The scene that is picked now, or null if there is nothing to pick.</returns>
	public Guid? Step(IReadOnlyList<Guid> candidates, bool backwards)
	{
		if (!IsActive)
		{
			if (candidates.Count == 0)
				return null;

			_candidates = candidates.ToArray();
			_index = backwards ? _candidates.Count - 1 : 0;
			return Picked;
		}

		var count = _candidates.Count;
		_index = (_index + (backwards ? count - 1 : 1)) % count;
		return Picked;
	}

	public void End()
	{
		_candidates = Array.Empty<Guid>();
		_index = -1;
	}
}
