using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace JsonBinMin.BinV1;

internal class JbmEncoder
{
	private static readonly Encoding Utf8Encoder = new UTF8Encoding(false, true);
	private readonly JbmOptions _options;
	private readonly DictBuilder _dict;
	public MemoryStream Output { get; } = new();

	public JbmEncoder(JbmOptions options, DictBuilder dictBuilder)
	{
		_options = options;
		dictBuilder.FinalizeDictionary();
		_dict = dictBuilder;
		Output.Write(dictBuilder.DictSerialized);
	}

	public void WriteValue(JsonElement elem)
	{
		switch (elem.ValueKind)
		{
			case JsonValueKind.Undefined:
				throw new InvalidOperationException();

			case JsonValueKind.Object:
				var objElements = elem.EnumerateObject().ToArray();

				if (objElements.Length <= Constants.SqueezedInlineMaxValue)
				{
					Output.WriteByte((byte)((byte)JBMType.Object | objElements.Length));
				}
				else
				{
					Output.WriteByte((byte)JBMType.ObjectExt);
					WriteNumberValue(objElements.Length.ToString(CultureInfo.InvariantCulture));
				}

				foreach (var kvp in objElements)
				{
					WriteStringValue(kvp.Name);
					WriteValue(kvp.Value);
				}

				break;

			case JsonValueKind.Array:
				var arrElemems = elem.EnumerateArray().ToArray();

				if (arrElemems.Length <= Constants.SqueezedInlineMaxValue)
				{
					Output.WriteByte((byte)((byte)JBMType.Array | arrElemems.Length));
				}
				else
				{
					Output.WriteByte((byte)JBMType.ArrayExt);
					WriteNumberValue(arrElemems.Length.ToString(CultureInfo.InvariantCulture));
				}

				foreach (var arrItem in arrElemems)
					WriteValue(arrItem);
				break;

			case JsonValueKind.String:
				WriteStringValue(elem.GetString());
				break;

			case JsonValueKind.Number:
				WriteNumberValue(elem.GetRawText());
				break;

			case JsonValueKind.True:
				Output.WriteByte((byte)JBMType.True);
				break;

			case JsonValueKind.False:
				Output.WriteByte((byte)JBMType.False);
				break;

			case JsonValueKind.Null:
				WriteNull();
				break;

			default:
				throw new InvalidOperationException();
		}
	}

	public void WriteStringValue(string? str)
	{
		if (str is null)
		{
			WriteNull();
			return;
		}

		if (TryWriteStringFromDict(str))
		{
			return;
		}

		WriteStringValue(str, Output, _options);
	}

	public static void WriteStringValue(string str, Stream output, JbmOptions options)
	{
		var bytes = Utf8Encoder.GetBytes(str);

		if (bytes.Length <= Constants.SqueezedInlineMaxValue)
		{
			output.WriteByte((byte)((byte)JBMType.String | bytes.Length));
		}
		else
		{
			output.WriteByte((byte)JBMType.StringExt);
			WriteNumberValue(bytes.Length.ToString(CultureInfo.InvariantCulture), output, options);
		}

		output.Write(bytes);
	}

	public bool TryWriteStringFromDict(string str)
	{
		if (_options.UseDict == UseDict.Off || !_dict.TryGetString(str, out var entry))
		{
			return false;
		}

		if (entry.IsIndexed)
		{
			Output.WriteByte((byte)(0x80 | entry.Index));
		}
		else
		{
			Output.Write(entry.Data);
		}

		return true;
	}

	public void WriteNumberValue(string num)
	{
		if (TryWriteNumberFromDict(num))
		{
			return;
		}

		WriteNumberValue(num, Output, _options);
	}

	private static readonly SearchValues<char> Dot = SearchValues.Create(['.']);
	private static readonly SearchValues<char> UppercaseE = SearchValues.Create(['E']);
	
