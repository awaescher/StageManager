using AsyncAwaitBestPractices;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace StageManager
{
	/// <summary>
	/// Windows from different scenes that are arranged together with the Windows snap feature
	/// (edge snapping or Snap Assist) are combined into a snap group scene.
	/// The exact layout of a snap group is restored when switching back to it.
	/// </summary>
	public partial class SceneManager
	{
		/// <summary>
		/// Maximum time to wait for a window selected in Snap Assist to be arranged.
		/// </summary>
		private const int SNAP_JOIN_TIMEOUT_MS = 500;
		private const int SNAP_JOIN_POLL_MS = 100;
		private const int SNAP_CHECK_DEBOUNCE_MS = 300;

		/// <summary>
		/// Maximum deviation in pixels before a window of a snap group gets repositioned.
		/// </summary>
		private const int LAYOUT_TOLERANCE = 2;

		private int _foregroundVersion;
		private DispatcherTimer _snapCheckTimer;
		private IntPtr _snapMoveInProgress = IntPtr.Zero;
		private readonly Dictionary<IntPtr, Rectangle> _moveStartRects = new Dictionary<IntPtr, Rectangle>();
		private readonly HashSet<IntPtr> _movedByUser = new HashSet<IntPtr>();

		private DispatcherTimer _pendingJoinTimer;
		private readonly Dictionary<IntPtr, IWindow> _pendingJoins = new Dictionary<IntPtr, IWindow>();

		private void InitializeSnapGroups()
		{
			_snapCheckTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
			{
				Interval = TimeSpan.FromMilliseconds(SNAP_CHECK_DEBOUNCE_MS)
			};
			_snapCheckTimer.Tick += (_, _) => CheckSnapGroup().SafeFireAndForget();

			_pendingJoinTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
			{
				Interval = TimeSpan.FromMilliseconds(SNAP_CHECK_DEBOUNCE_MS)
			};
			_pendingJoinTimer.Tick += (_, _) => JoinPendingWindows().SafeFireAndForget();
		}

		private static bool IsOnScreen(IWindow window) => Win32.IsWindowVisible(window.Handle) && !Win32.IsIconic(window.Handle);

		private static Rectangle GetFrame(IWindow window) => SnapHelper.GetFrameRect(window.Handle);

		#region Joining

		/// <summary>
		/// A window got the foreground. Usually this switches to its scene. But if the current scene contains snapped
		/// windows, the window might have been selected in Snap Assist. In this case the switch is delayed until
		/// it is clear whether the window was snapped next to the current windows.
		/// </summary>
		private void HandleForeground(IWindow window)
		{
			var version = ++_foregroundVersion;

			if (!MightJoinSnapGroup(window, out var reason))
			{
				DebugLog.Write("JOIN", $"v{version} {DebugLog.Describe(window.Handle)} no join candidate ({reason}), switching directly");
				SwitchToSceneByWindow(window).SafeFireAndForget();
				return;
			}

			DebugLog.Write("JOIN", $"v{version} {DebugLog.Describe(window.Handle)} join candidate, waiting for arrangement");

			// run inside a dispatcher operation so that all continuations return to the UI thread
			_dispatcher.InvokeAsync(() => JoinOrSwitchAsync(window, version)).Task.Unwrap().SafeFireAndForget();
		}

		private bool MightJoinSnapGroup(IWindow window, out string reason)
		{
			reason = "";

			if (_scenes is null || _current is null)
			{
				reason = "no current scene";
				return false;
			}

			if (_current.Contains(window))
			{
				reason = "already in current scene";
				return false;
			}

			// windows of another snap group are candidates as well, the groups get merged
			if (!_current.Windows.Any(w => IsOnScreen(w) && SnapHelper.IsArranged(w.Handle)))
			{
				reason = "current scene has no arranged windows";
				return false;
			}

			return true;
		}

		private async Task JoinOrSwitchAsync(IWindow window, int version)
		{
			var waited = 0;
			while (waited < SNAP_JOIN_TIMEOUT_MS && !SnapHelper.IsArranged(window.Handle))
			{
				await Task.Delay(SNAP_JOIN_POLL_MS);
				waited += SNAP_JOIN_POLL_MS;
				DebugLog.Write("JOIN", $"v{version} poll {waited} ms {DebugLog.Describe(window.Handle)} {DebugLog.State(window.Handle)}");

				if (version != _foregroundVersion)
				{
					DebugLog.Write("JOIN", $"v{version} cancelled, newer foreground event v{_foregroundVersion}");
					return;
				}
			}

			if (SnapHelper.IsArranged(window.Handle))
			{
				// give Windows the chance to finish positioning the window
				await Task.Delay(SNAP_JOIN_POLL_MS);

				if (version != _foregroundVersion)
				{
					DebugLog.Write("JOIN", $"v{version} cancelled after arrangement, newer foreground event v{_foregroundVersion}");
					return;
				}

				if (await TryJoinSnapGroup(window))
					return;
			}
			else
			{
				DebugLog.Write("JOIN", $"v{version} timeout, window not arranged");
			}

			DebugLog.Write("JOIN", $"v{version} fallback to normal switch");
			await SwitchToSceneByWindow(window);
		}

		/// <summary>
		/// Snap Assist keeps the focus until the last zone is filled, so windows placed in the zones before
		/// do not get a foreground event. They are detected by being restored or moved into an arranged position
		/// while the current scene contains arranged windows.
		/// </summary>
		private void HandlePossibleSnapAssistPlacement(IWindow window)
		{
			if (_scenes is null || _current is null || _current.Contains(window) || _pendingJoins.ContainsKey(window.Handle))
				return;

			if (!IsOnScreen(window) || !SnapHelper.IsArranged(window.Handle))
				return;

			if (!MightJoinSnapGroup(window, out _))
				return;

			DebugLog.Write("PENDING", $"arranged without foreground {DebugLog.Describe(window.Handle)} {DebugLog.State(window.Handle)}");
			_pendingJoins[window.Handle] = window;

			_pendingJoinTimer.Stop();
			_pendingJoinTimer.Start();
		}

		private async Task JoinPendingWindows()
		{
			_pendingJoinTimer.Stop();

			var pending = _pendingJoins.Values.ToArray();
			_pendingJoins.Clear();

			foreach (var window in pending)
			{
				// might have joined already together with another window
				if (_current is null || _current.Contains(window))
					continue;

				DebugLog.Write("PENDING", $"trying to join {DebugLog.Describe(window.Handle)}");

				// no fallback switch here, the window did not get the focus
				await TryJoinSnapGroup(window);
			}
		}

		/// <summary>
		/// Adds a window to the snap group of the current scene if it was snapped next to the snapped windows
		/// of the current scene. Creates a new snap group if the current scene is a process scene.
		/// </summary>
		private async Task<bool> TryJoinSnapGroup(IWindow window)
		{
			if (_scenes is null || _current is null || _current.Contains(window))
				return false;

			if (!IsOnScreen(window) || !SnapHelper.IsArranged(window.Handle))
				return JoinFailed(window, "not on screen or not arranged");

			// a window of another snap group brings its group along, the groups are merged
			var windowGroup = FindSceneForWindow(window) is { IsSnapGroup: true } g ? g : null;

			// only one monitor is supported for now, but groups never span multiple monitors
			var monitor = SnapHelper.GetMonitor(window.Handle);
			if (_current.IsSnapGroup && _current.Monitor != monitor)
				return JoinFailed(window, "different monitor");

			if (windowGroup is object && windowGroup.Monitor != monitor)
				return JoinFailed(window, "snap group of the window is on a different monitor");

			var anchors = _current.Windows
				.Where(w => IsOnScreen(w) && SnapHelper.IsArranged(w.Handle) && SnapHelper.GetMonitor(w.Handle) == monitor)
				.ToList();

			var frame = GetFrame(window);
			DebugLog.Write("JOIN", $"try {DebugLog.Describe(window.Handle)} frame={DebugLog.Rect(frame)} anchors: {string.Join("; ", anchors.Select(a => DebugLog.Describe(a.Handle) + " frame=" + DebugLog.Rect(GetFrame(a))))}");

			if (!anchors.Any())
				return JoinFailed(window, "no arranged anchors in current scene");

			if (anchors.Any(a => SnapHelper.OverlapsSignificantly(frame, GetFrame(a))))
				return JoinFailed(window, "overlaps an anchor");

			// other snapped windows next to the layout join as well, even without a foreground event
			// (Snap Assist only activates the window chosen for the last zone)
			var others = GetSceneableWindows()
				.Where(w => w.Handle != window.Handle && !_current.Contains(w))
				.Where(w => FindSceneForWindow(w) is not { IsSnapGroup: true })
				.Where(w => IsOnScreen(w) && SnapHelper.IsArranged(w.Handle) && SnapHelper.GetMonitor(w.Handle) == monitor)
				.Where(w =>
				{
					var otherFrame = GetFrame(w);
					return !SnapHelper.OverlapsSignificantly(otherFrame, frame)
						&& !anchors.Any(a => SnapHelper.OverlapsSignificantly(otherFrame, GetFrame(a)));
				})
				.ToList();

			var component = SnapHelper.GetConnected(window, anchors.Concat(others).Append(window), GetFrame);
			var connectedAnchors = component.Where(c => anchors.Any(a => a.Handle == c.Handle)).ToList();

			if (!connectedAnchors.Any())
				return JoinFailed(window, "not adjacent to any anchor");

			var joining = component.Where(c => !anchors.Any(a => a.Handle == c.Handle)).ToList();

			if (windowGroup is object)
				return await MergeIntoSnapGroup(windowGroup, window, connectedAnchors, joining);

			Scene group;

			try
			{
				_suspend = true;

				foreach (var joiningWindow in joining)
				{
					if (FindSceneForWindow(joiningWindow) is Scene joiningSource)
						RemoveWindowFromScene(joiningSource, joiningWindow);
				}

				if (_current.IsSnapGroup)
				{
					DebugLog.Write("JOIN", $"added {string.Join(", ", joining.Select(j => DebugLog.Describe(j.Handle)))} to existing group");
					foreach (var joiningWindow in joining)
						AddWindowToScene(_current, joiningWindow);

					_current.CaptureLayout();
					return true;
				}

				var processScene = _current;
				foreach (var anchor in connectedAnchors)
					RemoveWindowFromScene(processScene, anchor);

				group = Scene.CreateSnapGroup(monitor, connectedAnchors.Concat(joining));
				DebugLog.Write("JOIN", $"created group with {string.Join(", ", joining.Select(j => DebugLog.Describe(j.Handle)))} and {string.Join(", ", connectedAnchors.Select(a => DebugLog.Describe(a.Handle)))}");
				_scenes.Add(group);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(group, window, ChangeType.Created));
				group.CaptureLayout();
			}
			finally
			{
				_suspend = false;
			}

			// the group windows are already visible, remaining windows of the former scene get minimized
			await SwitchTo(group);
			return true;
		}

		/// <summary>
		/// A window of an existing snap group was snapped next to the snapped windows of the current scene.
		/// The existing group is extended by these windows. Windows of the existing group whose stored
		/// position overlaps the new layout go back to their process scene.
		/// </summary>
		private async Task<bool> MergeIntoSnapGroup(Scene target, IWindow window, List<IWindow> connectedAnchors, List<IWindow> joining)
		{
			var newLayout = connectedAnchors.Concat(joining).ToList();
			var newFrames = newLayout.Select(GetFrame).ToList();

			var overlapping = target.Windows
				.Where(m => !newLayout.Any(n => n.Handle == m.Handle))
				.Where(m =>
				{
					var rect = IsOnScreen(m) || !target.Layout.TryGetValue(m.Handle, out var stored)
						? SnapHelper.GetWindowRect(m.Handle)
						: stored;

					return newFrames.Any(f => LayoutOverlaps(rect, f));
				})
				.ToList();

			var source = _current;

			try
			{
				_suspend = true;

				foreach (var member in overlapping)
				{
					DebugLog.Write("JOIN", $"merge: {DebugLog.Describe(member.Handle)} overlaps the new layout, back to its process scene");
					RemoveWindowFromScene(target, member);
					AddWindowToHomeScene(member);
				}

				foreach (var newWindow in newLayout)
				{
					var newWindowScene = FindSceneForWindow(newWindow);
					if (target.Equals(newWindowScene))
						continue;

					if (newWindowScene is object)
						RemoveWindowFromScene(newWindowScene, newWindow);

					AddWindowToScene(target, newWindow);
				}

				DebugLog.Write("JOIN", $"merged into group of {DebugLog.Describe(window.Handle)}: {DescribeScene(target)}");

				if (source is { IsSnapGroup: true } && !source.Equals(target))
					DissolveSnapGroupIfTooSmall(source, switchToRemainingWindow: false);

				target.CaptureLayout();
			}
			finally
			{
				_suspend = false;
			}

			// minimized windows that stayed in the group get restored, other windows get minimized
			await SwitchTo(target);
			return true;
		}

		/// <summary>
		/// Compares a window rectangle (including invisible borders) with a visible frame. Both are shrunk a little,
		/// so that adjacent windows do not count as overlapping.
		/// </summary>
		private static bool LayoutOverlaps(Rectangle windowRect, Rectangle frame)
		{
			const int shrink = 8;
			var a = Rectangle.Inflate(windowRect, -shrink, -shrink);
			var b = Rectangle.Inflate(frame, -shrink, -shrink);
			return SnapHelper.OverlapsSignificantly(a, b);
		}

		private static bool JoinFailed(IWindow window, string reason)
		{
			DebugLog.Write("JOIN", $"failed {DebugLog.Describe(window.Handle)}: {reason}");
			return false;
		}

		#endregion

		#region Layout changes

		private bool IsCurrentSnapGroupWindow(IWindow window) => _current is { IsSnapGroup: true } && _current.Contains(window);

		private void HandleSnapGroupMoveStart(IWindow window)
		{
			if (!IsCurrentSnapGroupWindow(window))
				return;

			_snapMoveInProgress = window.Handle;

			// the stored layout is the preferred reference, the move start event is delivered asynchronously
			// and the window might already have changed when it is handled
			var reference = _current.Layout.TryGetValue(window.Handle, out var stored) ? stored : SnapHelper.GetWindowRect(window.Handle);
			_moveStartRects[window.Handle] = reference;

			DebugLog.Write("CHECK", $"move start {DebugLog.Describe(window.Handle)} reference={DebugLog.Rect(reference)} {DebugLog.State(window.Handle)}");
		}

		/// <summary>
		/// Distinguishes dragging a window from resizing it. Resizing always keeps at least one edge in place
		/// (three edges when dragging a border, two when dragging a corner), dragging moves all four edges.
		/// </summary>
		private static bool WasDragged(Rectangle before, Rectangle after)
		{
			return Math.Abs(before.Left - after.Left) > LAYOUT_TOLERANCE
				&& Math.Abs(before.Top - after.Top) > LAYOUT_TOLERANCE
				&& Math.Abs(before.Right - after.Right) > LAYOUT_TOLERANCE
				&& Math.Abs(before.Bottom - after.Bottom) > LAYOUT_TOLERANCE;
		}

		private void HandleSnapGroupLocationChanged(IWindow window, bool isMoveEnd)
		{
			if (!IsCurrentSnapGroupWindow(window))
				return;

			if (isMoveEnd)
			{
				_movedByUser.Add(window.Handle);

				if (_snapMoveInProgress == window.Handle)
					_snapMoveInProgress = IntPtr.Zero;
			}

			_snapCheckTimer.Stop();
			_snapCheckTimer.Start();
		}

		/// <summary>
		/// Checks whether windows were taken out of the current snap group by maximizing or dragging them.
		/// Resizing never changes the group. Windows that were taken out go back to their process scene,
		/// which becomes the current scene.
		/// </summary>
		private async Task CheckSnapGroup()
		{
			_snapCheckTimer.Stop();

			// wait for the end of the user interaction, the move end event restarts the check
			if (_snapMoveInProgress != IntPtr.Zero)
				return;

			var group = _current;

			if (group is not { IsSnapGroup: true } || _scenes is null || !_scenes.Contains(group))
			{
				ResetSnapMoveTracking();
				return;
			}

			var members = group.Windows.ToArray();
			var leaving = new List<IWindow>();

			DebugLog.Write("CHECK", $"checking group, movedByUser={_movedByUser.Count}");

			// resizing never takes a window out of the group, only maximizing or dragging it does
			foreach (var window in members)
			{
				var dragged = _movedByUser.Contains(window.Handle)
								&& _moveStartRects.TryGetValue(window.Handle, out var before)
								&& WasDragged(before, SnapHelper.GetWindowRect(window.Handle));

				DebugLog.Write("CHECK", $"  {DebugLog.Describe(window.Handle)} moved={_movedByUser.Contains(window.Handle)} dragged={dragged} {DebugLog.State(window.Handle)}");

				if (Win32.IsZoomed(window.Handle) || dragged)
					leaving.Add(window);
			}

			var movedWindows = _movedByUser.ToHashSet();
			ResetSnapMoveTracking();

			if (!leaving.Any())
			{
				DebugLog.Write("CHECK", "group unchanged, layout captured");
				group.CaptureLayout();
				return;
			}

			DebugLog.Write("CHECK", $"leaving: {string.Join(", ", leaving.Select(w => DebugLog.Describe(w.Handle)))}");

			try
			{
				_suspend = true;

				foreach (var window in leaving)
				{
					RemoveWindowFromScene(group, window);
					AddWindowToHomeScene(window);
				}

				if (!DissolveSnapGroupIfTooSmall(group, switchToRemainingWindow: false))
					group.CaptureLayout();
			}
			finally
			{
				_suspend = false;
			}

			// the window taken out of the layout stays visible, its process scene becomes the current scene
			var focus = leaving.FirstOrDefault(w => movedWindows.Contains(w.Handle))
						?? leaving.FirstOrDefault(w => w.IsFocused)
						?? leaving.First();

			await SwitchToSceneByWindow(focus);
		}

		private void ResetSnapMoveTracking()
		{
			_movedByUser.Clear();
			_moveStartRects.Clear();
		}

		/// <summary>
		/// Dissolves a snap group with less than two windows. The remaining window goes back to its process scene.
		/// </summary>
		/// <returns>True if the group was dissolved</returns>
		private bool DissolveSnapGroupIfTooSmall(Scene group, bool switchToRemainingWindow)
		{
			if (!group.IsSnapGroup || group.Windows.Count() >= 2)
				return false;

			var wasCurrent = group.Equals(_current);
			Scene target = null;
			DebugLog.Write("DISSOLVE", $"group with {group.Windows.Count()} window(s), wasCurrent={wasCurrent}");

			foreach (var window in group.Windows.ToArray())
			{
				RemoveWindowFromScene(group, window);
				target = AddWindowToHomeScene(window);
			}

			if (switchToRemainingWindow && wasCurrent && target is object)
				SwitchTo(target).SafeFireAndForget();

			return true;
		}

		#endregion

		#region Restoring

		/// <summary>
		/// Corrects the position and size of snap group windows that were restored from minimized state.
		/// Windows keeps the snap state over minimize and restore, and moving a snapped window would break
		/// the native Windows snap group (shared resize bar). So only windows that were restored without
		/// their snap state and differ from the stored layout are moved.
		/// </summary>
		private void RestoreSnapLayout(Scene group, ISet<IntPtr> restoredFromMinimized)
		{
			foreach (var window in group.Windows)
			{
				DebugLog.Write("RESTORE", $"shown {DebugLog.Describe(window.Handle)} wasMinimized={restoredFromMinimized.Contains(window.Handle)} {DebugLog.State(window.Handle)}");

				if (!restoredFromMinimized.Contains(window.Handle))
					continue;

				if (!group.Layout.TryGetValue(window.Handle, out var target))
					continue;

				if (!IsOnScreen(window) || SnapHelper.IsArranged(window.Handle))
					continue;

				if (IsSameRect(target, SnapHelper.GetWindowRect(window.Handle)))
					continue;

				DebugLog.Write("RESTORE", $"{DebugLog.Describe(window.Handle)} before: {DebugLog.State(window.Handle)} target={DebugLog.Rect(target)}");
				Win32.SetWindowPos(window.Handle, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
					Win32.SetWindowPosFlags.IgnoreZOrder | Win32.SetWindowPosFlags.DoNotActivate);
				DebugLog.Write("RESTORE", $"{DebugLog.Describe(window.Handle)} after: {DebugLog.State(window.Handle)}");
			}
		}

		private static bool IsSameRect(Rectangle a, Rectangle b)
		{
			return Math.Abs(a.Left - b.Left) <= LAYOUT_TOLERANCE
				&& Math.Abs(a.Top - b.Top) <= LAYOUT_TOLERANCE
				&& Math.Abs(a.Right - b.Right) <= LAYOUT_TOLERANCE
				&& Math.Abs(a.Bottom - b.Bottom) <= LAYOUT_TOLERANCE;
		}

		#endregion
	}
}
