using System.Text.Json;

namespace StageManager.Core;

public sealed class SettingsStore
{
	private readonly string _filePath;
	private readonly Action<string>? _warn;

	public SettingsStore(string filePath, Action<string>? warn = null)
	{
		_filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
		_warn = warn;
	}

	public string FilePath => _filePath;

	/// <summary>
	/// Loads the settings. A missing file is created with defaults, an unreadable file is kept as ".bad" and replaced by defaults.
	/// </summary>
	public AppSettings Load()
	{
		if (!File.Exists(_filePath))
		{
			var defaults = new AppSettings().Normalize();
			TrySave(defaults);
			return defaults;
		}

		try
		{
			var loaded = AppSettings.Deserialize(File.ReadAllText(_filePath));
			if (loaded is not null)
				return loaded.Normalize();

			throw new JsonException("The settings file is empty.");
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			_warn?.Invoke($"Could not read {_filePath}, using defaults: {ex.Message}");
			TryBackup();

			var defaults = new AppSettings().Normalize();
			TrySave(defaults);
			return defaults;
		}
	}

	/// <summary>
	/// Writes to a temporary file first, so a crash while saving can never leave a half-written settings file.
	/// </summary>
	public void Save(AppSettings settings)
	{
		var directory = Path.GetDirectoryName(_filePath);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		var temp = _filePath + ".tmp";
		File.WriteAllText(temp, settings.Serialize());
		File.Move(temp, _filePath, overwrite: true);
	}

	private void TrySave(AppSettings settings)
	{
		try
		{
			Save(settings);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_warn?.Invoke($"Could not write {_filePath}: {ex.Message}");
		}
	}

	private void TryBackup()
	{
		try
		{
			File.Move(_filePath, _filePath + ".bad", overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_warn?.Invoke($"Could not back up {_filePath}: {ex.Message}");
		}
	}
}
