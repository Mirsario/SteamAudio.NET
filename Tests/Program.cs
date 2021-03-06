﻿//#define NO_PROCESSING

using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using SteamAudio;

namespace Tests
{
	public static class Program
	{
		public const int UpdateFrequency = 120;

		public const int BufferFormatMonoFloat32 = 0x10010;
		public const int BufferFormatStereoFloat32 = 0x10011;
		public const int SamplingRate = 44100;
		public const int AudioFrameSize = 1024;
		public const int AudioFrameSizeInBytes = AudioFrameSize * sizeof(float);

		//OpenAL
		private static IntPtr alAudioDevice;
		private static IntPtr alAudioContext;
		//Steam Audio
		private static IntPtr iplContext;
		private static IntPtr iplBinauralRenderer;
		private static IntPtr iplBinauralEffect;
		private static IPL.AudioFormat iplFormatMono;
		private static IPL.AudioFormat iplFormatStereo;
		private static IPL.AudioBuffer iplInputBuffer;
		private static IPL.AudioBuffer iplOutputBuffer;
		private static IPL.RenderingSettings iplRenderingSettings;

		unsafe static void Main(string[] args)
		{
			Console.WriteLine($"Working Directory: {Path.GetFullPath(".")}");

			try {
				var stopwatch = new Stopwatch();
				byte[] bytes = LoadRawAudio(string.Join(' ', args));
				using var audioStream = new MemoryStream(bytes);

				PrepareOpenAL();

#if !NO_PROCESSING
				PrepareSteamAudio();
#endif

				uint[] alBuffers = new uint[2];
				byte[] frameInputBuffer = new byte[AudioFrameSizeInBytes];

				AL.GenBuffers(alBuffers.Length, alBuffers);
				AL.GenSource(out uint sourceId);

				stopwatch.Start();

				void StreamBuffer(uint bufferId, Vector3 position)
				{
					int bytesRead = audioStream.Read(frameInputBuffer, 0, frameInputBuffer.Length);

					//Loop the audio on stream end.
					if(bytesRead < frameInputBuffer.Length) {
						audioStream.Position = 0;

						audioStream.Read(frameInputBuffer, 0, frameInputBuffer.Length - bytesRead);
					}

#if !NO_PROCESSING
					fixed(byte* ptr = frameInputBuffer) {
						iplInputBuffer.interleavedBuffer = (IntPtr)ptr;

						IPL.ApplyBinauralEffect(iplBinauralEffect, iplBinauralRenderer, iplInputBuffer, new IPL.Vector3(position.X, position.Y, position.Z), IPL.HrtfInterpolation.Nearest, 1f, iplOutputBuffer);
					}

					AL.BufferData(bufferId, BufferFormatStereoFloat32, iplOutputBuffer.interleavedBuffer, AudioFrameSizeInBytes * 2, iplRenderingSettings.samplingRate);
#else
					AL.BufferData(bufferId, BufferFormatMonoFloat32, frameInputBuffer, frameInputBuffer.Length, SamplingRate);
#endif

					CheckALErrors();
				}

				Console.WriteLine();

				int cursorPosX = Console.CursorLeft;
				int cursorPosY = Console.CursorTop;

				Console.CursorVisible = false;

				while(true) {
					var position = GetRotatingAudioPosition((float)stopwatch.Elapsed.TotalSeconds);

					//Display information

					Console.SetCursorPosition(cursorPosX, cursorPosY);
					Console.WriteLine($"Sound position: {position: 0.00;-0.00; 0.00}");

					TimeSpan streamPositionTimeSpan = TimeSpan.FromSeconds((int)(audioStream.Position / sizeof(float) / SamplingRate));
					TimeSpan streamLengthTimeSpan = TimeSpan.FromSeconds((int)(audioStream.Length / sizeof(float) / SamplingRate));

					Console.WriteLine($"Stream position: {streamPositionTimeSpan.Minutes:D2}:{streamPositionTimeSpan.Seconds:D2} / {streamLengthTimeSpan.Minutes:D2}:{streamLengthTimeSpan.Seconds:D2}");

					//Update streamed audio

					AL.GetSource(sourceId, AL.GetSourceInt.BuffersProcessed, out int numProcessedBuffers);
					AL.GetSource(sourceId, AL.GetSourceInt.BuffersQueued, out int numQueuedBuffers);

					int buffersToAdd = alBuffers.Length - numQueuedBuffers + numProcessedBuffers;

					while(buffersToAdd > 0) {
						uint bufferId = alBuffers[buffersToAdd - 1];

						if(numProcessedBuffers > 0) {
							AL.SourceUnqueueBuffers(sourceId, 1, &bufferId);

							numProcessedBuffers--;
						}

						StreamBuffer(bufferId, position);

						AL.SourceQueueBuffers(sourceId, 1, &bufferId);
						CheckALErrors();

						buffersToAdd--;
					}

					//Start playback whenever it stops
					AL.GetSource(sourceId, AL.GetSourceInt.SourceState, out int sourceState);

					if((AL.SourceState)sourceState != AL.SourceState.Playing) {
						AL.SourcePlay(sourceId);
					}

					CheckALErrors();

					//Sleep to not stress the CPU.
					Thread.Sleep(1);
				}
			}
			catch(Exception e) {
				Console.WriteLine($"{e.GetType().Name}: {e.Message}");
				Console.WriteLine("Press any key to continue...");
				Console.ReadKey();
			}

			try {
				UnloadSteamAudio();
				UnloadOpenAL();
			}
			catch { }
		}

