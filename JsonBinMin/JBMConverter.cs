using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonBinMin.Aos;
using JsonBinMin.BinV1;
using JsonBinMin.Brotli;

namespace JsonBinMin;

public class JbmConverter(JbmOptions options)
{
	private readonly DictBuilder _dictBuilder = new(options);
	private readonly JbmOptions _options = options;

	public JbmConverter() : this(JbmOptions.Default) { }

	public static byte[] Encode(string json, JbmOptions? options = null)
	{
		options ??= JbmOptions.Default;
		var elem = JsonSerializer.Deserialize<JsonElement>(json, options.JsonSerializerOptions);
		return Encode(elem, options);
	}
	public static byte[] Encode(ReadOnlySpan<byte> json, JbmOptions? options = null)
	{
		options ??= JbmOptions.Default;
		var elem = JsonSerializer.Deserialize<JsonElement>(json, options.JsonSerializerOptions);
		return Encode(elem, options);
	}
	public static byte[] EncodeObject<T>(T obj, JbmOptions? options = null)
	{
		options ??= JbmOptions.Default;
		var elem = JsonSerializer.SerializeToElement(obj, options.JsonSerializerOptions);
		return Encode(elem, options);
	}
	public static byte[] Encode(JsonElement elem, JbmOptions? options = null)
	{
		var jbm = new JbmConverter(options ?? JbmOptions.Default);
		if (jbm._options.UseDict != UseDict.Off)
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
		if (_options.UseDict == UseDict.Off)
		{
			throw new NotSupportedException();
		}

		_dictBuilder.BuildDictionary(elem);
	}

	public void FinalizeDictionary()
	{
		if (_options.UseDict == UseDict.Off)
		{
			throw new NotSupportedException();
		}

		_dictBuilder.FinalizeDictionary();
	}

	public byte[] EncodeEntity(string json)
	{
		var elem = JsonSerializer.Deserialize<JsonElement>(json, _options.JsonSerializerOptions);
		return EncodeEntity(elem);
	}
	public byte[] EncodeEntity(ReadOnlySpan<byte> json)
	{
		var elem = JsonSerializer.Deserialize<JsonElement>(json, _options.JsonSerializerOptions);
		return EncodeEntity(elem);
	}
	public byte[] EncodeEntityObject<T>(T obj)
	{
		var elem = JsonSerializer.SerializeToElement(obj, _options.JsonSerializerOptions);
		return EncodeEntity(elem);
	}
	public byte[] EncodeEntity(JsonElement elem)
	{
		var flags = EncodeFlags.None;

		JsonNode? aosOut;
		if (_options.UseAos)
		{
			flags |= EncodeFlags.Aos;
			aosOut = AosConverter.Encode(elem.ToJsonNode(), _options);
		}
		else
		{
			aosOut = elem.ToJsonNode();
		}

		ReadOnlyMemory<byte> jbmOut;
		if (_options.UseJbm)
		{
			flags |= EncodeFlags.Jbm;
			var ctx = new JbmEncoder(_options, _dictBuilder);
			ctx.WriteValue(JsonSerializer.SerializeToElement(aosOut, _options.JsonSerializerOptions));
			jbmOut = ctx.Output.GetBuffer().AsMemory(0, (int)ctx.Output.Length);
		}
		else
		{
			jbmOut = JsonSerializer.SerializeToUtf8Bytes(aosOut, _options.JsonSerializerOptions);
		}

		ReadOnlyMemory<byte> compressOut;
		if (_options.Compress)
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

	private static ReadOnlyMemory<byte> DecodeToRomInternal(ReadOnlyMemory<byte> data, JbmOptions? options = null)
	{
		if (data.Length == 0)
		{
			return ReadOnlyMemory<byte>.Empty;
		}

		options ??= JbmOptions.Default;

		var flags = (EncodeFlags)data.Span[0];
		data = data[1..];

		if (flags.HasFlag(EncodeFlags.Compressed))
		{
			data = BrotliUtil.Decompress(data.Span);
		}

		if (flags.HasFlag(EncodeFlags.Jbm))
		{
			var ctx = new JbmDecoder(options);
			var parsePos = data.Span;
			while (ctx.Parse(parsePos, out parsePos)) ;
			data = ctx.Output.GetBuffer().AsMemory(0, (int)ctx.Output.Length);
		}

		if (flags.HasFlag(EncodeFlags.Aos))
		{
			var aosData = JsonSerializer.Deserialize<AosData<JsonElement>>(data.Span, options.JsonSerializerOptions)!;
			var aosDecoded = AosConverter.Decode(aosData, options);
			data = JsonSerializer.SerializeToUtf8Bytes(aosDecoded, options.JsonSerializerOptions);
		}

		return data;
	}
	public static Stream DecodeToStream(ReadOnlyMemory<byte> data, JbmOptions? options = null) => new MemoryStream(DecodeToRomInternal(data, options).ToArray());
	public static byte[] DecodeToBytes(ReadOnlyMemory<byte> data, JbmOptions? options = null)
	{
		var stream = DecodeToRomInternal(data, options);
		return stream.ToArray();
	}
	public static string DecodeToString(ReadOnlyMemory<byte> data, JbmOptions? options = null)
	{
		var rom = DecodeToRomInternal(data, options);
		return Encoding.UTF8.GetString(rom.Span);
	}
	public static T? DecodeObject<T>(ReadOnlyMemory<byte> data, JbmOptions? options = null)
	{
		var bytes = DecodeToBytes(data, options);
		return JsonSerializer.Deserialize<T>(bytes);
	}
}