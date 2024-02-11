using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonBinMin.Aos;
using JsonBinMin.BinV1;
using JsonBinMin.Brotli;

namespace JsonBinMin;

public class JBMConverter(JBMOptions options)
{
	private readonly DictBuilder dictBuilder = new(options);
	private readonly JBMOptions options = options;

	public JBMConverter() : this(JBMOptions.Default) { }

	public static byte[] Encode(string json, JBMOptions? options = null)
	{
		options ??= JBMOptions.Default;
		var elem = JsonSerializer.Deserialize<JsonElement>(json, options.JsonSerializerOptions);
		return Encode(elem, options);
	}
	public static byte[] Encode(ReadOnlySpan<byte> json, JBMOptions? options = null)
	{
		options ??= JBMOptions.Default;
		var elem = JsonSerializer.Deserialize<JsonElement>(json, options.JsonSerializerOptions);
		return Encode(elem, options);
	}
	public static byte[] EncodeObject<T>(T obj, JBMOptions? options = null)
	{
		options ??= JBMOptions.Default;
		var elem = JsonSerializer.SerializeToElement(obj, options.JsonSerializerOptions);
		return Encode(elem, options);
	}
	public static byte[] Encode(JsonElement elem, JBMOptions? options = null)
	{
		var jbm = new JBMConverter(options ?? JBMOptions.Default);
		if (jbm.options.UseDict != UseDict.Off)
		{
			jbm.AddToDictionary(elem);
			jbm.FinalizeDictionary();
		}
		return jbm.EncodeEntity(elem);
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
		if (options.UseDict == UseDict.Off)
			throw new NotSupportedException();

		dictBuilder.BuildDictionary(elem);
	}

	public void FinalizeDictionary()
	{
		if (options.UseDict == UseDict.Off)
			throw new NotSupportedException();
		dictBuilder.FinalizeDictionary();
	}

	public byte[] EncodeEntity(string json)
	{
		var elem = JsonSerializer.Deserialize<JsonElement>(json, options.JsonSerializerOptions);
		return EncodeEntity(elem);
	}
	public byte[] EncodeEntity(ReadOnlySpan<byte> json)
	{
		var elem = JsonSerializer.Deserialize<JsonElement>(json, options.JsonSerializerOptions);
		return EncodeEntity(elem);
	}
	public byte[] EncodeEntityObject<T>(T obj)
	{
		var elem = JsonSerializer.SerializeToElement(obj, options.JsonSerializerOptions);
		return EncodeEntity(elem);
	}
	public byte[] EncodeEntity(JsonElement elem)
	{
		var flags = EncodeFlags.None;

		JsonElement aosOut;
		if (options.UseAos)
		{
			flags |= EncodeFlags.Aos;
			aosOut = JsonSerializer.SerializeToElement(AosConverter.Encode(JsonObject.Create(elem)), options.JsonSerializerOptions);
		}
		else
		{
			aosOut = elem;
		}

		ReadOnlyMemory<byte> jbmOut;
		if (options.UseJbm)
		{
			flags |= EncodeFlags.Jbm;
			var ctx = new JBMEncoder(options, dictBuilder);
			ctx.WriteValue(aosOut);
			jbmOut = ctx.output.GetBuffer().AsMemory(0, (int)ctx.output.Length);
		}
		else
		{
			jbmOut = JsonSerializer.SerializeToUtf8Bytes(aosOut, options.JsonSerializerOptions);
		}

		ReadOnlyMemory<byte> compressOut;
		if (options.Compress)
		{
			flags |= EncodeFlags.Compressed;
			compressOut = BrotliUtil.Compress(jbmOut.Span);
		}
		else
		{
			compressOut = jbmOut;
		}

		var ret = new byte[compressOut.Length + 1];
		ret[0] = (byte)flags;
		compressOut.Span.CopyTo(ret.AsSpan(1));
		return ret;
	}

	private static ReadOnlyMemory<byte> DecodeToRomInternal(ReadOnlyMemory<byte> data)
	{
		if (data.Length == 0)
			return ReadOnlyMemory<byte>.Empty;

		EncodeFlags flags;
		var flagByte = data.Span[0];
		// Backwards compatibility
		if (flagByte == 0b0_1111_101)
		{
			flags = EncodeFlags.Compressed | EncodeFlags.Jbm;
		}
		else
		{
			flags = (EncodeFlags)flagByte;
		}

		data = data[1..];

		if (flags.HasFlag(EncodeFlags.Compressed))
		{
			data = BrotliUtil.Decompress(data.Span);
		}

		if (flags.HasFlag(EncodeFlags.Jbm))
		{
			var ctx = new JBMDecoder();
			var parsePos = data.Span;
			while (ctx.Parse(parsePos, out parsePos)) ;
			data = ctx.Output.GetBuffer().AsMemory(0, (int)ctx.Output.Length);
		}

		if (flags.HasFlag(EncodeFlags.Aos))
		{
			var aosData = JsonSerializer.Deserialize<AosData<JsonElement>>(data.Span)!;
			var aosDocoded = AosConverter.Decode(aosData);
			data = JsonSerializer.SerializeToUtf8Bytes(aosDocoded);
		}

		return data;
	}
	public static Stream DecodeToStream(byte[] data) => new MemoryStream(DecodeToRomInternal(data).ToArray());
	public static byte[] DecodeToBytes(byte[] data)
	{
		var stream = DecodeToRomInternal(data);
		return stream.ToArray();
	}
	public static string DecodeToString(byte[] data)
	{
		var rom = DecodeToRomInternal(data);
		return Encoding.UTF8.GetString(rom.Span);
	}
	public static T? DecodeObject<T>(byte[] data)
	{
		var bytes = DecodeToBytes(data);
		return JsonSerializer.Deserialize<T>(bytes);
	}
}