using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace JsonBinMin
{
	public class JsonBinMin
	{
		private readonly CompressCtx ctx;

		public JsonBinMin() : this(JsonBinMinOptions.Default) { }
		public JsonBinMin(JsonBinMinOptions options)
		{
			ctx = new CompressCtx() { Options = options };
		}

		public static byte[] Compress(string json) => new JsonBinMin().CompressEntity(json, true);

		public void AddToDictionary(string json)
		{
			if (!ctx.Options.UseDict)
				throw new NotSupportedException();

			var elem = JsonSerializer.Deserialize<JsonElement>(json);
			ctx.BuildDictionary(elem);
		}

		public byte[] CompressEntity(string json, bool addToDictionary)
		{
			var elem = JsonSerializer.Deserialize<JsonElement>(json);

			if (ctx.Options.UseDict)
			{
				if (addToDictionary)
					ctx.BuildDictionary(elem);
				ctx.WriteDictionary();
			}
			ctx.WriteValue(elem);
			return ctx.Output.ToArray();
		}

		public static string Decompress(byte[] json)
		{
			var ctx = new DecompressCtx();
			var parsePos = json.AsSpan();
			while (ctx.Parse(parsePos, out parsePos)) ;
			return ctx.Output.ToString();
		}
	}
}
