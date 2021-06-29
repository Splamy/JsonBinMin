using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace JsonBinMin
{
	public class JBMConverter
	{
		private readonly DictBuilder dictBuilder;
		private readonly JBMOptions options;

		public JBMConverter() : this(JBMOptions.Default) { }
		public JBMConverter(JBMOptions options)
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
			var jbm = new JBMConverter();
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

		private static MemoryStream DecompressToStreamInternal(byte[] data)
		{
			var ctx = new DecompressCtx();
			ReadOnlySpan<byte> parsePos = data.AsSpan();
			while (ctx.Parse(parsePos, out parsePos)) ;
			ctx.Output.Position = 0;
			return ctx.Output;
		}
		public static Stream DecompressToStream(byte[] data) => DecompressToStreamInternal(data);
		public static byte[] DecompressToBytes(byte[] data)
		{
			var stream = DecompressToStreamInternal(data);
			return stream.ToArray();
		}
		public static string DecompressToString(byte[] data)
		{
			using var stream = DecompressToStream(data);
			using var reader = new StreamReader(stream, Encoding.UTF8);
			return reader.ReadToEnd();
		}
	}
}
