using StageManager.Core;

namespace StageManager.Tests;

public class DesktopViewTests
{
	[Theory]
	[InlineData("Progman")]
	[InlineData("progman")]
	[InlineData("WorkerW")]
	[InlineData("WORKERW")]
	public void The_windows_of_the_desktop_are_recognized(string className)
	{
		Assert.True(DesktopView.IsDesktopClass(className));
	}

	[Theory]
	[InlineData("Shell_TrayWnd")]
	[InlineData("Shell_SecondaryTrayWnd")]
	[InlineData("SHELLDLL_DefView")]
	[InlineData("Notepad")]
	[InlineData("")]
	[InlineData(null)]
	public void Other_windows_are_not_the_desktop(string? className)
	{
		Assert.False(DesktopView.IsDesktopClass(className));
	}
}
