namespace CodeGenerator
{
	internal static class StringUtils
	{
		public static string Capitalize(string text)
		{
			if (text.Length > 0 && char.IsLower(text[0])) {
				char[] chars = text.ToCharArray();

				chars[0] = char.ToUpper(chars[0]);

				return new string(chars);
			}

			return text;
		}

		public static string RemovePrefix(string text, string prefix)
		{
			if (text.StartsWith(prefix)) {
				text = text.Substring(prefix.Length);
			}

			return text;
		}

		public static string RemoveSuffix(string text, string suffix)
		{
			if (text.EndsWith(suffix)) {
				text = text.Substring(0, text.Length - suffix.Length);
			}

			return text;
		}
	}
}
