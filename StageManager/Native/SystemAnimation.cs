using StageManager.Core;
using System.Runtime.InteropServices;

namespace StageManager.Native
{
	/// <summary>Reads what Windows says about animations.</summary>
	internal static class SystemAnimation
	{
		private const uint SPI_GETDRAGFULLWINDOWS = 0x0026;
		private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

		public static SystemAnimationState Read()
		{
			// if Windows does not answer, assume the usual: animations and window content while dragging are on
			var animations = !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out var a, 0) || a != 0;
			var dragFull = !SystemParametersInfo(SPI_GETDRAGFULLWINDOWS, 0, out var d, 0) || d != 0;
			return new SystemAnimationState(animations, dragFull);
		}

		public static FlightStyle CurrentStyle() => AnimationPolicy.Decide(App.Settings.Current.Animations, Read());
	}
}
