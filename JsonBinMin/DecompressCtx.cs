using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace JsonBinMin
{
	internal class DecompressCtx
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

						case JBMType.Float16:
						case JBMType.Float32:
						case JBMType.Float64:
							ReadNumber(Output, data, out rest);
							break;

						case JBMType.MetaDictDef:
							var dictSize = ReadNumberToInt(data[1..], out data);
							Dict = new byte[dictSize][];
							var dictCtx = new DecompressCtx
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
					if ((pick & 0b0000_0010) != 0) output.Write(Constants.Fraction);

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
					rest = data[nsOff..];
					return;
			}

			switch ((JBMType)pick)
			{
				case JBMType.Float16:
					rest = data[3..];
					throw new NotImplementedException();
				case JBMType.Float32:
					{
						Span<byte> buf = stackalloc byte[Constants.MaximumFormatSingleLength];
						var val = BitConverter.ToSingle(data[1..]);
						Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
						output.Write(buf[..written]);
						rest = data[5..];
						return;
					}
				case JBMType.Float64:
					{
						Span<byte> buf = stackalloc byte[Constants.MaximumFormatDoubleLength];
						var val = BitConverter.ToDouble(data[1..]);
						Util.Assert(Utf8Formatter.TryFormat(val, buf, out var written));
						output.Write(buf[..written]);
						rest = data[9..];
						return;
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

			throw new InvalidOperationException();
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

		// reads without PickByte
		public void ReadString(Stream output, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
		{
			var strLen = ReadNumberToInt(data, out data);
			output.WriteByte((byte)'"');
			output.Write(JsonEncodedText.Encode(data[..strLen]).EncodedUtf8Bytes);
			output.WriteByte((byte)'"');
			rest = data[strLen..];
		}
	}
}
