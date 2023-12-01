using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace JsonBinMin;

internal class JBMEncoder
{
	private static readonly Encoding Utf8Encoder = new UTF8Encoding(false, true);
	public readonly JBMOptions options;
	public readonly MemoryStream output = new();
	public readonly DictBuilder dict;

	public JBMEncoder(JBMOptions options, DictBuilder dictBuilder)
	{
		this.options = options;
		dictBuilder.FinalizeDictionary();
		dict = dictBuilder;
		output.Write(dictBuilder.DictSerialized);
	}

	public void WriteValue(JsonElement elem)
	{
		if (TryWriteValueFromDict(elem))
		{
			return;
		}

		switch (elem.ValueKind)
		{
		case JsonValueKind.Undefined:
			throw new InvalidOperationException();

		case JsonValueKind.Object:
			var objElemems = elem.EnumerateObject().ToArray();

			if (objElemems.Length <= Constants.SqueezedInlineMaxValue)
			{
				output.WriteByte((byte)((byte)JBMType.Object | objElemems.Length));
			}
			else
			{
				output.WriteByte((byte)JBMType.ObjectExt);
				WriteNumberValue(objElemems.Length.ToString(CultureInfo.InvariantCulture));
			}

			foreach (var kvp in objElemems)
			{
				WriteStringValue(kvp.Name);
				WriteValue(kvp.Value);
			}
			break;

		case JsonValueKind.Array:
			var arrElemems = elem.EnumerateArray().ToArray();

			if (arrElemems.Length <= Constants.SqueezedInlineMaxValue)
			{
				output.WriteByte((byte)((byte)JBMType.Array | arrElemems.Length));
			}
			else
			{
				output.WriteByte((byte)JBMType.ArrayExt);
				WriteNumberValue(arrElemems.Length.ToString(CultureInfo.InvariantCulture));
			}

			foreach (var arrItem in arrElemems)
			{
				WriteValue(arrItem);
			}
			break;

		case JsonValueKind.String:
			WriteStringValue(elem.GetString());
			break;

		case JsonValueKind.Number:
			WriteNumberValue(elem.GetRawText());
			break;

		case JsonValueKind.True:
			output.WriteByte((byte)JBMType.True);
			break;

		case JsonValueKind.False:
			output.WriteByte((byte)JBMType.False);
			break;

		case JsonValueKind.Null:
			WriteNull();
			break;

		default:
			throw new InvalidOperationException();
		}
	}

	public bool TryWriteValueFromDict(JsonElement elem)
	{
		if (options.UseDict == UseDict.Deep && dict.TryGetDeepEntry(elem, out var entry) && entry.IsIndexed)
		{
			output.WriteByte((byte)(0x80 | entry.Index));
			return true;
		}

		return false;
	}

	public void WriteStringValue(string? str)
	{
		if (str is null)
		{
			WriteNull();
			return;
		}

		if (TryWriteStringFromDict(str))
			return;

		WriteStringValue(str, output, options);
	}

	public static void WriteStringValue(string str, Stream output, JBMOptions options)
	{
		if (str.Length < 0xF)
		{
			output.WriteByte((byte)((byte)JBMType.String | str.Length));
		}
		else
		{
			output.WriteByte((byte)JBMType.StringExt);
			WriteNumberValue(str.Length.ToString(CultureInfo.InvariantCulture), output, options);
		}

		var bytes = Utf8Encoder.GetBytes(str);
		output.Write(bytes);
	}

	public bool TryWriteStringFromDict(string str)
	{
		if (options.UseDict == UseDict.Off || !dict.TryGetString(str, out var entry))
			return false;
		if (entry.IsIndexed)
			output.WriteByte((byte)(0x80 | entry.Index));
		else
			output.Write(entry.Data);
		return true;
	}

	public void WriteNumberValue(string num)
	{
		if (TryWriteNumberFromDict(num))
			return;

		WriteNumberValue(num, output, options);
	}

