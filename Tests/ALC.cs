using System;
using System.Runtime.InteropServices;

namespace Tests
{
	//This class contains only the most necessary functions & values that were used in the test.
	public static class ALC
	{
		public const string Library = AL.Library;

		static ALC() => DllManager.PrepareResolver();

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alcCreateContext")]
		public static extern IntPtr CreateContext(IntPtr device, int[] attrList);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alcMakeContextCurrent")]
		public static extern bool MakeContextCurrent(IntPtr context);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alcDestroyContext")]
		public static extern void DestroyContext(IntPtr context);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alcOpenDevice")]
		public static extern IntPtr OpenDevice([In()][MarshalAs(UnmanagedType.LPStr)] string deviceName);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alcCloseDevice")]
		public static extern bool CloseDevice(IntPtr device);
	}
}
