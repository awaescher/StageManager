using System.Text.Json;
using System.Text.Json.Serialization;

namespace StageManager.Core;

public enum SidebarSide
{
	Left,
	Right
}

public sealed class SidebarSettings
{
	public const int MinScenes = 1;
	public const int MaxScenesLimit = 12;
	public const int MinWindowsPerScene = 1;
	public const int MaxWindowsPerSceneLimit = 6;
	public const int MinWidth = 160;
	public const int MaxWidth = 480;
	public const int MinThumbnailEdge = 48;
	public const int MaxThumbnailEdge = 640;
	public const double MinAspect = 1.0;
	public const double MaxAspect = 2.5;
	public const double DefaultAspect = 1.6;

	public SidebarSide Side { get; set; } = SidebarSide.Left;

	/// <summary>Number of scenes (cards) shown in the sidebar at the same time.</summary>
	public int MaxScenes { get; set; } = 6;

	/// <summary>Number of windows of one scene that are shown as stacked thumbnails. All other windows only show up as icons.</summary>
	public int MaxWindowsPerScene { get; set; } = 3;

	/// <summary>Width of the sidebar in device independent units. 0 calculates it from the thumbnail size.</summary>
	public int Width { get; set; } = 0;

	/// <summary>Fixed thumbnail size in device independent units. Both values 0 calculates it from the number of scenes and the screen height.</summary>
	public int ThumbnailWidth { get; set; } = 0;

	/// <inheritdoc cref="ThumbnailWidth"/>
	public int ThumbnailHeight { get; set; } = 0;

	/// <summary>Width divided by height of calculated thumbnails.</summary>
	public double ThumbnailAspect { get; set; } = DefaultAspect;
}

public enum DesktopIconsMode
{
	/// <summary>Hidden while a scene is active, visible in the desktop view.</summary>
	Manage,

	/// <summary>Hidden as long as StageManager runs.</summary>
	AlwaysHidden,

	/// <summary>StageManager does not change the desktop icons. If it changed them before, the original state is restored.</summary>
	Untouched
}

public sealed class DesktopIconsSettings
{
	public DesktopIconsMode Mode { get; set; } = DesktopIconsMode.Manage;

	/// <summary>Brings the desktop icons back to the state they had before StageManager started, when it exits.</summary>
	public bool RestoreOnExit { get; set; } = true;
}

public sealed class AppSettings
{
	public const int CurrentVersion = 1;

	internal static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter() }
	};

	public int Version { get; set; } = CurrentVersion;

	public SidebarSettings Sidebar { get; set; } = new();

	public DesktopIconsSettings DesktopIcons { get; set; } = new();

	/// <summary>
	/// Replaces missing or out-of-range values with usable ones, so that a hand-edited file can never break the app.
	/// </summary>
	public AppSettings Normalize()
	{
		Version = CurrentVersion;
		Sidebar ??= new SidebarSettings();
		DesktopIcons ??= new DesktopIconsSettings();

		if (!Enum.IsDefined(DesktopIcons.Mode))
			DesktopIcons.Mode = DesktopIconsMode.Manage;

		if (!Enum.IsDefined(Sidebar.Side))
			Sidebar.Side = SidebarSide.Left;

		Sidebar.MaxScenes = Math.Clamp(Sidebar.MaxScenes, SidebarSettings.MinScenes, SidebarSettings.MaxScenesLimit);
		Sidebar.MaxWindowsPerScene = Math.Clamp(Sidebar.MaxWindowsPerScene, SidebarSettings.MinWindowsPerScene, SidebarSettings.MaxWindowsPerSceneLimit);

		if (Sidebar.Width != 0)
			Sidebar.Width = Math.Clamp(Sidebar.Width, SidebarSettings.MinWidth, SidebarSettings.MaxWidth);

		// a thumbnail size needs both edges, otherwise it is calculated
		if (Sidebar.ThumbnailWidth <= 0 || Sidebar.ThumbnailHeight <= 0)
		{
			Sidebar.ThumbnailWidth = 0;
			Sidebar.ThumbnailHeight = 0;
		}
		else
		{
			Sidebar.ThumbnailWidth = Math.Clamp(Sidebar.ThumbnailWidth, SidebarSettings.MinThumbnailEdge, SidebarSettings.MaxThumbnailEdge);
			Sidebar.ThumbnailHeight = Math.Clamp(Sidebar.ThumbnailHeight, SidebarSettings.MinThumbnailEdge, SidebarSettings.MaxThumbnailEdge);
		}

		Sidebar.ThumbnailAspect = double.IsFinite(Sidebar.ThumbnailAspect)
			? Math.Clamp(Sidebar.ThumbnailAspect, SidebarSettings.MinAspect, SidebarSettings.MaxAspect)
			: SidebarSettings.DefaultAspect;

		return this;
	}

	public AppSettings Clone() => Deserialize(Serialize())!;

	public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

	public static AppSettings? Deserialize(string json) => JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
}
