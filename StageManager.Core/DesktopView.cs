namespace StageManager.Core;

/// <summary>Knows which windows are the desktop itself, the wallpaper with its icons.</summary>
public static class DesktopView
{
	/// <summary>
	/// True for the class of a window that has the focus after a click on the wallpaper. Which one it is depends on Windows: the shell
	/// window (Progman) on current versions, a WorkerW after a wallpaper slideshow or "Show desktop".
	/// </summary>
	public static bool IsDesktopClass(string? className) =>
		string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase);
}
