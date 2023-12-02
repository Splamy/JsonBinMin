using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace JsonBinMin;

internal class JBMDecoder
{
	public JBMOptions Options { get; } = JBMOptions.Default;
	public MemoryStream Output { get; set; } = new();
	public byte[][] Dict { get; set; } = Array.Empty<byte[]>();
	private readonly MemoryStream mem = new();

	public bool Parse(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var pick = data[0];
		if ((pick & 0x80) != 0)
		{
			Output.Write(Dict[pick & 0x7F]);
			rest = data[1..];
			return false;
		}

		if ((JBMType)(pick & 0b1_11_00000) == 0) // IntInline
		{
			ReadNumber(Output, data, out rest);
			return false;
		}

		switch ((JBMType)(pick & 0b1_111_0000))
		{
		case JBMType.Object:
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

		case JBMType.Array:
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

		case JBMType.String:
			ReadString(Output, data, out rest);
			break;

		case JBMType._Block101:
			switch ((JBMType)(pick & 0b1_111_11_00))
			{
			case JBMType.Float16:
			case JBMType.Float32:
			case JBMType.Float64:
				ReadNumber(Output, data, out rest);
				break;

			case JBMType._Block101:
				switch ((JBMType)pick)
				{
				case JBMType.False:
					Output.Write(Constants.False);
					rest = data[1..];
					break;

				case JBMType.True:
					Output.Write(Constants.True);
					rest = data[1..];
					break;

				case JBMType.Null:
					Output.Write(Constants.Null);
					rest = data[1..];
					break;

				case JBMType.MetaDictDef:
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

				case JBMType.Compressed:
					throw new Exception("Mid jbm compression is not supported");

				default:
					throw new InvalidDataException();
				}
				break;

			default:
				throw new InvalidDataException();
			}
			break;

		case JBMType._Block110:
		case JBMType.NumStr:
			ReadNumber(Output, data, out rest);
			break;

		default:
			throw new InvalidDataException();
		}
		return false;
	}

	public static MemoryStream Decompress(ReadOnlySpan<byte> data)
	{
		var mem = new MemoryStream(Math.Max(8192, data.Length * 2));
		var bd = new BrotliDecoder();
		Span<byte> buffer = stackalloc byte[8192];
		while (true)
		{
			var res = bd.Decompress(data, buffer, out var read, out var written);
			data = data[read..];
			mem.Write(buffer[..written]);

			switch (res)
			{
			case System.Buffers.OperationStatus.DestinationTooSmall:
				continue;
			case System.Buffers.OperationStatus.Done:
				return mem;
			case System.Buffers.OperationStatus.InvalidData:
				throw new Exception("InvalidData");
			case System.Buffers.OperationStatus.NeedMoreData:
				throw new Exception("NeedMoreData");
			}
		}
	}

	// reads with PickByte
	public static void ReadNumber(Stream output, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var pick = data[0];

		if ((JBMType)(pick & 0b1_11_00000) == 0) // IntInline
		{
			var hVal = (data[0] & 0x1F);
			rest = data[1..];
			Span<byte> buf = stackalloc byte[2];
			Util.Assert(Utf8Formatter.TryFormat(hVal, buf, out var written));
			output.Write(buf[..written]);
			return;
		}

		switch ((JBMType)(pick & 0b1_111_0000))
		{
		case JBMType.Object:
		case JBMType.Array:
		case JBMType.String:
			var hVal = (data[0] & 0xF);
			if (hVal < 0xF)
			{
				rest = data[1..];
				Span<byte> buf = stackalloc byte[2];
				Util.Assert(Utf8Formatter.TryFormat(hVal, buf, out var written));
				output.Write(buf[..written]);
				return;
			}
			ReadNumber(output, data[1..], out rest);
			return;

		case JBMType.NumStr:
			int nsOff = 1;
			var strb = new StringBuilder();

			if ((pick & 0b0000_0001) != 0) output.WriteByte((byte)'-');
			if ((pick & 0b0000_0100) != 0) output.Write(Constants.Leading0);

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

			if ((pick & 0b0000_0010) != 0) output.Write(Constants.Tailing0);

			rest = data[nsOff..];
			return;
		}

		if ((pick & 0b1_111_00_0_0) == 0b0_101_00_0_0 && (pick & 0b0_000_11_0_0) != 0)
		{
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
#if NET5_0_OR_GREATER
					Span<byte> buf = stackalloc byte[Constants.MaximumFormatSingleLength];
					// https://source.dot.net/#System.Private.CoreLib/BitConverter.cs,573
					var val = Unsafe.ReadUnaligned<Half>(ref MemoryMarshal.GetReference(data[1..]));
					var written = Encoding.UTF8.GetBytes(val.ToString(CultureInfo.InvariantCulture), buf);
					SetE(buf[..written], upperE ? 'E' : 'e');
					output.Write(buf[..written]);
					if (tail0) output.Write(Constants.Tailing0);
#endif
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
			}
		}

