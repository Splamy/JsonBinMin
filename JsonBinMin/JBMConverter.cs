﻿using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

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
	public static byte[] Encode(byte[] json, JBMOptions? options = null)
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
	public byte[] EncodeEntity(byte[] json)
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
		if (options.UseDict != UseDict.Off)
		{
			FinalizeDictionary();
		}
		var ctx = new JBMEncoder(options, dictBuilder);
		ctx.WriteValue(elem);

		if (options.Compress)
		{
			var sourceStream = ctx.output;
			sourceStream.Position = 0;
			int sourceLength = (int)sourceStream.Length;

			var maxOutputSize = BrotliEncoder.GetMaxCompressedLength(sourceLength);
			var rawJbmBuffer = ArrayPool<byte>.Shared.Rent(sourceLength);
			var rawJbm = rawJbmBuffer.AsSpan(0, sourceLength);
			var outputBuffer = ArrayPool<byte>.Shared.Rent(maxOutputSize + 1);
			var output = outputBuffer.AsSpan();

			try
			{
				Util.Assert(sourceStream.Read(rawJbm) == rawJbm.Length);
				output[0] = (byte)JBMType.Compressed;
				Util.Assert(BrotliEncoder.TryCompress(rawJbm, output[1..], out var written, 11, 24));
				return output[..(written + 1)].ToArray();
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(rawJbmBuffer);
				ArrayPool<byte>.Shared.Return(outputBuffer);
			}
		}
		else
		{
			return ctx.output.ToArray();
		}
	}

	private static MemoryStream DecodeToStreamInternal(byte[] data)
	{
		if (data.Length == 0)
			return new MemoryStream();
		if (data[0] == (byte)JBMType.Compressed)
		{
			var mem = JBMDecoder.Decompress(data.AsSpan(1));
			data = mem.ToArray();
		}

		var ctx = new JBMDecoder();
		ReadOnlySpan<byte> parsePos = data.AsSpan();
		while (ctx.Parse(parsePos, out parsePos)) ;
		ctx.Output.Position = 0;
		return ctx.Output;
	}
	public static Stream DecodeToStream(byte[] data) => DecodeToStreamInternal(data);
	public static byte[] DecodeToBytes(byte[] data)
	{
		var stream = DecodeToStreamInternal(data);
		return stream.ToArray();
	}
	public static string DecodeToString(byte[] data)
	{
		using var stream = DecodeToStream(data);
		using var reader = new StreamReader(stream, Encoding.UTF8);
		return reader.ReadToEnd();
	}
	public static T? DecodeObject<T>(byte[] data)
	{
		var bytes = DecodeToBytes(data);
		return JsonSerializer.Deserialize<T>(bytes);
	}
}