	public static void WriteNumberValue(string numRaw, Stream output, JbmOptions options)
	{
		var num = numRaw.AsSpan();

		if (TryWriteIntegerNumber(num, output))
		{
			return;
		}

		var numStr = GetNumStr(num);
		var numStrLen = numStr.Length;

		var upperE = num.ContainsAny(UppercaseE);
		var tail0 = num.EndsWith(".0", StringComparison.Ordinal);
		if (tail0)
		{
			num = num[..^2];
		}

		if (numStrLen >= 3
		    && options.UseFloats.HasFlag(UseFloats.Half)
		    && TryGetRoundtripSaveFloat(num, "G5", out Half halfVal))
		{
			Span<byte> buf = stackalloc byte[3];
			buf[0] = GetWithFloatFlags(JBMType.Float16, tail0, upperE);
			Util.Assert(BitConverter.TryWriteBytes(buf[1..], halfVal));
			output.Write(buf);
			return;
		}

		if (numStrLen >= 5
		    && options.UseFloats.HasFlag(UseFloats.Single)
		    && TryGetRoundtripSaveFloat(num, "G9", out float floatVal))
		{
			Span<byte> buf = stackalloc byte[5];
			buf[0] = GetWithFloatFlags(JBMType.Float32, tail0, upperE);
			Util.Assert(BitConverter.TryWriteBytes(buf[1..], floatVal));
			output.Write(buf);
			return;
		}

		if (numStrLen >= 9
		    && options.UseFloats.HasFlag(UseFloats.Double)
		    && TryGetRoundtripSaveFloat(num, "G17", out double doubleVal))
		{
			Span<byte> buf = stackalloc byte[9];
			buf[0] = GetWithFloatFlags(JBMType.Float64, tail0, upperE);
			Util.Assert(BitConverter.TryWriteBytes(buf[1..], doubleVal));
			output.Write(buf);
			return;
		}

		output.Write(numStr);
	}

	private static bool TryWriteIntegerNumber(ReadOnlySpan<char> num, Stream output)
	{
		if (num.ContainsAny(Dot))
		{
			return false;
		}

		var numNeg = num.StartsWith("-");
		var posNum = numNeg ? num[1..] : num;

		if (ulong.TryParse(posNum, NumberStyles.None, CultureInfo.InvariantCulture, out var integerVal))
		{
			switch (integerVal)
			{
				case <= Constants.JbmIntInlineMaxValue when !numNeg:
					output.WriteByte((byte)((byte)JBMType.IntInline | integerVal));
					return true;
				case <= Constants.JbmInt8MaxValue:
				{
					output.Write([
						GetWithNegativeFlag(JBMType.Int8, numNeg),
						(byte)(integerVal - Constants.JbmInt8Offset)
					]);
					return true;
				}
				case <= Constants.JbmInt16MaxValue:
				{
					Span<byte> buf = stackalloc byte[3];
					buf[0] = GetWithNegativeFlag(JBMType.Int16, numNeg);
					BinaryPrimitives.WriteUInt16LittleEndian(buf[1..], (ushort)(integerVal - Constants.JbmInt16Offset));
					output.Write(buf);
					return true;
				}
				case <= Constants.JbmInt24MaxValue:
				{
					Span<byte> buf = stackalloc byte[4];
					buf[0] = GetWithNegativeFlag(JBMType.Int24, numNeg);
					var offsetVal = integerVal - Constants.JbmInt24Offset;
					BinaryPrimitives.WriteUInt16LittleEndian(buf[1..], (ushort)(offsetVal & 0xFFFF));
					buf[3] = (byte)(offsetVal >> 16 & 0xFF);
					output.Write(buf);
					return true;
				}
				case <= Constants.JbmInt32MaxValue:
				{
					Span<byte> buf = stackalloc byte[5];
					buf[0] = GetWithNegativeFlag(JBMType.Int32, numNeg);
					BinaryPrimitives.WriteUInt32LittleEndian(buf[1..], (uint)(integerVal - Constants.JbmInt32Offset));
					output.Write(buf);
					return true;
				}
				case <= Constants.JbmInt48MaxValue:
				{
					Span<byte> buf = stackalloc byte[7];
					buf[0] = GetWithNegativeFlag(JBMType.Int48, numNeg);
					var offsetVal = integerVal - Constants.JbmInt48Offset;
					BinaryPrimitives.WriteUInt32LittleEndian(buf[1..], (uint)(offsetVal & 0xFFFFFFFF));
					BinaryPrimitives.WriteUInt16LittleEndian(buf[5..], (ushort)(offsetVal >> 32 & 0xFFFF));
					output.Write(buf);
					return true;
				}
			}
		}

		if (BigInteger.TryParse(posNum, out var bi))
		{
			if (bi <= Constants.JbmInt64MaxValue)
			{
				Span<byte> buf = stackalloc byte[9];
				buf[0] = GetWithNegativeFlag(JBMType.Int64, numNeg);
				BinaryPrimitives.WriteUInt64LittleEndian(buf[1..], (ulong)(bi - Constants.JbmInt64Offset));
				output.Write(buf);
				return true;
			}
			else
			{
				bi -= Constants.JbmIntRleOffset;
				Debug.Assert(bi >= 0);

				var byteLength = bi.GetByteCount(true);

				var buffer = new byte[1 + 5 + byteLength].AsSpan();
				buffer[0] = GetWithNegativeFlag(JBMType.IntRle, numNeg);
				Util.Assert(TryWriteRleNum(buffer[1..], new BigInteger(byteLength), out var w1));
				Util.Assert(bi.TryWriteBytes(buffer[(1 + w1)..], out var w2, true));

				output.Write(buffer[..(1 + w1 + w2)]);
			}

			return true;
		}

		return false;
	}