		switch ((JBMType)(pick & 0b1_111_111_0))
		{
		case JBMType.Int8:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt8Length];
				var val = data[1];
				Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
				output.Write(buf[..written]);
				rest = data[2..];
				return;
			}
		case JBMType.Int16:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt16Length];
				var val = BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
				Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
				output.Write(buf[..written]);
				rest = data[3..];
				return;
			}
		case JBMType.Int24:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt24Length];
				var valLow = (uint)BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
				var valHigh = (uint)data[3];
				var val = valHigh << 16 | valLow;
				Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
				output.Write(buf[..written]);
				rest = data[4..];
				return;
			}
		case JBMType.Int32:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt32Length];
				var val = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
				Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
				output.Write(buf[..written]);
				rest = data[5..];
				return;
			}
		case JBMType.Int48:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt48Length];
				var valLow = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
				var valHigh = (ulong)BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
				var val = valHigh << 32 | valLow;
				Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
				output.Write(buf[..written]);
				rest = data[7..];
				return;
			}
		case JBMType.Int64:
			{
				WriteSignByFlag(output, pick);
				Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt64Length];
				var val = BinaryPrimitives.ReadUInt64LittleEndian(data[1..]);
				Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
				output.Write(buf[..written]);
				rest = data[9..];
				return;
			}
		case JBMType.IntRle:
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
				WriteSignByFlag(output, pick);
				foreach (var c in intAcc.ToString(CultureInfo.InvariantCulture))
					output.WriteByte((byte)c);
				rest = data[rleOff..];
				return;
			}
		}

		throw new InvalidDataException();
	}

	private int ReadNumberToInt(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		mem.SetLength(0);
		ReadNumber(mem, data, out rest);
		Span<byte> buf = stackalloc byte[Constants.MaximumFormatUInt32Length];
		mem.Position = 0;
		mem.Read(buf[..(int)mem.Length]);
		if (!Utf8Parser.TryParse(buf, out int val, out _))
			throw new InvalidDataException();
		return val;
	}

	private static void WriteSignByFlag(Stream output, byte i)
	{
		if ((i & 1) != 0)
			output.WriteByte((byte)'-');
	}

	private static readonly Encoding Utf8Encoder = new UTF8Encoding(false, false);
	// reads with PickByte
	public void ReadString(Stream output, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		var strLen = ReadNumberToInt(data, out data);
		output.WriteByte((byte)'"');

		int bytesRead = 0;
		if (strLen > 0)
		{
			var decoder = Utf8Encoder.GetDecoder();

			var charBuf = new char[strLen];
			decoder.Convert(data, charBuf, true, out bytesRead, out var charsRead, out _);

			if (charsRead != strLen)
				throw new InvalidDataException();

			output.Write(JsonEncodedText.Encode(charBuf).EncodedUtf8Bytes);
		}

		output.WriteByte((byte)'"');
		rest = data[bytesRead..];
	}
}
