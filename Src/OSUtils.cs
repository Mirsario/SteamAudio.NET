using System.Runtime.InteropServices;

namespace SteamAudio
{
	internal static class OSUtils
	{
		public enum OS
		{
			Windows,
			Linux,
			OSX,
			FreeBSD
		}

		public static bool IsOS(OS os) => os switch {
			OS.Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
			OS.Linux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
			OS.OSX => RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
			OS.FreeBSD => RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD),
			_ => false
		};

		public static OS GetOS()
		{
			if(IsOS(OS.Linux)) {
				return OS.Linux;
			}

			if(IsOS(OS.OSX)) {
				return OS.OSX;
			}

			if(IsOS(OS.FreeBSD)) {
				return OS.FreeBSD;
			}

			return OS.Windows;
		}
	}
}
