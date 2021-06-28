using System;
using System.Text.Json;

namespace JsonBinMin
{
	public class JsonBinMin
	{
		private readonly DictBuilder dictBuilder;
		private readonly JsonBinMinOptions options;

		public JsonBinMin() : this(JsonBinMinOptions.Default) { }
		public JsonBinMin(JsonBinMinOptions options)
		{
			dictBuilder = new(options);
			this.options = options;
		}

		public static byte[] Compress(string json)
		{
			var elem = JsonSerializer.Deserialize<JsonElement>(json);
			return Compress(elem);
		}
		public static byte[] Compress(byte[] json)
		{
			var elem = JsonSerializer.Deserialize<JsonElement>(json);
			return Compress(elem);
		}
		public static byte[] Compress(JsonElement elem)
		{
			var jbm = new JsonBinMin();
			jbm.AddToDictionary(elem);
			jbm.FinalizeDictionary();
			return jbm.CompressEntity(elem);
		}

		public void AddToDictionary(string json)
		{
			var elem = JsonSerializer.Deserialize<JsonElement>(json);
			AddToDictionary(elem);
		}
		public void AddToDictionary(byte[] json)
		{
			var elem = JsonSerializer.Deserialize<JsonElement>(json);
			AddToDictionary(elem);
		}
		public void AddToDictionary(JsonElement elem)
		{
			if (!options.UseDict)
				throw new NotSupportedException();

			dictBuilder.BuildDictionary(elem);
		}

		public void FinalizeDictionary()
		{
			if (!options.UseDict)
				throw new NotSupportedException();
			dictBuilder.FinalizeDictionary();
		}

		public byte[] CompressEntity(string json)
		{
			var elem = JsonSerializer.Deserialize<JsonElement>(json);
			return CompressEntity(elem);
		}
		public byte[] CompressEntity(byte[] json)
		{
			var elem = JsonSerializer.Deserialize<JsonElement>(json);
			return CompressEntity(elem);
		}
		public byte[] CompressEntity(JsonElement elem)
		{
			if (options.UseDict)
			{
				FinalizeDictionary();
			}
			var ctx = new CompressCtx(options, dictBuilder);
			ctx.WriteValue(elem);
			return ctx.output.ToArray();
		}

		public static string DecompressToString(byte[] json)
		{
			var ctx = new DecompressCtx();
			var parsePos = json.AsSpan();
			while (ctx.Parse(parsePos, out parsePos)) ;
			return ctx.Output.ToString();
		}
	}
}
