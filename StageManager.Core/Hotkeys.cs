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

	public static string Format(HotkeyModifiers modifiers) =>
		string.Join("+", Names.Where(n => modifiers.HasFlag(n.Flag)).Select(n => n.Name));

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

	public static string Describe(HotkeyModifiers modifiers, HotkeyAction action) => action.Kind switch
	{
		HotkeyActionKind.Next => Format(modifiers) + "+Tab",
		HotkeyActionKind.Previous => Format(modifiers) + "+Shift+Tab",
		_ => Format(modifiers) + "+" + action.Number
	};
}

/// <summary>What is known about key combinations that Windows and common apps use. Windows only says "taken", not by whom.</summary>
public static class HotkeyAdvice
{
	/// <summary>Who is known to use the combination, or null.</summary>
	public static string? KnownOwner(HotkeyModifiers modifiers, HotkeyAction action)
	{
		var tab = action.Kind != HotkeyActionKind.Number;

		return (modifiers, tab) switch
		{
			(HotkeyModifiers.Alt, true) => "the Windows window switcher",
			(HotkeyModifiers.Win, true) => "the Windows task view",
			(HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, true) => "the Windows window switcher",
			(HotkeyModifiers.Alt | HotkeyModifiers.Win, true) => "Windows",
			(HotkeyModifiers.Win, false) => "the apps on the Windows taskbar",
			(HotkeyModifiers.Alt | HotkeyModifiers.Win, false) => "the jump lists of the Windows taskbar",
			(HotkeyModifiers.Win | HotkeyModifiers.Ctrl, false) => "the apps on the Windows taskbar",
			_ => null
		};
	}

	/// <summary>One line for each key combination that could not be taken.</summary>
	public static IReadOnlyList<string> Conflicts(HotkeyModifiers modifiers, IEnumerable<HotkeyAction> failed)
	{
		var list = failed.ToList();
		var lines = new List<string>();

		foreach (var action in list.Where(a => a.Kind != HotkeyActionKind.Number))
			lines.Add(Line(HotkeyText.Describe(modifiers, action), KnownOwner(modifiers, action)));

		var numbers = list.Where(a => a.Kind == HotkeyActionKind.Number).Select(a => a.Number).OrderBy(n => n).ToList();
		if (numbers.Count > 0)
		{
			var keys = Format(modifiers, numbers);
			lines.Add(Line(keys, KnownOwner(modifiers, HotkeyAction.Card(numbers[0]))));
		}

		return lines;
	}

	/// <summary>A hint for keys that work but get in the way of other apps, or null.</summary>
	public static string? Warning(HotkeyModifiers modifiers)
	{
		if (modifiers == HotkeyModifiers.Ctrl)
			return "Ctrl alone takes Ctrl+Tab and Ctrl+1...9 from browsers and editors. Add another key.";

		if (modifiers == HotkeyModifiers.Alt)
			return "Alt alone takes Alt+1...9 from many apps. Add another key.";

		if (modifiers == (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt))
			return "Ctrl+Alt is AltGr on many keyboards (e.g. German): Ctrl+Alt+digits types characters such as { [ ] }.";

		return null;
	}

	private static string Format(HotkeyModifiers modifiers, List<int> numbers)
	{
		var prefix = HotkeyText.Format(modifiers) + "+";
		var consecutive = numbers.Count > 2 && numbers.Zip(numbers.Skip(1), (a, b) => b - a).All(d => d == 1);
		return consecutive
			? $"{prefix}{numbers[0]}...{numbers[^1]}"
			: prefix + string.Join(", ", numbers);
	}

	private static string Line(string keys, string? owner) =>
		$"{keys} is already used by {owner ?? "Windows or another app"}.";
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
