using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace JsonBinMin
{
	internal class CompressCtx
	{
		private static readonly Encoding Utf8Encoder = new UTF8Encoding(false, true);
		public readonly JBMOptions options;
		public readonly MemoryStream output = new();
		public readonly Dictionary<(string, DictElemKind), DictEntry> dict;

		public CompressCtx(JBMOptions options, DictBuilder dictBuilder)
		{
			this.options = options;
			dictBuilder.FinalizeDictionary();
			this.dict = dictBuilder.Dict ?? throw new ArgumentNullException(nameof(dictBuilder));
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

					if (objElemems.Length < 0xF)
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

					if (arrElemems.Length < 0xF)
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
					break;
			}
		}

		public bool TryWriteValueFromDict(JsonElement elem)
		{
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
			if (!options.UseDict || !dict.TryGetValue((str, DictElemKind.String), out var entry))
				return false;
			output.WriteByte((byte)(0x80 | entry.Index));
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

			if (ulong.TryParse(posNum, out var integerVal))
			{
				switch (integerVal)
				{
					case <= 0x1F when !numNeg:
						output.WriteByte((byte)((byte)JBMType.IntInline | integerVal));
						return;
					case <= byte.MaxValue:
						{
							Span<byte> buf = stackalloc byte[2] { FlagType(JBMType.Int8, numNeg), (byte)integerVal };
							output.Write(buf);
							return;
						}
					case <= ushort.MaxValue:
						{
							Span<byte> buf = stackalloc byte[3];
							buf[0] = FlagType(JBMType.Int16, numNeg);
							BinaryPrimitives.WriteUInt16LittleEndian(buf[1..], (ushort)integerVal);
							output.Write(buf);
							return;
						}
					case <= (1UL << 24):
						{
							Span<byte> buf = stackalloc byte[4];
							buf[0] = FlagType(JBMType.Int24, numNeg);
							BinaryPrimitives.WriteUInt16LittleEndian(buf[1..], (ushort)(integerVal & 0xFFFF));
							buf[3] = (byte)((integerVal >> 16) & 0xFF);
							output.Write(buf);
							return;
						}
					case <= uint.MaxValue:
						{
							Span<byte> buf = stackalloc byte[5];
							buf[0] = FlagType(JBMType.Int32, numNeg);
							BinaryPrimitives.WriteUInt32LittleEndian(buf[1..], (uint)integerVal);
							output.Write(buf);
							return;
						}
					case <= (1UL << 48):
						{
							Span<byte> buf = stackalloc byte[7];
							buf[0] = FlagType(JBMType.Int48, numNeg);
							BinaryPrimitives.WriteUInt32LittleEndian(buf[1..], (uint)(integerVal & 0xFFFFFFFF));
							BinaryPrimitives.WriteUInt16LittleEndian(buf[5..], (ushort)((integerVal >> 32) & 0xFFFF));
							output.Write(buf);
							return;
						}
					case <= ulong.MaxValue:
						{
							Span<byte> buf = stackalloc byte[9];
							buf[0] = FlagType(JBMType.Int64, numNeg);
							BinaryPrimitives.WriteUInt64LittleEndian(buf[1..], (ulong)integerVal);
							output.Write(buf);
							return;
						}

					default:
						throw new InvalidOperationException();
				}
			}

			var rle = TryGetNumRle(posNum);
			var rleLen = rle?.Length ?? 0;
			var numStr = GetNumStr(num);
			var numStrLen = numStr.Length;

			if (rle != null) // number is an integer
			{
				if (rleLen < numStrLen)
					output.Write(rle);
				else
					output.Write(numStr);
				return;
			}

#if NET5_0_OR_GREATER
			if (options.UseHalfType &&
				Half.TryParse(num,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var halfVal)
				&& halfVal.ToString(CultureInfo.InvariantCulture) == num
				&& numStrLen >= 3)
			{
				Span<byte> buf = stackalloc byte[3];
				buf[0] = (byte)JBMType.Float16;
				BinaryPrimitives.WriteUInt16LittleEndian(buf[1..], unchecked((ushort)halfVal));
				output.Write(buf);
				return;
			}
#endif
			if (float.TryParse(num,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var floatVal)
				&& floatVal.ToString(CultureInfo.InvariantCulture) == num
				&& numStrLen >= 5)
			{
				Span<byte> buf = stackalloc byte[5];
				buf[0] = (byte)JBMType.Float32;
				if (!BitConverter.TryWriteBytes(buf[1..], floatVal)) throw new InvalidOperationException();
				output.Write(buf);
				return;
			}

			if (double.TryParse(num,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var doubleVal)
				&& doubleVal.ToString(CultureInfo.InvariantCulture) == num
				&& numStrLen >= 9)
			{
				Span<byte> buf = stackalloc byte[9];
				buf[0] = (byte)JBMType.Float64;
				if (!BitConverter.TryWriteBytes(buf[1..], doubleVal)) throw new InvalidOperationException();
				output.Write(buf);
				return;
			}

			output.Write(numStr);
		}

		public bool TryWriteNumberFromDict(string num)
		{
			if (!options.UseDict || !dict.TryGetValue((num, DictElemKind.Number), out var entry))
				return false;
			output.WriteByte((byte)(0x80 | entry.Index));
			return true;
		}

		private static byte FlagType(JBMType t, bool n) => n ? (byte)((int)t | 1) : (byte)t;

		public static byte[]? TryGetNumRle(ReadOnlySpan<char> num)
		{
			if (!BigInteger.TryParse(num, out var bi))
				return null;

			var buf = new List<byte>();
			bool neg = false;
			if (bi < 0)
			{
				neg = true;
				bi = -bi;
			}

			while (bi > 0)
			{
				var b = (byte)(bi & 0x7F);
				bi >>= 7;
				buf.Add(b);
			}
			for (int i = 1; i < buf.Count; i++) buf[i] |= 0x80;
			buf.Reverse();
			buf.Insert(0, FlagType(JBMType.IntRle, neg));
			return buf.ToArray();
		}

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

			bool frac = num.StartsWith("0.", StringComparison.Ordinal);
			if (frac) num = num[2..];

			var buf = new byte[1 + (num.Length / 2) + 1];
			buf[0] = (byte)((byte)JBMType.NumStr | (frac ? 2 : 0) | (neg ? 1 : 0));
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
}
