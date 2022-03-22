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
		private static readonly Dictionary<string, string> NameReplacements = new() {
			// C# naming convention "violations" fixes.

			{ "HRTF", "Hrtf" },
			{ "SIMD", "Simd" },

			// Case conversion mistake fixes.
			// This could theoretically be made automatic through crazy dictionary-based algorithms, but that's overkill.

			{ "Outofmemory", "OutOfMemory" },
			{ "Tailremaining", "TailRemaining" },
			{ "Tailcomplete", "TailComplete" },
			{ "Staticsource", "StaticSource" },
			{ "Staticlistener", "StaticListener" },
			{ "Inversedistance", "InverseDistance" },
			{ "Radeonrays", "RadeonRays" },
			{ "Uniformfloor", "UniformFloor" },
			{ "N3d", "N3D" },
			{ "Sn3D", "SN3D" },
			{ "Applydistanceattenuation", "ApplyDistanceAttenuation" },
			{ "Applyairabsorption", "ApplyAirAbsorption" },
			{ "Applydirectivity", "ApplyDirectivity" },
			{ "Applyocclusion", "ApplyOcclusion" },
			{ "Applytransmission", "ApplyTransmission" },
			{ "Bakeconvolution", "BakeConvolution" },
			{ "Bakeparametric", "BakeParametric" },
			{ "Distanceattenuation", "DistanceAttenuation" },
			{ "Airabsorption", "AirAbsorption" },

			// Special
			// Kind of doing actual renames for the sake of the explicitness C# naming conventions have.

			{ "Freqindependent", "FrequencyIndependent" },
			{ "Freqdependent", "FrequencyDependent" },
		};

		internal static void Main(string[] args)
		{
			Console.WriteLine($"NOTE: To run this generator, you may need a Windows machine and Visual Studio with C++ Development packages installed.");

			try {
				if (args.Length == 0) {
					throw new ArgumentException("An output path must be provided in command line arguments.");
				}

				ProcessFile("include/phonon.h", Path.GetFullPath(args[0]), "IPL.Generated.cs", "SteamAudio", "IPL");

				Console.WriteLine("Success.");
				Thread.Sleep(500);
			}
			catch (Exception e) {
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
				ParseMacros = true,
				TypedefCodeGenKind = CppTypedefCodeGenKind.NoWrap,

				MappingRules = {
					// Remove prefixes from elements' names.
					e => e.MapAll<CppElement>().CSharpAction((converter, element) => {
						switch (element) {
							case CSharpNamedType csNamedType:
								csNamedType.Name = StringUtils.Capitalize(StringUtils.RemovePrefix(csNamedType.Name, "IPL"));
								break;
							case CSharpEnumItem csEnumItem:
								csEnumItem.Name = StringUtils.Capitalize(StringUtils.RemovePrefix(csEnumItem.Name, "IPL_"));
								break;
							case CSharpMethod csMethod:
								string oldName = csMethod.Name;
								string newName = StringUtils.Capitalize(StringUtils.RemovePrefix(oldName, "ipl"));

								// Add an EntryPoint parameter to the DllImportAttribute, so that this rename doesn't break anything.
								if (csMethod.Attributes.FirstOrDefault(attrib => attrib is CSharpDllImportAttribute) is CSharpDllImportAttribute dllImportAttribute) {
									dllImportAttribute.EntryPoint = $@"""{oldName}""";
								}

								csMethod.Name = newName;

								break;
						}
					}),

					// Replace the bool enum with an actual bool.
					e => e.Map<CppEnum>("IPLbool").Discard(),
					e => e.MapAll<CppDeclaration>().CSharpAction((converter, element) => {
						CSharpType type;
						Action<CSharpType> setType;

						if (element is CSharpField field) {
							type = field.FieldType;
							setType = value => field.FieldType = value;
						} else if (element is CSharpParameter parameter) {
							type = parameter.ParameterType;
							setType = value => parameter.ParameterType = value;
						} else {
							return;
						}

						if (type is CSharpFreeType freeType && freeType.Text == "unsupported_type /* enum IPLbool {...} */") {
							var boolean = converter.GetCSharpType(CppPrimitiveType.Bool, element);

							setType(boolean);

							if (boolean is CSharpTypeWithAttributes typeWithAttributes) {
								foreach(CSharpMarshalAttribute attribute in typeWithAttributes.Attributes.Where(a => a is CSharpMarshalAttribute)) {
									attribute.UnmanagedType = CSharpUnmanagedKind.U4;
								}
							}
						}
					}),

					// Rename enum elements from SCREAMING_SNAKECASE to LameupperCamelcase. There are manual fixes below, for cases where words aren't separated.
					e => e.MapAll<CppEnumItem>().CSharpAction((converter, element) => {
						var csEnumItem = (CSharpEnumItem)element;

						string name = csEnumItem.Name;
						string[] splits = name.Split('_');

						if (splits.Length > 1) {
							string prefix = splits[0];

							// Remove (potentially partial) prefixes of enum's name on its items' names.
							if (name.Length > prefix.Length + 1 && name.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase)) {
								name = name.Substring(prefix.Length + 1);
								splits = name.Split('_');
							}

							// Capitalize each part
							for (int i = 0; i < splits.Length; i++) {
								string split = splits[i];
								char[] chars = split.ToCharArray();

								for (int j = 0; j < chars.Length; j++) {
									chars[j] = j == 0 ? char.ToUpper(chars[j]) : char.ToLower(chars[j]);
								}

								splits[i] = new string(chars);
							}

							name = string.Join(string.Empty, splits);
						}

						csEnumItem.Name = name;
					}),

					// Fix weird 'ref void' parameters.
					e => e.MapAll<CppParameter>().CSharpAction((converter, element) => {
						var parameter = (CSharpParameter)element;
						var parameterType = parameter.ParameterType;

						if (parameterType is CSharpRefType refType && refType.ElementType is CSharpPrimitiveType primitiveType && primitiveType.Kind == CSharpPrimitiveKind.Void) {
							parameter.ParameterType = CSharpPrimitiveType.IntPtr;
						}
					}),

					// Turn some 'ref' parameters to 'out' or 'in' based on \param documentation.
					e => e.MapAll<CppParameter>().CSharpAction((converter, element) => {
						var parameter = (CSharpParameter)element;

						if (parameter.ParameterType is not CSharpRefType refParameterType) {
							return;
						}

						if (element.Parent is not CSharpMethod method || method.CppElement is not CppFunction function) {
							return;
						}

						if (function.Comment?.Children?.FirstOrDefault(c => c is CppCommentParamCommand pc && pc.ParamName == parameter.Name) is not CppCommentParamCommand parameterComment) {
							return;
						}

						if (parameterComment?.Children?.FirstOrDefault() is not CppCommentParagraph paragraph) {
							return;
						}

						string paragraphText = paragraph.ToString().Trim();

						if (paragraphText.StartsWith("[out]")) {
							refParameterType.Kind = CSharpRefKind.Out;
						} else if (paragraphText.StartsWith("[in]")) {
							refParameterType.Kind = CSharpRefKind.In;
						}
					}),
					
					// Handle _IPL*_t types. This has to be done at Cpp level due to the generated C# methods' code using raw text and therefore not being linked to the methods' types.
					e => e.MapAll<CppClass>().CppAction((converter, cppElement) => {
						var cppClass = (CppClass)cppElement;
						string newName = cppClass.Name;

						newName = StringUtils.RemovePrefix(newName, "_IPL");
						newName = StringUtils.RemoveSuffix(newName, "_t");
						newName = StringUtils.Capitalize(newName);

						foreach (var pair in NameReplacements) {
							newName = newName.Replace(pair.Key, pair.Value);
						}

						cppClass.Name = newName;
					}),

					// Execute replacements from the NameReplacements dictionaries for all C# types.
					e => e.MapAll<CppElement>().CSharpAction((converter, csElement) => {
						string oldName;

						if (csElement is ICSharpMember csMember) {
							oldName = csMember.Name;
						} else if (csElement is CSharpEnumItem csEnumItem) {
							oldName = csEnumItem.Name;
						} else {
							return;
						}

						string newName = oldName;

						foreach (var pair in NameReplacements) {
							newName = newName.Replace(pair.Key, pair.Value);
						}

						if (newName != oldName) {
							switch (csElement) {
								case ICSharpMember csMember2:
									csMember2.Name = newName;
									break;
								case CSharpEnumItem csEnumItem2:
									csEnumItem2.Name = newName;
									break;
							}
						}
					}),

					// Lazy fixes for conversion mistakes.
					e => e.Map<CppField>("IPLMatrix4x4::elements").Type("float", 16),
					e => e.Map<CppEnumItem>("IPLSIMDLevel::IPL_SIMDLEVEL_NEON").CSharpAction((_, element) => ((CSharpEnumItem)element).Value = "Sse2"),
				}
			};

			converterOptions.IncludeFolders.Add(Path.GetDirectoryName(inputFile));

			var compilation = CSharpConverter.Convert(new List<string> { inputFile }, converterOptions);

			if (compilation.HasErrors) {
				foreach (var message in compilation.Diagnostics.Messages) {
					if (message.Type == CppLogMessageType.Error) {
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
