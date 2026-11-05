using StageManager.Core;

namespace StageManager.Tests;

public class SidebarGeometryTests
{
	private static readonly Area Primary = new(0, 0, 1920, 1040);
	private static readonly Area LeftOfPrimary = new(-1920, 0, 0, 1040);

	[Fact]
	public void Left_side_shows_at_work_area_left_and_hides_to_the_left()
	{
		var g = new SidebarGeometry(Primary, 280, SidebarSide.Left);

		Assert.Equal(0, g.OnScreenLeft);
		Assert.Equal(-280, g.OffScreenLeft);
		Assert.Equal(g.OffScreenLeft, g.GetLeft(false));
	}

	[Fact]
	public void Right_side_shows_at_work_area_right_and_hides_to_the_right()
	{
		var g = new SidebarGeometry(Primary, 280, SidebarSide.Right);

		Assert.Equal(1640, g.OnScreenLeft);
		Assert.Equal(1920, g.OffScreenLeft);
		Assert.Equal(g.OnScreenLeft, g.GetLeft(true));
	}

	[Fact]
	public void Monitor_with_negative_origin_is_handled()
	{
		var left = new SidebarGeometry(LeftOfPrimary, 280, SidebarSide.Left);
		var right = new SidebarGeometry(LeftOfPrimary, 280, SidebarSide.Right);

		Assert.Equal(-1920, left.OnScreenLeft);
		Assert.Equal(-2200, left.OffScreenLeft);
		Assert.Equal(-280, right.OnScreenLeft);
		Assert.Equal(0, right.OffScreenLeft);
	}

	[Theory]
	[InlineData(SidebarSide.Left, 0, true)]
	[InlineData(SidebarSide.Left, 44, true)]
	[InlineData(SidebarSide.Left, 45, false)]
	[InlineData(SidebarSide.Left, 1919, false)]
	[InlineData(SidebarSide.Right, 1919, true)]
	[InlineData(SidebarSide.Right, 1876, true)]
	[InlineData(SidebarSide.Right, 1875, false)]
	[InlineData(SidebarSide.Right, 0, false)]
	public void Trigger_zone_is_at_the_sidebar_edge(SidebarSide side, double mouseX, bool expected)
	{
		var g = new SidebarGeometry(Primary, 280, side);

		Assert.Equal(expected, g.IsInTriggerZone(mouseX));
	}

	[Fact]
	public void Trigger_zone_does_not_react_on_the_neighbour_monitor()
	{
		var g = new SidebarGeometry(Primary, 280, SidebarSide.Left);

		Assert.False(g.IsInTriggerZone(-10));
	}

	[Theory]
	[InlineData(SidebarSide.Left, 279, true)]
	[InlineData(SidebarSide.Left, 281, false)]
	[InlineData(SidebarSide.Right, 1641, true)]
	[InlineData(SidebarSide.Right, 1639, false)]
	public void Mouse_over_sidebar(SidebarSide side, double mouseX, bool expected)
	{
		var g = new SidebarGeometry(Primary, 280, side);

		Assert.Equal(expected, g.IsOverSidebar(mouseX));
	}

	[Theory]
	[InlineData(SidebarSide.Left, 281, true)]
	[InlineData(SidebarSide.Left, 100, false)]
	[InlineData(SidebarSide.Right, 1639, true)]
	[InlineData(SidebarSide.Right, 1800, false)]
	public void Pulled_out_of_sidebar(SidebarSide side, double mouseX, bool expected)
	{
		var g = new SidebarGeometry(Primary, 280, side);

		Assert.Equal(expected, g.IsPulledOutOfSidebar(mouseX));
	}

	[Theory]
	[InlineData(SidebarSide.Left, 0, 800, true)]
	[InlineData(SidebarSide.Left, 139, 800, true)]
	[InlineData(SidebarSide.Left, 140, 800, false)]
	[InlineData(SidebarSide.Left, 500, 800, false)]
	[InlineData(SidebarSide.Right, 1120, 800, true)]
	[InlineData(SidebarSide.Right, 1000, 800, true)]
	[InlineData(SidebarSide.Right, 700, 800, false)]
	public void Normal_window_overlap_is_mirrored(SidebarSide side, double windowLeft, double windowWidth, bool expected)
	{
		var g = new SidebarGeometry(Primary, 280, side);

		Assert.Equal(expected, g.DoesNormalWindowOverlap(windowLeft, windowWidth));
	}

	[Fact]
	public void Overlap_uses_the_monitor_origin()
	{
		var g = new SidebarGeometry(LeftOfPrimary, 280, SidebarSide.Left);

		Assert.True(g.DoesNormalWindowOverlap(-1900, 800));
		Assert.False(g.DoesNormalWindowOverlap(-1500, 800));
	}
}
