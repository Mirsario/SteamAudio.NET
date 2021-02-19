using System;
using System.Runtime.InteropServices;

namespace Tests
{
	//This class contains only the most necessary functions & values that were used in the test.
	public unsafe static class AL
	{
		public const string Library = "soft_oal.dll";

		public enum SourceState
		{
			Initial = 0x1011,
			Playing = 0x1012,
			Paused = 0x1013,
			Stopped = 0x1014,
		}

		public enum GetSourceInt
		{
			SourceState = 0x1010,
			BuffersQueued = 0x1015,
			BuffersProcessed = 0x1016,
		}

		public enum Error
		{
			NoError = 0x0000,
			InvalidName = 0xA001,
			InvalidEnum = 0xA002,
			InvalidValue = 0xA003,
			InvalidOperation = 0xA004,
			OutOfMemory = 0xA005
		}

		static AL() => DllManager.PrepareResolver();

		//General

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alIsExtensionPresent")]
		public static extern bool IsExtensionPresent([In] [MarshalAs(UnmanagedType.LPStr)] string extName);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alGetError")]
		public static extern Error GetError();

		//Buffers

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alGenBuffers")]
		public static extern void GenBuffers(int numBuffers, uint[] bufferIdOutputArray);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alBufferData")]
		public static extern void BufferData(uint buffer, uint format, IntPtr data, int size, int frequency);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alBufferData")]
		public static extern void BufferData(uint buffer, uint format, byte[] data, int size, int frequency);

		//Sources

		public static void GenSource(out uint sourceId)
		{
			fixed(uint* ptr = &sourceId) {
				GenSources(1, ptr);
			}
		}

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alGenSources")]
		private static extern void GenSources(int numSources, [Out] uint* sourceIds);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alGetSourcei")]
		public static extern void GetSource(uint sourceId, GetSourceInt parameter, out int value);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alSourcePlay")]
		public static extern void SourcePlay(uint sourceId);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alSourceQueueBuffers")]
		public unsafe static extern void SourceQueueBuffers(uint sourceId, int numBuffers, [Out] uint* bufferIds);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true, EntryPoint = "alSourceUnqueueBuffers")]
		public unsafe static extern void SourceUnqueueBuffers(uint sourceId, int numBuffers, [Out] uint* bufferIds);
	}
}
