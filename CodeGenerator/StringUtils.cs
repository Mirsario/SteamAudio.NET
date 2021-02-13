namespace CodeGenerator
{
	internal static class StringUtils
	{
		public static string Capitalize(string text)
		{
			if(text.Length > 0 && char.IsLower(text[0])) {
				var chars = text.ToCharArray();

				chars[0] = char.ToUpper(chars[0]);

				return new string(chars);
			}

			return text;
		}
		public static string RemovePrefix(string text, string prefix)
		{
			if(text.StartsWith(prefix)) {
				text = text.Substring(prefix.Length);
			}

			return text;
		}
	}
}