		internal static byte[] LoadRawAudio(string rawAudioPath)
		{
			if(string.IsNullOrWhiteSpace(rawAudioPath)) {
				throw new ArgumentException($"Provide a path to a raw audio file in command line arguments to play it.");
			}

			if(!File.Exists(rawAudioPath)) {
				throw new ArgumentException($"Invalid audio path: '{rawAudioPath}'.");
			}

			return File.ReadAllBytes(rawAudioPath);
		}
		internal static Vector3 GetRotatingAudioPosition(float time)
		{
			Quaternion rotation = Quaternion.Identity;

			rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, time * 1 / 3f);
			rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitX, time * 1 / 2f);
			rotation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, time);

			return Vector3.Transform(new Vector3(1f, 0f, 0f), rotation);
		}

		//OpenAL
		private static void PrepareOpenAL()
		{
			alAudioDevice = ALC.OpenDevice(null);
			alAudioContext = ALC.CreateContext(alAudioDevice, null);

			if(!ALC.MakeContextCurrent(alAudioContext)) {
				throw new InvalidOperationException("Unable to make context current");
			}

			Console.WriteLine("Created OpenAL context.");

			//Require float32 support.
			const string Float32Extension = "AL_EXT_float32";

			if(!AL.IsExtensionPresent(Float32Extension)) {
				throw new Exception($"This program requires '{Float32Extension}' OpenAL extension to function.");
			}

			CheckALErrors();

			Console.WriteLine("OpenAL is ready.");
		}
		private static void UnloadOpenAL()
		{
			ALC.DestroyContext(alAudioContext);
			ALC.CloseDevice(alAudioDevice);
		}
		private static void CheckALErrors()
		{
			var error = AL.GetError();

			if(error != AL.Error.NoError) {
				throw new Exception($"OpenAL Error: {error}");
			}
		}
		//SteamAudio
		private static void PrepareSteamAudio()
		{
			//Steam Audio Initialization

			IPL.CreateContext(null, null, null, out iplContext);

			Console.WriteLine("Created SteamAudio context.");

			iplRenderingSettings = new IPL.RenderingSettings {
				samplingRate = SamplingRate,
				frameSize = AudioFrameSize
			};

			//Binaural Renderer

			var hrtfParams = new IPL.HrtfParams {
				type = IPL.HrtfDatabaseType.Default
			};

			IPL.CreateBinauralRenderer(iplContext, iplRenderingSettings, hrtfParams, out iplBinauralRenderer);

			//Audio Formats

			iplFormatMono = new IPL.AudioFormat {
				channelLayoutType = IPL.ChannelLayoutType.Speakers,
				channelLayout = IPL.ChannelLayout.Mono,
				channelOrder = IPL.ChannelOrder.Interleaved
			};
			iplFormatStereo = new IPL.AudioFormat {
				channelLayoutType = IPL.ChannelLayoutType.Speakers,
				channelLayout = IPL.ChannelLayout.Stereo,
				channelOrder = IPL.ChannelOrder.Interleaved
			};

			//Binaural Effect

			IPL.CreateBinauralEffect(iplBinauralRenderer, iplFormatMono, iplFormatStereo, out iplBinauralEffect);

			//Audio Buffers

			iplInputBuffer = new IPL.AudioBuffer {
				format = iplFormatMono,
				numSamples = iplRenderingSettings.frameSize,
				interleavedBuffer = IntPtr.Zero //Will be assigned before use.
			};

			IntPtr outputDataPtr = Marshal.AllocHGlobal(AudioFrameSizeInBytes * 2);

			iplOutputBuffer = new IPL.AudioBuffer {
				format = iplFormatStereo,
				numSamples = iplRenderingSettings.frameSize,
				interleavedBuffer = outputDataPtr
			};

			Console.WriteLine("SteamAudio is ready.");
		}
		private static void UnloadSteamAudio()
		{
			IPL.DestroyBinauralEffect(ref iplBinauralEffect);
			IPL.DestroyBinauralRenderer(ref iplBinauralRenderer);
			IPL.DestroyContext(ref iplContext);
			IPL.Cleanup();
		}
	}
}
