namespace StageManager.Core;

public sealed class SettingsService
{
	private readonly SettingsStore _store;
	private readonly object _lock = new();
	private AppSettings _current;

	public SettingsService(SettingsStore store)
	{
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_current = _store.Load();
	}

	/// <summary>
	/// The current settings. Treat this as a read-only snapshot and change settings with <see cref="Update"/> only.
	/// </summary>
	public AppSettings Current
	{
		get
		{
			lock (_lock)
				return _current;
		}
	}

	/// <summary>
	/// Raised after a change was applied and saved. Handlers run on the thread that called <see cref="Update"/>.
	/// </summary>
	public event EventHandler<AppSettings>? Changed;

	public void Update(Action<AppSettings> change)
	{
		lock (_lock)
		{
			var updated = _current.Clone();
			change(updated);

			if (!Apply(updated))
				return;
		}

		Changed?.Invoke(this, Current);
	}

	/// <summary>Brings all settings back to their defaults.</summary>
	public void Reset()
	{
		lock (_lock)
		{
			if (!Apply(new AppSettings()))
				return;
		}

		Changed?.Invoke(this, Current);
	}

	/// <returns>False if nothing changed.</returns>
	private bool Apply(AppSettings updated)
	{
		updated.Normalize();

		if (updated.Serialize() == _current.Serialize())
			return false;

		_store.Save(updated);
		_current = updated;
		return true;
	}
}
