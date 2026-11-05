namespace StageManager.Core;

public sealed record SidebarLayoutInput(
	double AreaHeight,
	int MaxScenes,
	double Aspect,
	double FixedSidebarWidth,
	double FixedThumbnailWidth,
	double FixedThumbnailHeight);

public sealed record SidebarLayoutResult(
	double ThumbnailWidth,
	double ThumbnailHeight,
	double SidebarWidth,
	int EffectiveMaxScenes,
	bool IsLimited);

/// <summary>
/// Calculates how large the thumbnails can be so that the wanted number of scenes fits into the height of the screen.
/// All values are device independent units. The constants have to match the sidebar template in MainWindow.xaml.
/// </summary>
public static class SidebarLayout
{
	/// <summary>Space above the first and below the last card.</summary>
	public const double VerticalMargin = 24;

	/// <summary>Icon row below a thumbnail: 4 margin + 30 icon height.</summary>
	public const double IconRowHeight = 34;

	/// <summary>Space below every card.</summary>
	public const double CardGap = 24;

	/// <summary>Space between the screen edge and the thumbnails.</summary>
	public const double EdgePadding = 12;

	/// <summary>Space between the thumbnails and the inner edge of the sidebar, leaves room for the shadow.</summary>
	public const double ShadowRoom = 24;

	public const double MinThumbnailWidth = 96;
	public const double MinThumbnailHeight = 60;
	public const double MaxThumbnailWidth = 320;
	public const double MaxThumbnailHeight = 200;

	public static SidebarLayoutResult Calculate(SidebarSettings sidebar, double areaHeight) =>
		Calculate(new SidebarLayoutInput(
			areaHeight,
			sidebar.MaxScenes,
			sidebar.ThumbnailAspect,
			sidebar.Width,
			sidebar.ThumbnailWidth,
			sidebar.ThumbnailHeight));

	public static SidebarLayoutResult Calculate(SidebarLayoutInput input)
	{
		var requested = Math.Clamp(input.MaxScenes, SidebarSettings.MinScenes, SidebarSettings.MaxScenesLimit);
		var aspect = double.IsFinite(input.Aspect)
			? Math.Clamp(input.Aspect, SidebarSettings.MinAspect, SidebarSettings.MaxAspect)
			: SidebarSettings.DefaultAspect;
		var available = Math.Max(0, input.AreaHeight - 2 * VerticalMargin);
		var chrome = IconRowHeight + CardGap;

		double width;
		double height;

		if (input.FixedThumbnailWidth > 0 && input.FixedThumbnailHeight > 0)
		{
			width = input.FixedThumbnailWidth;
			height = input.FixedThumbnailHeight;
		}
		else
		{
			height = Math.Clamp(available / requested - chrome, MinThumbnailHeight, MaxThumbnailHeight);
			width = height * aspect;

			if (width > MaxThumbnailWidth)
			{
				width = MaxThumbnailWidth;
				height = width / aspect;
			}
		}

		// a fixed sidebar width wins over the thumbnail size, the thumbnail shrinks and keeps its proportions
		if (input.FixedSidebarWidth > 0)
		{
			var room = input.FixedSidebarWidth - EdgePadding - ShadowRoom;
			if (room > 0 && width > room)
			{
				height *= room / width;
				width = room;
			}
		}

		var fitting = (int)Math.Floor(available / (height + chrome));
		var effective = Math.Clamp(fitting, 1, requested);

		var sidebarWidth = input.FixedSidebarWidth > 0
			? input.FixedSidebarWidth
			: Math.Clamp(width + EdgePadding + ShadowRoom, SidebarSettings.MinWidth, SidebarSettings.MaxWidth);

		return new SidebarLayoutResult(width, height, sidebarWidth, effective, effective < requested);
	}
}
