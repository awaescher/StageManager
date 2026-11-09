using StageManager.Core;

namespace StageManager.Tests;

public class ParkingSpotTests
{
	private static readonly Area Primary = new(0, 0, 1920, 1080);

	[Fact]
	public void The_window_is_parked_beyond_the_left_edge_of_a_single_monitor()
	{
		var spot = ParkingSpot.Find(new Area(300, 200, 1100, 700), new[] { Primary });

		Assert.NotNull(spot);
		Assert.Equal(new Area(-960, 200, -160, 700), spot);
	}

	[Fact]
	public void The_parked_window_keeps_its_size()
	{
		var window = new Area(300, 200, 1100, 700);

		var spot = ParkingSpot.Find(window, new[] { Primary })!.Value;

		Assert.Equal(window.Width, spot.Width);
		Assert.Equal(window.Height, spot.Height);
	}

	[Fact]
	public void The_parked_window_does_not_touch_any_monitor()
	{
		var monitors = new[] { Primary, new Area(1920, 0, 3840, 1080) };

		var spot = ParkingSpot.Find(new Area(300, 200, 1100, 700), monitors)!.Value;

		Assert.All(monitors, m => Assert.False(FlightPlanner.Intersects(m, spot)));
	}

	[Fact]
	public void Windows_stay_at_the_monitor_they_are_on()
	{
		var left = new Area(0, 0, 1920, 1080);
		var right = new Area(1920, 0, 3840, 1080);
		var monitors = new[] { left, right };
		var window = new Area(2400, 200, 3200, 700);

		var spot = ParkingSpot.Find(window, monitors)!.Value;

		Assert.Equal(1, ParkingSpot.NearestMonitor(spot, monitors));
	}

	[Fact]
	public void A_window_on_the_left_of_two_monitors_is_parked_on_the_left_side()
	{
		var monitors = new[] { new Area(0, 0, 1920, 1080), new Area(1920, 0, 3840, 1080) };

		var spot = ParkingSpot.Find(new Area(300, 200, 1100, 700), monitors)!.Value;

		Assert.True(spot.Right < 0);
		Assert.Equal(0, ParkingSpot.NearestMonitor(spot, monitors));
	}

	[Fact]
	public void Monitors_above_each_other_park_on_the_free_side()
	{
		var top = new Area(0, -1080, 1920, 0);
		var bottom = new Area(0, 0, 1920, 1080);
		var monitors = new[] { top, bottom };

		var spot = ParkingSpot.Find(new Area(300, 200, 1100, 700), monitors)!.Value;

		Assert.All(monitors, m => Assert.False(FlightPlanner.Intersects(m, spot)));
		Assert.Equal(1, ParkingSpot.NearestMonitor(spot, monitors));
	}

	[Fact]
	public void A_monitor_with_a_negative_origin_is_handled()
	{
		var monitors = new[] { new Area(-1920, 0, 0, 1080), new Area(0, 0, 1920, 1080) };

		var spot = ParkingSpot.Find(new Area(-1500, 100, -700, 600), monitors)!.Value;

		Assert.True(spot.Right < -1920);
		Assert.Equal(0, ParkingSpot.NearestMonitor(spot, monitors));
	}

	[Fact]
	public void Nothing_is_found_without_monitors_or_for_an_empty_window()
	{
		Assert.Null(ParkingSpot.Find(new Area(0, 0, 100, 100), Array.Empty<Area>()));
		Assert.Null(ParkingSpot.Find(new Area(0, 0, 0, 0), new[] { Primary }));
	}

	[Fact]
	public void The_nearest_monitor_is_the_one_with_the_largest_overlap()
	{
		var monitors = new[] { new Area(0, 0, 1000, 1000), new Area(1000, 0, 2000, 1000) };

		Assert.Equal(0, ParkingSpot.NearestMonitor(new Area(800, 0, 1100, 100), monitors));
		Assert.Equal(1, ParkingSpot.NearestMonitor(new Area(900, 0, 1500, 100), monitors));
	}

	[Fact]
	public void A_rectangle_outside_everything_belongs_to_the_closest_monitor()
	{
		var monitors = new[] { new Area(0, 0, 1000, 1000), new Area(1000, 0, 2000, 1000) };

		Assert.Equal(0, ParkingSpot.NearestMonitor(new Area(-500, 0, -100, 100), monitors));
		Assert.Equal(1, ParkingSpot.NearestMonitor(new Area(2100, 0, 2500, 100), monitors));
	}
}

public class WindowParkerTests
{
	private sealed class FakePlacer : IWindowPlacer
	{
		public Dictionary<long, (int Process, Area Rect, bool Minimized)> Windows { get; } = new();

		public Dictionary<long, List<long>> Owned { get; } = new();

		public List<Area> MonitorList { get; } = new() { new Area(0, 0, 1920, 1080) };

		public List<(long Window, Area Rect, bool Refresh)> Moves { get; } = new();

		public bool Exists(long window, int processId) => Windows.TryGetValue(window, out var w) && w.Process == processId;

