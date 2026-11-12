namespace StageManager.Core;

/// <summary>
/// The list of apps StageManager leaves alone. Entries are file names ("chrome.exe") or full paths of the executable, see
/// <see cref="AppIdentity.IsListed"/>. Pure list handling, so it can be tested without a screen.
/// </summary>
public static class ExcludedApps
{
	/// <summary>
	/// What is written to the list for an app: the file name. A path would break whenever an app moves into a folder with its version
	/// in the name, as Teams and Discord do with every update.
	/// </summary>
	public static string EntryFor(string? executablePath)
	{
		if (string.IsNullOrWhiteSpace(executablePath))
			return "";

		var path = executablePath.Trim();
		var index = path.LastIndexOfAny(new[] { '\\', '/' });
		return index < 0 ? path : path[(index + 1)..];
	}

	/// <summary>The list with an entry added. An app that is named already (by file name or by path) is not added twice.</summary>
	public static List<string> With(IEnumerable<string>? entries, string? entry)
	{
		var list = Clean(entries);

		if (string.IsNullOrWhiteSpace(entry))
			return list;

		var trimmed = entry.Trim();

		// "chrome" and "chrome.exe" and the full path are one app
		if (list.Any(e => Same(e, trimmed)))
			return list;

		list.Add(trimmed);
		return list;
	}

	/// <summary>The list without an entry.</summary>
	public static List<string> Without(IEnumerable<string>? entries, string? entry)
	{
		var list = Clean(entries);

		if (string.IsNullOrWhiteSpace(entry))
			return list;

		var trimmed = entry.Trim();
		list.RemoveAll(e => string.Equals(e, trimmed, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	public static bool Contains(IEnumerable<string>? entries, string? executablePath) => AppIdentity.IsListed(entries, executablePath);

	/// <summary>True if two entries name the same app: the same text, or one of them is the file name (with or without extension) of the other.</summary>
	private static bool Same(string a, string b)
	{
		if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
			return true;

		return Plain(a) == Plain(b);
	}

	private static string Plain(string entry)
	{
		var name = EntryFor(entry);
		var dot = name.LastIndexOf('.');
		return (dot <= 0 ? name : name[..dot]).ToUpperInvariant();
	}

	private static List<string> Clean(IEnumerable<string>? entries) =>
		(entries ?? Enumerable.Empty<string>())
			.Where(e => !string.IsNullOrWhiteSpace(e))
			.Select(e => e.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
}
