using System.Text.Json;

namespace StageManager.Core;

/// <summary>What the parker needs from Windows. Windows are identified by their handle as a number.</summary>
public interface IWindowPlacer
{
	/// <summary>True if the handle still belongs to the window of that process. Handles are reused after a window is gone.</summary>
	bool Exists(long window, int processId);

	int ProcessIdOf(long window);

	Area? GetRect(long window);

	/// <summary>The visible frame, which is what a thumbnail shows. Without the invisible borders of the window.</summary>
	Area? GetFrame(long window);

	bool IsMinimized(long window);

	bool IsMaximized(long window);

	/// <summary>The rectangle a maximized window gets back when it is restored, as Windows keeps it. Only handed back to <see cref="Maximize"/>.</summary>
	Area? GetRestoredRect(long window);

	/// <summary>
	/// Takes a maximized window out of the maximized state without changing what is seen: it keeps <paramref name="rect"/>.
	/// A maximized window cannot be moved, Windows puts it back.
	/// </summary>
	bool Normalize(long window, Area rect);

	/// <summary>Maximizes the window again, <paramref name="restored"/> is what it gets when it is restored later.</summary>
	bool Maximize(long window, Area restored);

	/// <summary>Moves the window so that its top left corner is at the one of <paramref name="rect"/>. The size does not change.</summary>
	bool Move(long window, Area rect, bool refreshFrame);

	/// <summary>Visible windows that belong to the window, like dialogs.</summary>
	IReadOnlyList<long> OwnedWindows(long window);

	IReadOnlyList<Area> Monitors();
}

/// <summary>A window that was moved out of the screen and has to be brought back.</summary>
public sealed record ParkedWindow
{
	public long Window { get; init; }

	public int ProcessId { get; init; }

	/// <summary>The window this one belongs to, or 0. Owned windows follow their owner.</summary>
	public long Owner { get; init; }

	public Area Original { get; init; }

	public Area OriginalFrame { get; init; }

	public Area Parked { get; init; }

	/// <summary>The window was maximized. It is parked in the normal state and maximized again when it comes back.</summary>
	public bool Maximized { get; init; }

	/// <summary>The rectangle a maximized window gets when it is restored, null for other windows.</summary>
	public Area? Restored { get; init; }
}

/// <summary>Remembers parked windows, so they come back even if the app was killed.</summary>
public interface IParkingJournal
{
	IReadOnlyList<ParkedWindow> Load();

	void Save(IReadOnlyList<ParkedWindow> entries);
}

public sealed class FileParkingJournal : IParkingJournal
{
	private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

	private readonly string _filePath;

	public FileParkingJournal(string filePath)
	{
		_filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
	}

	public IReadOnlyList<ParkedWindow> Load()
	{
		try
		{
			if (!File.Exists(_filePath))
				return Array.Empty<ParkedWindow>();

			return JsonSerializer.Deserialize<List<ParkedWindow>>(File.ReadAllText(_filePath), Options) ?? new List<ParkedWindow>();
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			return Array.Empty<ParkedWindow>();
		}
	}

	/// <summary>Written through a temporary file, so a crash while saving cannot leave half a journal behind.</summary>
	public void Save(IReadOnlyList<ParkedWindow> entries)
	{
		try
		{
			if (entries.Count == 0)
			{
				File.Delete(_filePath);
				return;
			}

			var directory = Path.GetDirectoryName(_filePath);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);

			var temp = _filePath + ".tmp";
			File.WriteAllText(temp, JsonSerializer.Serialize(entries, Options));
			File.Move(temp, _filePath, overwrite: true);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// without a journal windows are only lost if the app is killed while they are parked
		}
	}
}

/// <summary>
/// Hides windows by moving them out of the screen and brings them back. Unlike minimizing, this keeps the picture
/// DWM shows of a window: Chromium based windows lose theirs when they are minimized.
/// </summary>
public sealed class WindowParker
{
	private readonly IWindowPlacer _placer;
	private readonly IParkingJournal _journal;
	private readonly object _lock = new();
	private readonly List<ParkedWindow> _entries = new();

	public WindowParker(IWindowPlacer placer, IParkingJournal journal)
	{
		_placer = placer ?? throw new ArgumentNullException(nameof(placer));
		_journal = journal ?? throw new ArgumentNullException(nameof(journal));
	}

	public int Count
	{
		get
		{
			lock (_lock)
				return _entries.Count;
		}
	}

	public bool IsParked(long window)
	{
		lock (_lock)
			return _entries.Any(e => e.Window == window);
	}

	/// <summary>Where a parked window was before, null if it is not parked.</summary>
	public Area? OriginalRectOf(long window)
	{
		lock (_lock)
			return _entries.FirstOrDefault(e => e.Window == window)?.Original;
	}