	private static bool TryGetRoundtripSaveFloat<T>(ReadOnlySpan<char> num, string format,
		[MaybeNullWhen(false)] out T val)
		where T : IFloatingPoint<T>
	{
		if (!T.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
		{
			return false;
		}

		var roundtripped = val.ToString(format, CultureInfo.InvariantCulture);

		return num.Equals(roundtripped, StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryWriteRleNum(Span<byte> data, BigInteger bi, out int written)
	{
		Debug.Assert(bi >= 0);
		var buf = new List<byte>();

		while (bi > 0)
		{
			var b = (byte)(bi & 0x7F);
			bi >>= 7;
			buf.Add(b);
		}

		if (buf.Count > data.Length)
		{
			written = 0;
			return false;
		}

		for (var i = 1; i < buf.Count; i++) buf[i] |= 0x80;
		buf.Reverse();

		CollectionsMarshal.AsSpan(buf).CopyTo(data);
		written = buf.Count;
		return true;
	}

	public bool TryWriteNumberFromDict(string num)
	{
		if (_options.UseDict == UseDict.Off || !_dict.TryGetNumber(num, out var entry))
		{
			return false;
		}

		if (entry.IsIndexed)
		{
			Output.WriteByte((byte)(0x80 | entry.Index));
		}
		else
		{
			Output.Write(entry.Data);
		}

		return true;
	}

	private static byte GetWithNegativeFlag(JBMType t, bool n) => n ? (byte)((int)t | 1) : (byte)t;

	private static byte GetWithFloatFlags(JBMType t, bool tail0, bool upper)
		=> (byte)((byte)t | (tail0 ? 2 : 0) | (upper ? 1 : 0));

	public static byte[] GetNumStr(ReadOnlySpan<char> num)
	{
		// 0 - 9 : '0' - '9'
		// 10    : '+'
		// 11    : '-'
		// 12    : '.'
		// 13    : 'e'
		// 14    : 'E'
		// 15    : END

#if NET9_0_OR_GREATER
		var neg = num.StartsWith('-');
#else
		var neg = num.StartsWith("-");
#endif
		if (neg)
		{
			num = num[1..];
		}

		if (num is ['0', '.', '0'])
		{
			return [(byte)((byte)JBMType.NumStr | 4 | 2 | (neg ? 1 : 0))];
		}

		var lead0 = num.StartsWith("0.", StringComparison.Ordinal);
		if (lead0)
		{
			num = num[2..];
		}

		var tail0 = num.EndsWith(".0", StringComparison.Ordinal);
		if (tail0)
		{
			num = num[..^2];
		}

		var buf = new byte[1 + num.Length / 2 + 1];
		buf[0] = (byte)((byte)JBMType.NumStr | (lead0 ? 4 : 0) | (tail0 ? 2 : 0) | (neg ? 1 : 0));
		var bufPos = 1;

		var firstNibble = true;
		byte buildByte = 0;

		foreach (var c in num)
		{
			byte nibble = c switch
			{
				'0' => 0x0,
				'1' => 0x1,
				'2' => 0x2,
				'3' => 0x3,
				'4' => 0x4,
				'5' => 0x5,
				'6' => 0x6,
				'7' => 0x7,
				'8' => 0x8,
				'9' => 0x9,
				'+' => 0xA,
				'-' => 0xB,
				'.' => 0xC,
				'e' => 0xD,
				'E' => 0xE,
				_ => throw new InvalidDataException(),
			};

			if (firstNibble)
			{
				buildByte = nibble;
			}
			else
			{
				buildByte = (byte)(buildByte << 4 | nibble);
				buf[bufPos++] = buildByte;
			}

			firstNibble = !firstNibble;
		}

		if (firstNibble)
		{
			buf[bufPos] = 0xFF;
		}
		else
		{
			buf[bufPos] = (byte)(buildByte << 4 | 0x0F);
		}

		return buf;
	}

	public void WriteNull() => Output.WriteByte((byte)JBMType.Null);
}