		public int ProcessIdOf(long window) => Windows.TryGetValue(window, out var w) ? w.Process : 0;

		public Area? GetRect(long window) => Windows.TryGetValue(window, out var w) ? w.Rect : null;

		// the visible frame is the rectangle without 7 invisible pixels at the sides
		public Area? GetFrame(long window) => GetRect(window) is Area r ? new Area(r.Left + 7, r.Top, r.Right - 7, r.Bottom - 7) : null;

		public bool IsMinimized(long window) => Windows.TryGetValue(window, out var w) && w.Minimized;

		public bool Move(long window, Area rect, bool refreshFrame)
		{
			if (!Windows.TryGetValue(window, out var w))
				return false;

			Windows[window] = w with { Rect = rect };
			Moves.Add((window, rect, refreshFrame));
			return true;
		}

		public IReadOnlyList<long> OwnedWindows(long window) => Owned.TryGetValue(window, out var list) ? list : new List<long>();

		public IReadOnlyList<Area> Monitors() => MonitorList;
	}

	private sealed class MemoryJournal : IParkingJournal
	{
		public List<ParkedWindow> Entries { get; private set; } = new();

		public int Saves { get; private set; }

		public IReadOnlyList<ParkedWindow> Load() => Entries.ToList();

		public void Save(IReadOnlyList<ParkedWindow> entries)
		{
			Entries = entries.ToList();
			Saves++;
		}
	}

	private static readonly Area Rect = new(300, 200, 1100, 700);

	private readonly FakePlacer _placer = new();
	private readonly MemoryJournal _journal = new();

	private WindowParker Create()
	{
		_placer.Windows[1] = (100, Rect, false);
		return new WindowParker(_placer, _journal);
	}

	[Fact]
	public void Park_moves_the_window_out_of_the_screen_and_remembers_it()
	{
		var p = Create();

		Assert.True(p.Park(1));

		Assert.True(p.IsParked(1));
		Assert.True(_placer.Windows[1].Rect.Right < 0);
		Assert.Single(_journal.Entries);
		Assert.Equal(Rect, _journal.Entries[0].Original);
	}

	[Fact]
	public void The_journal_is_written_before_the_window_moves()
	{
		var p = Create();
		var journalHadItWhenMoved = false;
		var placer = new MovingPlacer(_placer, () => journalHadItWhenMoved = _journal.Entries.Count == 1);
		var parker = new WindowParker(placer, _journal);

		parker.Park(1);

		Assert.True(journalHadItWhenMoved);
	}

	private sealed class MovingPlacer : IWindowPlacer
	{
		private readonly FakePlacer _inner;
		private readonly Action _onMove;

		public MovingPlacer(FakePlacer inner, Action onMove)
		{
			_inner = inner;
			_onMove = onMove;
		}

		public bool Exists(long window, int processId) => _inner.Exists(window, processId);
		public int ProcessIdOf(long window) => _inner.ProcessIdOf(window);
		public Area? GetRect(long window) => _inner.GetRect(window);
		public Area? GetFrame(long window) => _inner.GetFrame(window);
		public bool IsMinimized(long window) => _inner.IsMinimized(window);
		public IReadOnlyList<long> OwnedWindows(long window) => _inner.OwnedWindows(window);
		public IReadOnlyList<Area> Monitors() => _inner.Monitors();

		public bool Move(long window, Area rect, bool refreshFrame)
		{
			_onMove();
			return _inner.Move(window, rect, refreshFrame);
		}
	}

	[Fact]
	public void Unpark_brings_the_window_back_and_refreshes_its_frame()
	{
		var p = Create();
		p.Park(1);

		Assert.True(p.Unpark(1));

		Assert.Equal(Rect, _placer.Windows[1].Rect);
		Assert.True(_placer.Moves[^1].Refresh);
		Assert.False(p.IsParked(1));
		Assert.Empty(_journal.Entries);
	}

	[Fact]
	public void Parking_does_not_ask_for_a_frame_refresh()
	{
		var p = Create();

		p.Park(1);

		Assert.False(_placer.Moves[0].Refresh);
	}

	[Fact]
	public void Parking_twice_moves_only_once()
	{
		var p = Create();

		p.Park(1);
		p.Park(1);

		Assert.Single(_placer.Moves);
		Assert.Single(_journal.Entries);
	}

	[Fact]
	public void A_minimized_window_is_not_parked()
	{
		var p = Create();
		_placer.Windows[1] = (100, Rect, true);

		Assert.False(p.Park(1));

		Assert.Empty(_placer.Moves);
		Assert.Empty(_journal.Entries);
	}

	[Fact]
	public void An_unknown_window_is_not_parked()
	{
		Assert.False(Create().Park(99));
	}

	[Fact]
	public void Without_a_free_place_the_window_is_not_parked()
	{
		var p = Create();
		_placer.MonitorList.Clear();

		Assert.False(p.Park(1));
	}

