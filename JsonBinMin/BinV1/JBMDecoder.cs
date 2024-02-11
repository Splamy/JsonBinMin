using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using JsonBinMin.BinV1;

namespace JsonBinMin;

internal partial class JBMDecoder
{
	public JBMOptions Options { get; } = JBMOptions.Default;
	public MemoryStream Output { get; set; } = new();
	public byte[][] Dict { get; set; } = [];
	private readonly MemoryStream mem = new();

	public bool Parse(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var pick = data[0];
		if (pick > 0x7F) // (pick & 0x80) != 0
		{
			Output.Write(Dict[pick & 0x7F]);
			rest = data[1..];
			return false;
		}

		switch (DecodeMap[pick])
		{
		case DecodePoint.IntInline:
			ReadInlineNumber(Output, data, out rest);
			break;

		case DecodePoint.DObject:
			var objElemCount = ReadNumberToInt(data, out data);
			Output.WriteByte((byte)'{');
			for (int i = 0; i < objElemCount; i++)
			{
				Parse(data, out data);
				Output.WriteByte((byte)':');
				Parse(data, out data);
				if (i < objElemCount - 1) Output.WriteByte((byte)',');
			}
			Output.WriteByte((byte)'}');
			rest = data;
			break;

		case DecodePoint.DArray:
			var arrElemCount = ReadNumberToInt(data, out data);
			Output.WriteByte((byte)'[');
			for (int i = 0; i < arrElemCount; i++)
			{
				Parse(data, out data);
				if (i < arrElemCount - 1) Output.WriteByte((byte)',');
			}
			Output.WriteByte((byte)']');
			rest = data;
			break;


		case DecodePoint.DString:
			ReadString(Output, data, out rest);
			break;

		case DecodePoint.Block101:
			ReadBlock101(Output, data, out rest);
			break;

		case DecodePoint.Block110:
			ReadBlock110(Output, data, out rest);
			break;

		case DecodePoint.NumStr:
			ReadNumStr(Output, data, out rest);
			break;

		case DecodePoint.False:
			Output.Write(Constants.False);
			rest = data[1..];
			break;

		case DecodePoint.True:
			Output.Write(Constants.True);
			rest = data[1..];
			break;

		case DecodePoint.Null:
			Output.Write(Constants.Null);
			rest = data[1..];
			break;

		case DecodePoint.MetaDictDef:
			var dictSize = ReadNumberToInt(data[1..], out data);
			Dict = new byte[dictSize][];
			var dictCtx = new JBMDecoder
			{
				Dict = Dict // allow in-self referential dict entries
			};
			for (int i = 0; i < dictSize; i++)
			{
				dictCtx.Output.SetLength(0);
				dictCtx.Parse(data, out data);
				Dict[i] = dictCtx.Output.ToArray();
			}
			rest = data;
			return true;

		default:
			throw new InvalidDataException();
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ReadInlineNumber(Stream output, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var hVal = (data[0] & 0x1F);
		rest = data[1..];
		Span<byte> buf = stackalloc byte[2];
		Util.Assert(Utf8Formatter.TryFormat(hVal, buf, out var written));
		output.Write(buf[..written]);
		return;
	}


	public static void ReadNumStr(Stream output, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var pick = data[0];

		var isNeg = (pick & 0b0000_0001) != 0;
		var tail0 = (pick & 0b0000_0010) != 0;
		var lead0 = (pick & 0b0000_0100) != 0;

		if (isNeg) output.WriteByte((byte)'-');
		if (lead0 && tail0)
		{
			output.Write(Constants.Float0);
			rest = data[1..];
			return;
		}
		if (lead0) output.Write(Constants.Leading0);

		int nsOff = 1;
		while (true)
		{
			var b = data[nsOff++];

			if (Get((byte)(b >> 4)) is { } bFirst) output.WriteByte(bFirst);
			else break;
			if (Get((byte)(b & 0xF)) is { } bSecond) output.WriteByte(bSecond);
			else break;

			static byte? Get(byte val) => val switch
			{
				>= 0 and <= 9 => (byte)('0' + val),
				0xA => (byte)'+',
				0xB => (byte)'-',
				0xC => (byte)'.',
				0xD => (byte)'e',
				0xE => (byte)'E',
				0xF => null,
				_ => throw new InvalidDataException(),
			};
		}

		if (tail0) output.Write(Constants.Tailing0);

		rest = data[nsOff..];
	}

	public static void ReadBlock101(Stream output, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var pick = data[0];

		var upperE = (pick & 1) != 0;
		var tail0 = (pick & 2) != 0;
		static void SetE(Span<byte> span, char e)
		{
			for (int i = 0; i < span.Length; i++)
			{
				if (span[i] == 'e' || span[i] == 'E')
					span[i] = (byte)e;
			}
		}

		switch ((JBMType)(pick & 0b1_111_11_0_0))
		{
		case JBMType.Float16:
			{
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatSingleLength];
				var val = BitConverter.ToHalf(data[1..]);
				var written = Encoding.UTF8.GetBytes(val.ToString(CultureInfo.InvariantCulture), buf);
				SetE(buf[..written], upperE ? 'E' : 'e');
				output.Write(buf[..written]);
				if (tail0) output.Write(Constants.Tailing0);
				rest = data[3..];
				return;
			}
		case JBMType.Float32:
			{
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatSingleLength];
				var val = BitConverter.ToSingle(data[1..]);
				Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
				SetE(buf[..written], upperE ? 'E' : 'e');
				output.Write(buf[..written]);
				if (tail0) output.Write(Constants.Tailing0);
				rest = data[5..];
				return;
			}
		case JBMType.Float64:
			{
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatDoubleLength];
				var val = BitConverter.ToDouble(data[1..]);
				Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
				SetE(buf[..written], upperE ? 'E' : 'e');
				output.Write(buf[..written]);
				if (tail0) output.Write(Constants.Tailing0);
				rest = data[9..];
				return;
			}
		default:
			throw new InvalidDataException();
		}
	}

	public static void ReadBlock110(Stream output, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var pick = data[0];

		switch ((JBMType)(pick & 0b1_111_111_0))
		{
		case JBMType.Int8:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt64Length];
				var val = data[1];
				Util.Assert(Utf8Formatter.TryFormat(val + Constants.JbmInt8Offset, buf, out var written));
				output.Write(buf[..written]);
				rest = data[2..];
				return;
			}
		case JBMType.Int16:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt64Length];
				var val = BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
				Util.Assert(Utf8Formatter.TryFormat(val + Constants.JbmInt16Offset, buf, out var written));
				output.Write(buf[..written]);
				rest = data[3..];
				return;
			}
		case JBMType.Int24:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt64Length];
				var valLow = (uint)BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
				var valHigh = (uint)data[3];
				var val = valHigh << 16 | valLow;
				Util.Assert(Utf8Formatter.TryFormat(val + Constants.JbmInt24Offset, buf, out var written));
				output.Write(buf[..written]);
				rest = data[4..];
				return;
			}
		case JBMType.Int32:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt64Length];
				var val = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
				Util.Assert(Utf8Formatter.TryFormat(val + Constants.JbmInt32Offset, buf, out var written));
				output.Write(buf[..written]);
				rest = data[5..];
				return;
			}
		case JBMType.Int48:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt64Length];
				var valLow = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
				var valHigh = (ulong)BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
				var val = valHigh << 32 | valLow;
				Util.Assert(Utf8Formatter.TryFormat(val + Constants.JbmInt48Offset, buf, out var written));
				output.Write(buf[..written]);
				rest = data[7..];
				return;
			}
		case JBMType.Int64:
			{
				WriteSignByFlag(output, pick);
				var val = (BigInteger)BinaryPrimitives.ReadUInt64LittleEndian(data[1..]);
				val += Constants.JbmInt64Offset;
				foreach (var c in val.ToString(CultureInfo.InvariantCulture))
					output.WriteByte((byte)c);
				rest = data[9..];
				return;
			}
		case JBMType.IntRle:
			{
				WriteSignByFlag(output, pick);
				var byteLen = (int)ReadRleNum(data, out data);
				var intAcc = new BigInteger(data[..byteLen], true);
				intAcc += Constants.JbmIntRleOffset;
				foreach (var c in intAcc.ToString(CultureInfo.InvariantCulture))
					output.WriteByte((byte)c);
				rest = data[byteLen..];
				return;
			}
		default:
			throw new InvalidDataException();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void WriteSignByFlag(Stream output, byte i)
		{
			if ((i & 1) != 0)
				output.WriteByte((byte)'-');
		}
	}

	private static BigInteger ReadRleNum(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		// [0XXX_XXXX] 1 byte
		// [1XXX_XXXX] [0XXX_XXXX] 2 byte ...
		int rleOff = 1;
		var intAcc = new BigInteger();
		do
		{
			intAcc <<= 7;
			intAcc |= data[rleOff] & 0b0111_1111;
		} while ((data[rleOff++] & 0x80) != 0);
		rest = data[rleOff..];
		return intAcc;
	}

	public uint ReadNumberToInt(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var pick = data[0];

		if (pick > 0x7F) // (pick & 0x80) != 0
		{
			Util.Assert(Utf8Parser.TryParse(Dict[pick & 0x7F], out uint num, out _));
			rest = data[1..];
			return num;
		}

		if ((JBMType)(pick & 0b1_11_00000) == 0) // IntInline
		{
			rest = data[1..];
			return (uint)(data[0] & 0x1F);
		}

		switch ((JBMType)(pick & 0b1_111_0000))
		{
		case JBMType.Object:
		case JBMType.Array:
		case JBMType.String:
			var hVal = (uint)(data[0] & 0xF);
			if (hVal < 0xF)
			{
				rest = data[1..];
				return hVal;
			}

			return ReadNumberToInt(data[1..], out rest);
		}

		if ((pick & 0b1_111_00_0_0) == 0b0_101_00_0_0 && (pick & 0b0_000_11_0_0) != 0)
		{
			throw new Exception("Can't read float value as integer");
		}

		switch ((JBMType)(pick & 0b1_111_111_0))
		{
		case JBMType.Int8:
			{
				CheckNotPositive(pick);
				rest = data[2..];
				return data[1] + Constants.JbmInt8Offset;
			}
		case JBMType.Int16:
			{
				CheckNotPositive(pick);
				rest = data[3..];
				return BinaryPrimitives.ReadUInt16LittleEndian(data[1..]) + Constants.JbmInt16Offset;
			}
		case JBMType.Int24:
			{
				CheckNotPositive(pick);
				rest = data[4..];
				var valLow = (uint)BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
				var valHigh = (uint)data[3];
				var val = valHigh << 16 | valLow;
				return val + Constants.JbmInt24Offset;
			}
		case JBMType.Int32:
			{
				CheckNotPositive(pick);
				rest = data[5..];
				return BinaryPrimitives.ReadUInt32LittleEndian(data[1..]) + Constants.JbmInt32Offset;
			}
		case JBMType.Int48:
		case JBMType.Int64:
		case JBMType.IntRle:
			throw new Exception($"Datatype is too big for int");
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void CheckNotPositive(byte i)
		{
			if ((i & 1) != 0)
				throw new Exception("Can't read negative value as length");
		}

		throw new InvalidDataException();
	}

	// reads with PickByte
	public void ReadString(Stream output, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var strLen = (int)ReadNumberToInt(data, out data);
		output.WriteByte((byte)'"');
		output.Write(JsonEncodedText.Encode(data[..strLen], Options.JsonSerializerOptions.Encoder).EncodedUtf8Bytes);
		output.WriteByte((byte)'"');
		rest = data[strLen..];
	}
}