	/// <summary>The visible frame a parked window had before, null if it is not parked.</summary>
	public Area? OriginalFrameOf(long window)
	{
		lock (_lock)
			return _entries.FirstOrDefault(e => e.Window == window)?.OriginalFrame;
	}

	/// <summary>Moves a window and the windows that belong to it out of the screen.</summary>
	/// <returns>False if it cannot be parked: it is minimized already, gone or there is no free place. Then it has to be hidden another way.</returns>
	public bool Park(long window)
	{
		lock (_lock)
		{
			if (_entries.Any(e => e.Window == window))
				return true;

			var processId = _placer.ProcessIdOf(window);
			if (!_placer.Exists(window, processId) || _placer.IsMinimized(window))
				return false;

			if (_placer.GetRect(window) is not Area rect)
				return false;

			if (ParkingSpot.Find(rect, _placer.Monitors()) is not Area spot)
				return false;

			var dx = spot.Left - rect.Left;
			var dy = spot.Top - rect.Top;

			// Windows does not move a maximized window, it has to leave that state first
			var maximized = _placer.IsMaximized(window);
			Area? restored = null;
			if (maximized && (restored = _placer.GetRestoredRect(window)) is null)
				return false;

			var parked = new List<ParkedWindow> { Entry(window, processId, 0, rect, spot) with { Maximized = maximized, Restored = restored } };

			foreach (var owned in _placer.OwnedWindows(window))
			{
				if (_entries.Any(e => e.Window == owned) || _placer.IsMinimized(owned) || _placer.GetRect(owned) is not Area ownedRect)
					continue;

				var target = new Area(ownedRect.Left + dx, ownedRect.Top + dy, ownedRect.Right + dx, ownedRect.Bottom + dy);
				parked.Add(Entry(owned, _placer.ProcessIdOf(owned), window, ownedRect, target));
			}

			// the journal first: if the app dies right after the move, the window has to be found again
			_entries.AddRange(parked);
			_journal.Save(_entries);

			if (maximized && !_placer.Normalize(window, rect))
			{
				_entries.RemoveAll(parked.Contains);
				_journal.Save(_entries);
				return false;
			}

			foreach (var entry in parked)
				_placer.Move(entry.Window, entry.Parked, refreshFrame: false);

			return true;
		}
	}

	/// <summary>Brings a parked window and the windows that belong to it back to where they were.</summary>
	public bool Unpark(long window)
	{
		lock (_lock)
		{
			var entries = _entries.Where(e => e.Window == window || e.Owner == window).ToList();
			if (entries.Count == 0)
				return false;

			foreach (var entry in entries)
				Bring(entry);

			_entries.RemoveAll(entries.Contains);
			_journal.Save(_entries);
			return true;
		}
	}

	/// <summary>Forgets a window that is gone, without moving anything.</summary>
	public void Forget(long window)
	{
		lock (_lock)
		{
			if (_entries.RemoveAll(e => e.Window == window) > 0)
				_journal.Save(_entries);
		}
	}

	/// <summary>Brings back every window. For the end of the app and for emergencies.</summary>
	public int RestoreAll()
	{
		lock (_lock)
		{
			var restored = _entries.Count;

			foreach (var entry in _entries.ToList())
				Bring(entry);

			_entries.Clear();
			_journal.Save(_entries);
			return restored;
		}
	}

	/// <summary>
	/// Brings back the windows a previous run left out of the screen, e.g. because it was killed. Entries of windows that are gone are dropped.
	/// </summary>
	public int Recover()
	{
		lock (_lock)
		{
			var remembered = _journal.Load();
			var restored = 0;

			foreach (var entry in remembered)
			{
				if (!_placer.Exists(entry.Window, entry.ProcessId))
					continue;

				Bring(entry);
				restored++;
			}

			_entries.Clear();
			_journal.Save(_entries);
			return restored;
		}
	}

	private void Bring(ParkedWindow entry)
	{
		if (!_placer.Exists(entry.Window, entry.ProcessId))
			return;

		// a window that came back on its own, or that the user placed on the screen, stays where it is
		if (_placer.GetRect(entry.Window) is Area now && _placer.Monitors().Any(m => FlightPlanner.Intersects(m, now)))
			return;

		// a maximized window is maximized again where it was, the placement brings it there. A move before it would arrive after it
		if (entry.Maximized && entry.Restored is Area restored && _placer.Maximize(entry.Window, restored))
			return;

		_placer.Move(entry.Window, entry.Original, refreshFrame: true);
	}

	private ParkedWindow Entry(long window, int processId, long owner, Area original, Area parked) => new()
	{
		Window = window,
		ProcessId = processId,
		Owner = owner,
		Original = original,
		OriginalFrame = _placer.GetFrame(window) ?? original,
		Parked = parked
	};
}