	[Fact]
	public void Owned_windows_follow_their_owner_in_both_directions()
	{
		var p = Create();
		var dialog = new Area(500, 300, 900, 500);
		_placer.Windows[2] = (100, dialog, false);
		_placer.Owned[1] = new List<long> { 2 };

		p.Park(1);
		var dx = _placer.Windows[1].Rect.Left - Rect.Left;
		Assert.Equal(new Area(dialog.Left + dx, dialog.Top, dialog.Right + dx, dialog.Bottom), _placer.Windows[2].Rect);
		Assert.Equal(2, _journal.Entries.Count);

		p.Unpark(1);
		Assert.Equal(Rect, _placer.Windows[1].Rect);
		Assert.Equal(dialog, _placer.Windows[2].Rect);
		Assert.Empty(_journal.Entries);
	}

	[Fact]
	public void RestoreAll_brings_every_window_back()
	{
		var p = Create();
		_placer.Windows[2] = (101, new Area(10, 10, 400, 300), false);
		p.Park(1);
		p.Park(2);

		var restored = p.RestoreAll();

		Assert.Equal(2, restored);
		Assert.Equal(Rect, _placer.Windows[1].Rect);
		Assert.Equal(new Area(10, 10, 400, 300), _placer.Windows[2].Rect);
		Assert.Equal(0, p.Count);
		Assert.Empty(_journal.Entries);
	}

	[Fact]
	public void Recover_brings_back_windows_a_killed_run_left_behind()
	{
		var first = Create();
		first.Park(1);
		var afterCrash = new WindowParker(_placer, _journal);

		var restored = afterCrash.Recover();

		Assert.Equal(1, restored);
		Assert.Equal(Rect, _placer.Windows[1].Rect);
		Assert.Empty(_journal.Entries);
	}

	[Fact]
	public void Recover_ignores_a_handle_that_belongs_to_another_window_now()
	{
		var first = Create();
		first.Park(1);
		// the window is gone and another process got the same handle
		_placer.Windows[1] = (555, new Area(50, 50, 400, 400), false);
		var afterCrash = new WindowParker(_placer, _journal);

		var restored = afterCrash.Recover();

		Assert.Equal(0, restored);
		Assert.Equal(new Area(50, 50, 400, 400), _placer.Windows[1].Rect);
		Assert.Empty(_journal.Entries);
	}

	[Fact]
	public void Recover_skips_windows_that_are_gone()
	{
		var first = Create();
		first.Park(1);
		_placer.Windows.Remove(1);

		Assert.Equal(0, new WindowParker(_placer, _journal).Recover());
		Assert.Empty(_journal.Entries);
	}

	[Fact]
	public void A_window_that_came_back_on_its_own_stays_where_it_is()
	{
		var p = Create();
		p.Park(1);
		var userPlaced = new Area(700, 100, 1500, 600);
		_placer.Windows[1] = (100, userPlaced, false);

		p.Unpark(1);

		Assert.Equal(userPlaced, _placer.Windows[1].Rect);
	}

	[Fact]
	public void Forget_drops_a_window_without_moving_anything()
	{
		var p = Create();
		p.Park(1);
		var moves = _placer.Moves.Count;

		p.Forget(1);

		Assert.False(p.IsParked(1));
		Assert.Equal(moves, _placer.Moves.Count);
		Assert.Empty(_journal.Entries);
	}

	[Fact]
	public void The_original_frame_is_remembered_for_the_animation()
	{
		var p = Create();

		p.Park(1);

		Assert.Equal(new Area(307, 200, 1093, 693), p.OriginalFrameOf(1));
		Assert.Equal(Rect, p.OriginalRectOf(1));
		Assert.Null(p.OriginalRectOf(99));
	}

	[Fact]
	public void Unpark_of_a_window_that_is_not_parked_does_nothing()
	{
		Assert.False(Create().Unpark(1));
		Assert.Empty(_placer.Moves);
	}

	[Fact]
	public void Parked_windows_survive_a_round_trip_through_the_journal_file()
	{
		var path = Path.Combine(Path.GetTempPath(), "StageManagerTests", Guid.NewGuid().ToString("N"), "parked.json");
		try
		{
			var journal = new FileParkingJournal(path);
			var entry = new ParkedWindow { Window = 42, ProcessId = 7, Owner = 3, Original = Rect, OriginalFrame = Rect, Parked = new Area(-960, 200, -160, 700) };

			journal.Save(new[] { entry });
			var loaded = journal.Load();

			Assert.Equal(entry, Assert.Single(loaded));

			journal.Save(Array.Empty<ParkedWindow>());
			Assert.Empty(journal.Load());
			Assert.False(File.Exists(path));
		}
		finally
		{
			var dir = Path.GetDirectoryName(path)!;
			if (Directory.Exists(dir))
				Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void A_broken_journal_file_counts_as_empty()
	{
		var dir = Path.Combine(Path.GetTempPath(), "StageManagerTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		var path = Path.Combine(dir, "parked.json");
		File.WriteAllText(path, "{ not json");

		try
		{
			Assert.Empty(new FileParkingJournal(path).Load());
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}
}
