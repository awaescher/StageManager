namespace StageManager.Core;

/// <summary>The desktop icons of Windows. Switching them is a toggle, so the caller has to know the current state.</summary>
public interface IDesktopIcons
{
	/// <summary>The state Windows reports. It can lag behind for a moment after <see cref="Toggle"/>.</summary>
	bool IsVisible { get; }

	/// <returns>False if the desktop could not be reached and nothing was switched.</returns>
	bool Toggle();
}

/// <summary>Keeps the state the desktop icons had before StageManager touched them, so it survives a crash.</summary>
public interface IDesktopIconsState
{
	bool? LoadOriginal();

	void SaveOriginal(bool visible);

	void Clear();
}

public sealed class FileDesktopIconsState : IDesktopIconsState
{
	private readonly string _filePath;

	public FileDesktopIconsState(string filePath)
	{
		_filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
	}

	public bool? LoadOriginal()
	{
		try
		{
			return File.Exists(_filePath) ? File.ReadAllText(_filePath).Trim() switch
			{
				"visible" => true,
				"hidden" => false,
				_ => null
			} : null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	public void SaveOriginal(bool visible)
	{
		try
		{
			var directory = Path.GetDirectoryName(_filePath);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);

			File.WriteAllText(_filePath, visible ? "visible" : "hidden");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// not being able to remember the state only matters after a crash
		}
	}

	public void Clear()
	{
		try
		{
			File.Delete(_filePath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}

/// <summary>
/// Decides when the desktop icons are shown or hidden. Keeps track of the state itself, because the state Windows reports lags behind
/// and a second toggle would switch back.
/// </summary>
public sealed class DesktopIconsPolicy
{
	private readonly IDesktopIcons _icons;
	private readonly IDesktopIconsState _state;
	private readonly Func<DesktopIconsSettings> _settings;
	private readonly object _lock = new();

	private bool _started;
	private bool _original;
	private bool _current;
	private bool _touched;
	private bool _sceneActive = true;

	public DesktopIconsPolicy(IDesktopIcons icons, IDesktopIconsState state, Func<DesktopIconsSettings> settings)
	{
		_icons = icons ?? throw new ArgumentNullException(nameof(icons));
		_state = state ?? throw new ArgumentNullException(nameof(state));
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
	}

	/// <summary>The state the desktop icons had before StageManager changed them.</summary>
	public bool OriginalVisible
	{
		get
		{
			lock (_lock)
				return _original;
		}
	}

	public bool CurrentVisible
	{
		get
		{
			lock (_lock)
				return _current;
		}
	}

	/// <summary>Reads the current state. A remembered state from a previous run that did not exit properly wins over it.</summary>
	public void Start()
	{
		lock (_lock)
		{
			var remembered = _state.LoadOriginal();

			_current = _icons.IsVisible;
			_original = remembered ?? _current;
			_touched = remembered is not null;
			_started = true;
		}
	}

	/// <param name="sceneActive">False in the desktop view, when no scene is selected.</param>
	public void Apply(bool sceneActive)
	{
		lock (_lock)
		{
			_sceneActive = sceneActive;
			ApplyDesired(Desired(_settings()));
		}
	}

	/// <summary>Applies the settings again, e.g. after the mode was changed.</summary>
	public void Reapply()
	{
		lock (_lock)
			ApplyDesired(Desired(_settings()));
	}

	/// <summary>Reads the state from Windows again, e.g. after the Explorer restarted.</summary>
	public void Resync()
	{
		lock (_lock)
		{
			if (!_started)
				return;

			_current = _icons.IsVisible;
			ApplyDesired(Desired(_settings()));
		}
	}

	public void Stop()
	{
		lock (_lock)
		{
			if (!_started)
				return;

			if (_settings().RestoreOnExit)
				Switch(_original);

			_state.Clear();
			_started = false;
			_touched = false;
		}
	}

	private bool? Desired(DesktopIconsSettings settings) => settings.Mode switch
	{
		DesktopIconsMode.Manage => !_sceneActive,
		DesktopIconsMode.AlwaysHidden => false,
		// never go beyond what the user had, but undo own changes
		_ => _touched ? _original : null
	};

	private void ApplyDesired(bool? desired)
	{
		if (!_started || desired is null)
			return;

		Switch(desired.Value);
	}

	private void Switch(bool visible)
	{
		if (visible == _current)
			return;

		// remember the original state before the first change, a crash afterwards would otherwise lose it
		if (!_touched)
		{
			_state.SaveOriginal(_original);
			_touched = true;
		}

		if (_icons.Toggle())
			_current = visible;
	}
}
