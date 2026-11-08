namespace StageManager.Core;

/// <summary>What Windows says about animations at the moment.</summary>
public readonly record struct SystemAnimationState(bool AnimationsEnabled, bool DragFullWindows);

public enum FlightStyle
{
	/// <summary>No animation, switch right away.</summary>
	None,

	/// <summary>Thumbnails of the windows fly.</summary>
	Windows,

	/// <summary>A frame flies in place of the windows.</summary>
	Frames
}

/// <summary>Decides how a switch of scenes is animated. Pure logic, no Win32.</summary>
public static class AnimationPolicy
{
	public static FlightStyle Decide(AnimationSettings settings, SystemAnimationState system)
	{
		var animate = settings.Mode switch
		{
			AnimationMode.On => true,
			AnimationMode.Off => false,
			_ => system.AnimationsEnabled
		};

		if (!animate)
			return FlightStyle.None;

		// the same wish as for dragging windows: if Windows shows a frame only, the user does not want to see moving windows
		if (settings.FrameIfWindowContentIsOff && !system.DragFullWindows)
			return FlightStyle.Frames;

		return FlightStyle.Windows;
	}
}

/// <summary>Time and easing of a flight.</summary>
public static class FlightMotion
{
	/// <summary>Duration of a flight at normal speed.</summary>
	public static readonly TimeSpan BaseDuration = TimeSpan.FromMilliseconds(520);

	public static TimeSpan Duration(double speed)
	{
		var clamped = double.IsFinite(speed) ? Math.Clamp(speed, AnimationSettings.MinSpeed, AnimationSettings.MaxSpeed) : 1.0;
		return TimeSpan.FromMilliseconds(BaseDuration.TotalMilliseconds / clamped);
	}

	/// <summary>
	/// A critically damped spring that is launched towards its target and normalised to end on exactly 1:
	/// brisk at the start, most of the way early, settling without overshoot.
	/// </summary>
	public static double Ease(double progress)
	{
		const double stiffness = 7.5;
		const double launch = 0.6;

		static double Spring(double x) => 1 - (1 + (1 - launch) * x) * Math.Exp(-x);

		var p = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
		return Spring(stiffness * p) / Spring(stiffness);
	}

	public static double Lerp(double from, double to, double t) => from + (to - from) * t;
}

/// <summary>Where a window shows during a flight, in screen pixels.</summary>
public readonly record struct Pose(double X, double Y, double Width, double Height, double Opacity = 1)
{
	public static Pose Lerp(Pose from, Pose to, double t) => new(
		FlightMotion.Lerp(from.X, to.X, t),
		FlightMotion.Lerp(from.Y, to.Y, t),
		FlightMotion.Lerp(from.Width, to.Width, t),
		FlightMotion.Lerp(from.Height, to.Height, t),
		FlightMotion.Lerp(from.Opacity, to.Opacity, t));
}

/// <summary>
/// One window that flies from one pose to another. A flight that is turned around while it is in the air
/// continues from where it is, instead of jumping back to its start.
/// </summary>
public sealed class Flight
{
	private Pose _from;
	private Pose _to;
	private double _startedAt;
	private double _duration;

	public Flight(long window, Pose from, Pose to, TimeSpan duration, double now = 0)
	{
		Window = window;
		_from = from;
		_to = to;
		_duration = duration.TotalSeconds;
		_startedAt = now;
	}

	public long Window { get; }

	public Pose Target => _to;

	/// <param name="now">Seconds on any clock that only goes forward.</param>
	public double Progress(double now) => _duration <= 0 ? 1 : Math.Clamp((now - _startedAt) / _duration, 0, 1);

	public Pose At(double now) => Pose.Lerp(_from, _to, FlightMotion.Ease(Progress(now)));

	public bool IsDone(double now) => Progress(now) >= 1;

	/// <summary>Sends the flight to a new target, starting from where it is at <paramref name="now"/>.</summary>
	public void Redirect(Pose to, TimeSpan duration, double now)
	{
		_from = At(now);
		_to = to;
		_duration = duration.TotalSeconds;
		_startedAt = now;
	}
}
