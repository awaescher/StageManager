namespace StageManager.Core;

/// <summary>Identity of an app, independent of how many windows or processes it has.</summary>
public static class AppIdentity
{
	/// <summary>The key windows are grouped by: the full path of the executable, in upper case. Empty if the path is unknown.</summary>
	public static string KeyFromPath(string? executablePath) =>
		string.IsNullOrWhiteSpace(executablePath) ? "" : executablePath.Trim().ToUpperInvariant();

	/// <summary>
	/// True if an entry of a list from the settings names the app. An entry is either the file name ("calc.exe", "calc")
	/// or the full path of the executable. Case does not matter.
	/// </summary>
	public static bool IsListed(IEnumerable<string>? entries, string? executablePath)
	{
		if (entries is null || string.IsNullOrWhiteSpace(executablePath))
			return false;

		var path = executablePath.Trim();
		var fileName = GetFileName(path);
		var fileNameWithoutExtension = RemoveExtension(fileName);

		foreach (var raw in entries)
		{
			var entry = raw?.Trim();
			if (string.IsNullOrEmpty(entry))
				continue;

			if (string.Equals(entry, path, StringComparison.OrdinalIgnoreCase))
				return true;

			if (entry.Contains('\\') || entry.Contains('/'))
				continue;

			if (string.Equals(entry, fileName, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(entry, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	private static string GetFileName(string path)
	{
		var index = path.LastIndexOfAny(new[] { '\\', '/' });
		return index < 0 ? path : path[(index + 1)..];
	}

	private static string RemoveExtension(string fileName)
	{
		var index = fileName.LastIndexOf('.');
		return index <= 0 ? fileName : fileName[..index];
	}
}
