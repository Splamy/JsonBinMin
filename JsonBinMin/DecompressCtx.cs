using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace JsonBinMin
{
	public class DecompressCtx
	{
		public JsonBinMinOptions Options { get; } = JsonBinMinOptions.Default;
		public StringBuilder Output { get; set; } = new();
		public string[] Dict { get; set; } = Array.Empty<string>();

		public bool Parse(Span<byte> data, out Span<byte> rest)
		{
			var pick = data[0];
			if ((pick & 0x80) != 0)
			{
				Output.Append(Dict[pick & 0x7F]);
				rest = data[1..];
				return false;
			}

			switch ((JBMType)(pick & 0b0_111_0000))
			{
				case JBMType.Object:
					var objElemCount = int.Parse(ReadNumber(data, out data), NumberStyles.Integer, CultureInfo.InvariantCulture);
					Output.Append('{');
					for (int i = 0; i < objElemCount; i++)
					{
						Parse(data, out data);
						Output.Append(':');
						Parse(data, out data);
						if (i < objElemCount - 1) Output.Append(',');
					}
					Output.Append('}');
					rest = data;
					break;

				case JBMType.Array:
					var arrElemCount = int.Parse(ReadNumber(data, out data), NumberStyles.Integer, CultureInfo.InvariantCulture);
					Output.Append('[');
					for (int i = 0; i < arrElemCount; i++)
					{
						Parse(data, out data);
						if (i < arrElemCount - 1) Output.Append(',');
					}
					Output.Append(']');
					rest = data;
					break;

				case JBMType.String:
					Output.Append(ReadString(data, out rest));
					break;

				case JBMType._Constants:
					switch ((JBMType)(pick & 0b0_111_1111))
					{
						case JBMType.False:
							Output.Append("false");
							rest = data[1..];
							break;

						case JBMType.True:
							Output.Append("true");
							rest = data[1..];
							break;

						case JBMType.Null:
							Output.Append("null");
							rest = data[1..];
							break;

						case JBMType.MetaDictDef:
							var dictSize = byte.Parse(ReadNumber(data[1..], out data), NumberStyles.Integer, CultureInfo.InvariantCulture);
							Dict = new string[dictSize];
							var dictCtx = new DecompressCtx
							{
								Dict = Dict // allow in-self referential dict entries
							};
							for (int i = 0; i < dictSize; i++)
							{
								dictCtx.Output.Clear();
								dictCtx.Parse(data, out data);
								Dict[i] = dictCtx.Output.ToString();
							}
							rest = data;
							return true;

						default:
							throw new InvalidOperationException();
					}
					break;

				case JBMType.IntInline:
				case JBMType._NumSized:
				case JBMType.NumStr:
					Output.Append(ReadNumber(data, out rest));
					break;

				default:
					throw new InvalidOperationException();
			}
			return false;
		}

		// reads with PickByte
		public string ReadNumber(Span<byte> data, out Span<byte> rest)
		{
			var pick = (byte)(data[0] & 0b0_111_1111);
			var numPick = (JBMType)(pick & 0b0_111_0000);

			switch (numPick)
			{
				case JBMType.Object:
				case JBMType.Array:
				case JBMType.String:
				case JBMType.IntInline:
					var hVal = (data[0] & 0xF);
					if (hVal < 15 || numPick == JBMType.IntInline)
					{
						rest = data[1..];
						return hVal.ToString(CultureInfo.InvariantCulture);
					}
					return ReadNumber(data[1..], out rest);

				case JBMType._NumSized:
					static string FlagStr(string s, byte i) => (i & 1) == 0 ? s : "-" + s;

					switch ((JBMType)(pick & 0b0_111_111_0))
					{
						case JBMType.Int8:
							rest = data[2..];
							return FlagStr(data[1].ToString(CultureInfo.InvariantCulture), pick);
						case JBMType.Int16:
							rest = data[3..];
							return FlagStr(BinaryPrimitives.ReadUInt16LittleEndian(data[1..]).ToString(CultureInfo.InvariantCulture), pick);
						case JBMType.Int32:
							rest = data[5..];
							return FlagStr(BinaryPrimitives.ReadUInt32LittleEndian(data[1..]).ToString(CultureInfo.InvariantCulture), pick);
						case JBMType.Int64:
							rest = data[9..];
							return FlagStr(BinaryPrimitives.ReadUInt64LittleEndian(data[1..]).ToString(CultureInfo.InvariantCulture), pick);
						case JBMType.IntRle:
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
							return FlagStr(intAcc.ToString(CultureInfo.InvariantCulture), pick);

						case JBMType.Float32:
							rest = data[5..];
							return BitConverter.ToSingle(data[1..]).ToString(CultureInfo.InvariantCulture);
						case JBMType.Float64:
							rest = data[9..];
							return BitConverter.ToDouble(data[1..]).ToString(CultureInfo.InvariantCulture);

						default:
							throw new InvalidOperationException();
					}

				case JBMType.NumStr:
					int nsOff = 1;
					var strb = new StringBuilder();

					if ((pick & 0b0000_0001) != 0) strb.Append('-');
					if ((pick & 0b0000_0010) != 0) strb.Append("0.");

					while (true)
					{
						var b = data[nsOff++];

						if (Get((byte)(b >> 4)) is { } bFirst) strb.Append(bFirst);
						else break;
						if (Get((byte)(b & 0xF)) is { } bSecond) strb.Append(bSecond);
						else break;

						static char? Get(byte val) => val switch
						{
							>= 0 and <= 9 => (char)('0' + val),
							0xA => '+',
							0xB => '-',
							0xC => '.',
							0xD => 'e',
							0xE => 'E',
							0xF => null,
							_ => throw new InvalidCastException(),
						};
					}
					rest = data[nsOff..];
					return strb.ToString();

				default:
					throw new InvalidOperationException();
			}
		}

		// reads without PickByte
		public string ReadString(Span<byte> data, out Span<byte> rest)
		{
			var numStr = ReadNumber(data, out data);
			var strLen = int.Parse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture);
			var str = '"' + JsonEncodedText.Encode(Encoding.UTF8.GetString(data[..strLen])).ToString() + '"';
			rest = data[strLen..];
			return str;
		}
	}
}