	public static void WriteNumberValue(string numRaw, Stream output, JBMOptions options)
	{
		var num = numRaw.AsSpan();
		var numNeg = num.StartsWith("-");
		var posNum = numNeg ? num[1..] : num;

		if (ulong.TryParse(posNum, NumberStyles.None, CultureInfo.InvariantCulture, out var integerVal))
		{
			switch (integerVal)
			{
			case <= Constants.JbmIntInlineMaxValue when !numNeg:
				output.WriteByte((byte)((byte)JBMType.IntInline | integerVal));
				return;
			case <= Constants.JbmInt8MaxValue:
				{
					ReadOnlySpan<byte> buf = [GetWithNegativeFlag(JBMType.Int8, numNeg), (byte)(integerVal - Constants.JbmInt8Offset)];
					output.Write(buf);
					return;
				}
			case <= Constants.JbmInt16MaxValue:
				{
					Span<byte> buf = stackalloc byte[3];
					buf[0] = GetWithNegativeFlag(JBMType.Int16, numNeg);
					BinaryPrimitives.WriteUInt16LittleEndian(buf[1..], (ushort)(integerVal - Constants.JbmInt16Offset));
					output.Write(buf);
					return;
				}
			case <= Constants.JbmInt24MaxValue:
				{
					Span<byte> buf = stackalloc byte[4];
					buf[0] = GetWithNegativeFlag(JBMType.Int24, numNeg);
					var offsetVal = integerVal - Constants.JbmInt24Offset;
					BinaryPrimitives.WriteUInt16LittleEndian(buf[1..], (ushort)(offsetVal & 0xFFFF));
					buf[3] = (byte)((offsetVal >> 16) & 0xFF);
					output.Write(buf);
					return;
				}
			case <= Constants.JbmInt32MaxValue:
				{
					Span<byte> buf = stackalloc byte[5];
					buf[0] = GetWithNegativeFlag(JBMType.Int32, numNeg);
					BinaryPrimitives.WriteUInt32LittleEndian(buf[1..], (uint)(integerVal - Constants.JbmInt32Offset));
					output.Write(buf);
					return;
				}
			case <= Constants.JbmInt48MaxValue:
				{
					Span<byte> buf = stackalloc byte[7];
					buf[0] = GetWithNegativeFlag(JBMType.Int48, numNeg);
					var offsetVal = integerVal - Constants.JbmInt48Offset;
					BinaryPrimitives.WriteUInt32LittleEndian(buf[1..], (uint)(offsetVal & 0xFFFFFFFF));
					BinaryPrimitives.WriteUInt16LittleEndian(buf[5..], (ushort)((offsetVal >> 32) & 0xFFFF));
					output.Write(buf);
					return;
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
				return;
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

			return;
		}

		var numStr = GetNumStr(num);
		var numStrLen = numStr.Length;

		var upperE = num.Contains("E", StringComparison.Ordinal);
		var tail0 = num.EndsWith(".0", StringComparison.Ordinal);
		if (tail0) num = num[..^2];

		if (numStrLen >= 3
			&& options.UseFloats.HasFlag(UseFloats.Half)
			&& Half.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfVal)
			&& num.Equals(halfVal.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
		{
			Span<byte> buf = stackalloc byte[3];
			buf[0] = GetWithFloatFlags(JBMType.Float16, tail0, upperE);
			Util.Assert(BitConverter.TryWriteBytes(buf[1..], halfVal));
			output.Write(buf);
			return;
		}

		if (numStrLen >= 5
			&& options.UseFloats.HasFlag(UseFloats.Single)
			&& float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal)
			&& num.Equals(floatVal.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
		{
			Span<byte> buf = stackalloc byte[5];
			buf[0] = GetWithFloatFlags(JBMType.Float32, tail0, upperE);
			Util.Assert(BitConverter.TryWriteBytes(buf[1..], floatVal));
			output.Write(buf);
			return;
		}

		if (numStrLen >= 9
			&& options.UseFloats.HasFlag(UseFloats.Double)
			&& double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal)
			&& num.Equals(doubleVal.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
		{
			Span<byte> buf = stackalloc byte[9];
			buf[0] = GetWithFloatFlags(JBMType.Float64, tail0, upperE);
			Util.Assert(BitConverter.TryWriteBytes(buf[1..], doubleVal));
			output.Write(buf);
			return;
		}
		output.Write(numStr);
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

		for (int i = 1; i < buf.Count; i++) buf[i] |= 0x80;
		buf.Reverse();

		CollectionsMarshal.AsSpan(buf).CopyTo(data);
		written = buf.Count;
		return true;
	}

	public bool TryWriteNumberFromDict(string num)
	{
		if (options.UseDict == UseDict.Off || !dict.TryGetNumber(num, out var entry))
			return false;
		if (entry.IsIndexed)
			output.WriteByte((byte)(0x80 | entry.Index));
		else
			output.Write(entry.Data);
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

		bool neg = num.StartsWith("-");
		if (neg) num = num[1..];

		if (num is ['0', '.', '0'])
		{
			return [(byte)((byte)JBMType.NumStr | 4 | 2 | (neg ? 1 : 0))];
		}

		bool lead0 = num.StartsWith("0.", StringComparison.Ordinal);
		if (lead0) num = num[2..];

		bool tail0 = num.EndsWith(".0", StringComparison.Ordinal);
		if (tail0) num = num[..^2];

		var buf = new byte[1 + (num.Length / 2) + 1];
		buf[0] = (byte)((byte)JBMType.NumStr | (lead0 ? 4 : 0) | (tail0 ? 2 : 0) | (neg ? 1 : 0));
		int bufPos = 1;

		bool firstNibble = true;
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
				buildByte = (byte)((buildByte << 4) | nibble);
				buf[bufPos++] = buildByte;
			}
			firstNibble = !firstNibble;
		}

		if (firstNibble)
			buf[bufPos] = 0xFF;
		else
			buf[bufPos] = (byte)((buildByte << 4) | 0x0F);

		return buf;
	}

	public void WriteNull() => output.WriteByte((byte)JBMType.Null);
}
