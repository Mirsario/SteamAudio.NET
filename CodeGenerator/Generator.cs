using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CppAst;
using CppAst.CodeGen.Common;
using CppAst.CodeGen.CSharp;
using Zio.FileSystems;

namespace CodeGenerator
{
	public static class Generator
	{
		internal static void Main(string[] args)
		{
			try {
				if(args.Length == 0) {
					throw new ArgumentException("An output path must be provided in command line arguments.");
				}

				ProcessFile("../SteamAudio/fmod/include/phonon/phonon.h", Path.GetFullPath(args[0]), "IPL.Generated.cs", "SteamAudio", "IPL");

				Console.WriteLine("Success.");
				Thread.Sleep(500);
			}
			catch(Exception e) {
				Console.WriteLine($"{e.GetType().Name}: {e.Message}");
				Console.WriteLine();
				Console.WriteLine("Press any key to close the application...");
				Console.ReadKey();
			}
		}

		public static void ProcessFile(string inputFile, string outputPath, string outputFile, string defaultNamespace, string defaultClass)
		{
			inputFile = Path.GetFullPath(inputFile);

			Console.WriteLine($"Processing file '{Path.GetFileName(inputFile)}'...");

			//Writing
			var converterOptions = new CSharpConverterOptions() {
				DefaultNamespace = defaultNamespace,
				DefaultClassLib = defaultClass,
				DefaultOutputFilePath = outputFile,
				DefaultDllImportNameAndArguments = "Library",
				DispatchOutputPerInclude = false,
				GenerateEnumItemAsFields = false,
				TypedefCodeGenKind = CppTypedefCodeGenKind.NoWrap,

				MappingRules = {
					//Remove prefixes from elements' names.
					e => e.MapAll<CppElement>().CppAction((converter, element) => {
						if(element is ICppMember member) {
							string prefix = member switch {
								CppType _ => "IPL",
								CppEnumItem _ => "IPL_",
								_ => null
							};

							if(prefix != null) {
								member.Name = StringUtils.Capitalize(StringUtils.RemovePrefix(member.Name, prefix));
							}
						}
					}).CSharpAction((converter, element) => {
						if(element is CSharpMethod method) {
							const string Prefix = "ipl";

							string oldName = method.Name;
							string newName = StringUtils.Capitalize(StringUtils.RemovePrefix(oldName, Prefix));

							//Add an EntryPoint parameter to the DllImportAttribute, so that this rename doesn't break anything.
							if(method.Attributes.FirstOrDefault(attrib => attrib is CSharpDllImportAttribute) is CSharpDllImportAttribute dllImportAttribute) {
								dllImportAttribute.EntryPoint = $@"""{oldName}""";
							}

							method.Name = newName;
						}
					}),

					//Replace the bool enum with an actual bool.
					e => e.Map<CppEnum>("Bool").Discard(),
					e => e.MapAll<CppDeclaration>().CSharpAction((converter, element) => {
						CSharpType type;
						Action<CSharpType> setType;

						if(element is CSharpField field) {
							type = field.FieldType;
							setType = value => field.FieldType = value;
						} else if(element is CSharpParameter parameter) {
							type = parameter.ParameterType;
							setType = value => parameter.ParameterType = value;
						} else {
							return;
						}

						if(type is CSharpFreeType freeType && freeType.Text == "unsupported_type /* enum Bool {...} */") {
							var boolean = converter.GetCSharpType(CppPrimitiveType.Bool, element);

							setType(boolean);

							if(boolean is CSharpTypeWithAttributes typeWithAttributes) {
								foreach(CSharpMarshalAttribute attribute in typeWithAttributes.Attributes.Where(a => a is CSharpMarshalAttribute)) {
									attribute.UnmanagedType = CSharpUnmanagedKind.U4;
								}
							}
						}
					}),

					//Rename enum elements from SCREAMING_SNAKECASE to LameupperCamelcase. There are manual fixes below, for cases where words aren't separated.
					e => e.MapAll<CppEnumItem>().CppAction((converter, element) => {
						var enumItem = (CppEnumItem)element;

						string name = enumItem.Name;
						string[] splits = name.Split('_');

						if(splits.Length > 1) {
							string prefix = splits[0];

							//Remove (potentially partial) prefixes of enum's name on its items' names.
							if(name.Length > prefix.Length + 1 && name.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase)) {
								name = name.Substring(prefix.Length + 1);
								splits = name.Split('_');
							}

							//Capitalize each part
							for(int i = 0; i < splits.Length; i++) {
								string split = splits[i];
								var chars = split.ToCharArray();

								for(int j = 0; j < chars.Length; j++) {
									chars[j] = j == 0 ? char.ToUpper(chars[j]) : char.ToLower(chars[j]);
								}

								splits[i] = new string(chars);
							}

							name = string.Join(string.Empty, splits);
						}

						enumItem.Name = name;
					}),

					//Fix weird 'ref void' parameters.
					e => e.MapAll<CppParameter>().CppAction((converter, element) => {
						var parameter = (CppParameter)element;

						if(parameter.Type is CppPointerType pointerType) {
							var canonicalElementType = pointerType.ElementType.GetCanonicalType();

							if(canonicalElementType is CppPrimitiveType primitiveType && primitiveType.Kind == CppPrimitiveKind.Void) {
								parameter.Type = new CppPointerType(CppPrimitiveType.Char);
							}
						}
					}),

					//Turn some 'ref' parameters to 'out' or 'in' based on \param documentation.
					e => e.MapAll<CppParameter>().CSharpAction((converter, element) => {
						var parameter = (CSharpParameter)element;

						if(!(parameter.ParameterType is CSharpRefType refParameterType)) {
							return;
						}

						if(!(element.Parent is CSharpMethod method) || !(method.CppElement is CppFunction function)) {
							return;
						}

						if(!(function.Comment?.Children?.FirstOrDefault(c => c is CppCommentParamCommand pc && pc.ParamName == parameter.Name) is CppCommentParamCommand parameterComment)) {
							return;
						}

						if(!(parameterComment?.Children?.FirstOrDefault() is CppCommentParagraph paragraph)) {
							return;
						}

						string paragraphText = paragraph.ToString().Trim();

						if(paragraphText.StartsWith("[out]")) {
							refParameterType.Kind = CSharpRefKind.Out;
						} else if(paragraphText.StartsWith("[in]")) { //Never actually used
							refParameterType.Kind = CSharpRefKind.In;
						}
					}),

					//Turn a 2D fixed array into an 1D one.
					e => e.Map<CppField>("Matrix4x4::elements").Type("float", 16),

					//Manually fix casing on some enum properties. This could theoretically be made automatic through crazy dictionary-based algorithms, but that's overkill.
					e => e.Map<CppEnumItem>("Error::Outofmemory").Name("OutOfMemory"),
					e => e.Map<CppEnumItem>("SceneType::Radeonrays").Name("RadeonRays"),
					e => e.Map<CppEnumItem>("ConvolutionType::Trueaudionext").Name("TrueAudioNext"),
					e => e.Map<CppEnumItem>("ChannelLayout::Fivepointone").Name("FivePointOne"),
					e => e.Map<CppEnumItem>("ChannelLayout::Sevenpointone").Name("SevenPointOne"),
					e => e.Map<CppEnumItem>("AmbisonicsOrdering::Fursemalham").Name("FurseMalham"),
					e => e.Map<CppEnumItem>("AmbisonicsNormalization::Fursemalham").Name("FurseMalham"),
					e => e.Map<CppEnumItem>("AmbisonicsNormalization::Sn3d").Name("Sn3D"),
					e => e.Map<CppEnumItem>("AmbisonicsNormalization::N3d").Name("N3D"),
					e => e.Map<CppEnumItem>("DistanceAttenuationModelType::Inversedistance").Name("InversedDistance"),
					e => e.Map<CppEnumItem>("DirectOcclusionMode::Notransmission").Name("NoTransmission"),
					e => e.Map<CppEnumItem>("DirectOcclusionMode::Transmissionbyvolume").Name("TransmissionByVolume"),
					e => e.Map<CppEnumItem>("DirectOcclusionMode::Transmissionbyfrequency").Name("TransmissionByFrequency"),
					e => e.Map<CppEnumItem>("BakedDataType::Staticsource").Name("StaticSource"),
					e => e.Map<CppEnumItem>("BakedDataType::Staticlistener").Name("StaticListener"),
					e => e.Map<CppEnumItem>("BakedDataType::Uniformfloor").Name("UniformFloor"),
				}
			};

			converterOptions.IncludeFolders.Add(Path.GetDirectoryName(inputFile));

			var compilation = CSharpConverter.Convert(new List<string> { inputFile }, converterOptions);

			if(compilation.HasErrors) {
				foreach(var message in compilation.Diagnostics.Messages) {
					if(message.Type == CppLogMessageType.Error) {
						Console.WriteLine(message);
					}
				}

				Console.ReadKey();

				return;
			}

			using var fileSystem = new PhysicalFileSystem();
			using var subFileSystem = new SubFileSystem(fileSystem, fileSystem.ConvertPathFromInternal(outputPath));

			var codeWriterOptions = new CodeWriterOptions(subFileSystem);
			var codeWriter = new CodeWriter(codeWriterOptions);

			compilation.DumpTo(codeWriter);
		}
	}
}
