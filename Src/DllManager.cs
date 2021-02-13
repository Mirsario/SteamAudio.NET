using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace SteamAudio
{
	internal static partial class DllManager
	{
		private static bool resolverReady;

		static DllManager() => PrepareResolver();

		//This method implements a dllmap resolver for native libraries. It expects 'AssemblyName.dll.config' to be present next to the managed library's dll.
		internal static void PrepareResolver()
		{
			if(resolverReady) {
				return;
			}

			string osString = OSUtils.GetOS().ToString().ToLower();
			string cpuString = RuntimeInformation.OSArchitecture switch {
				Architecture.Arm => "arm",
				Architecture.Arm64 => "armv8",
				Architecture.X86 => "x86",
				_ => "x86-64",
			};
			string wordSizeString = RuntimeInformation.OSArchitecture switch {
				Architecture.X86 => "32",
				Architecture.Arm => "32",
				_ => "64",
			};
			var stringComparer = StringComparer.InvariantCultureIgnoreCase;

			bool StringNullOrEqual(string a, string b)
				=> a == null || stringComparer.Equals(a, b);

			Assembly wrapperAssembly = Assembly.GetExecutingAssembly();

			NativeLibrary.SetDllImportResolver(wrapperAssembly, (name, assembly, path) => {
				string configPath = wrapperAssembly.Location + ".config";

				if(!File.Exists(configPath)) {
					return IntPtr.Zero;
				}

				XElement root = XElement.Load(configPath);

				var maps = root
					.Elements("dllmap")
					.Where(element => stringComparer.Equals(element.Attribute("dll")?.Value, name))
					.Where(element => StringNullOrEqual(element.Attribute("os")?.Value, osString))
					.Where(element => StringNullOrEqual(element.Attribute("cpu")?.Value, cpuString))
					.Where(element => StringNullOrEqual(element.Attribute("wordsize")?.Value, wordSizeString));

				var map = maps.SingleOrDefault();

				if(map == null) {
					throw new ArgumentException($"'{Path.GetFileName(configPath)}' - Found {maps.Count()} possible mapping candidates for dll '{name}'.");
				}

				return NativeLibrary.Load(map.Attribute("target").Value);
			});

			resolverReady = true;
		}
	}
}
