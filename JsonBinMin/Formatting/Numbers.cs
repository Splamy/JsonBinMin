using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace JsonBinMin.Formatting;

internal static partial class Number
{
	private const int CharStackBufferSize = 32;
	private const int DefaultPrecisionExponentialFormat = 6;

	public static unsafe string FormatFloatNet10<TNumber>(TNumber value, string? format, NumberFormatInfo info)
		where TNumber : unmanaged, IFloatingPointIeee754<TNumber>
	{
		var vlb = new ValueListBuilder<char>(stackalloc char[CharStackBufferSize]);
		string result = FormatFloatNet10(ref vlb, value, format, info) ?? vlb.AsSpan().ToString();
		vlb.Dispose();
		return result;
	}


	/// <summary>Formats the specified value according to the specified format and info.</summary>
	/// <returns>
	/// Non-null if an existing string can be returned, in which case the builder will be unmodified.
	/// Null if no existing string was returned, in which case the formatted output is in the builder.
	/// </returns>
	private static unsafe string? FormatFloatNet10<TNumber, TChar>(ref ValueListBuilder<TChar> vlb, TNumber value,
		ReadOnlySpan<char> format, NumberFormatInfo info)
		where TNumber : unmanaged, IFloatingPointIeee754<TNumber>
	// where TChar : unmanaged, IUtfChar<TChar>
	{
		Debug.Assert(typeof(TChar) == typeof(char) || typeof(TChar) == typeof(byte));

		if (!TNumber.IsFinite(value))
		{
			throw new NotSupportedException("Only finite floating point values are supported.");
		}

		char fmt = '\0';

		byte* pDigits = stackalloc byte[Hack<TNumber>.NumberBufferLength];
		var precision = Hack<TNumber>.MaxPrecisionCustomFormat;

		NumberBuffer number =
			new NumberBuffer(NumberBufferKind.FloatingPoint, pDigits, Hack<TNumber>.NumberBufferLength);
		number.IsNegative = TNumber.IsNegative(value);

		// We need to track the original precision requested since some formats
		// accept values like 0 and others may require additional fixups.
		int nMaxDigits = GetFloatingPointMaxDigitsAndPrecision(fmt, ref precision, info, out bool isSignificantDigits);

		if ((value != default) && (!isSignificantDigits || !Grisu3.TryRun(value, precision, ref number)))
		{
			Dragon4(value, precision, isSignificantDigits, ref number);
		}

		number.CheckConsistency();

		if (fmt != 0)
		{
			if (precision == -1)
			{
				Debug.Assert((fmt == 'G') || (fmt == 'g') || (fmt == 'R') || (fmt == 'r'));

				// For the roundtrip and general format specifiers, when returning the shortest roundtrippable
				// string, we need to update the maximum number of digits to be the greater of number.DigitsCount
				// or SinglePrecision. This ensures that we continue returning "pretty" strings for values with
				// less digits. One example this fixes is "-60", which would otherwise be formatted as "-6E+01"
				// since DigitsCount would be 1 and the formatter would almost immediately switch to scientific notation.

				nMaxDigits = Math.Max(number.DigitsCount, Hack<TNumber>.MaxRoundTripDigits);
			}

			// NumberToString(ref vlb, ref number, fmt, nMaxDigits, info);
			Console.WriteLine("Format: {0}, Precision: {1}, MaxDigits: {2}", fmt, precision, nMaxDigits);
		}
		else
		{
			Debug.Assert(precision == Hack<TNumber>.MaxPrecisionCustomFormat);
			// NumberToStringFormat(ref vlb, ref number, format, info);
			Console.WriteLine("Custom Format: {0}, Precision: {1}", format.ToString(), precision);
		}

		return null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int GetFloatingPointMaxDigitsAndPrecision(char fmt, ref int precision, NumberFormatInfo info,
		out bool isSignificantDigits)
	{
		// We want to fast path the common case of no format and general format + precision.
		// These are commonly encountered and the full switch is otherwise large enough to show up in hot path profiles

		if (fmt == 0)
		{
			isSignificantDigits = true;
			return precision;
		}

		// Bitwise-or with space (' ') converts any uppercase character to
		// lowercase and keeps unsupported characters as something unsupported.
		fmt |= ' ';

		if (fmt == 'g')
		{
			// The general format uses the precision specifier to indicate the number of significant
			// digits to format. This defaults to the shortest roundtrippable string. Additionally,
			// given that we can't return zero significant digits, we treat 0 as returning the shortest
			// roundtrippable string as well.

			isSignificantDigits = true;

			if (precision == 0)
			{
				precision = -1;
				return 0;
			}

			return precision;
		}

		return Slow(fmt, ref precision, info, out isSignificantDigits);

		static int Slow(char fmt, ref int precision, NumberFormatInfo info, out bool isSignificantDigits)
		{
			int maxDigits = precision;

			switch (fmt)
			{
				case 'c':
				{
					// The currency format uses the precision specifier to indicate the number of
					// decimal digits to format. This defaults to NumberFormatInfo.CurrencyDecimalDigits.

					if (precision == -1)
					{
						precision = info.CurrencyDecimalDigits;
					}

					isSignificantDigits = false;

					break;
				}

				case 'e':
				{
					// The exponential format uses the precision specifier to indicate the number of
					// decimal digits to format. This defaults to 6. However, the exponential format
					// also always formats a single integral digit, so we need to increase the precision
					// specifier and treat it as the number of significant digits to account for this.

					if (precision == -1)
					{
						precision = DefaultPrecisionExponentialFormat;
					}

					precision++;
					isSignificantDigits = true;

					break;
				}

				case 'f':
				case 'n':
				{
					// The fixed-point and number formats use the precision specifier to indicate the number
					// of decimal digits to format. This defaults to NumberFormatInfo.NumberDecimalDigits.

					if (precision == -1)
					{
						precision = info.NumberDecimalDigits;
					}

					isSignificantDigits = false;

					break;
				}

				case 'p':
				{
					// The percent format uses the precision specifier to indicate the number of
					// decimal digits to format. This defaults to NumberFormatInfo.PercentDecimalDigits.
					// However, the percent format also always multiplies the number by 100, so we need
					// to increase the precision specifier to ensure we get the appropriate number of digits.

					if (precision == -1)
					{
						precision = info.PercentDecimalDigits;
					}

					precision += 2;
					isSignificantDigits = false;

					break;
				}

				case 'r':
				{
					// The roundtrip format ignores the precision specifier and always returns the shortest
					// roundtrippable string.

					precision = -1;
					isSignificantDigits = true;

					break;
				}

				default:
				{
					throw new FormatException($"The format specifier '{fmt}' is invalid.");
					goto case 'r'; // unreachable
				}
			}

			return maxDigits;
		}
	}

	private static ulong ExtractFractionAndBiasedExponent<TNumber>(TNumber value, out int exponent)
		where TNumber : unmanaged, IFloatingPointIeee754<TNumber>
	{
		ulong bits = Hack<TNumber>.FloatToBits(value);
		ulong fraction = (bits & Hack<TNumber>.DenormalMantissaMask);
		exponent = ((int)(bits >> Hack<TNumber>.DenormalMantissaBits) & Hack<TNumber>.InfinityExponent);

		if (exponent != 0)
		{
			// For normalized value,
			// value = 1.fraction * 2^(exp - ExponentBias)
			//       = (1 + mantissa / 2^TrailingSignificandLength) * 2^(exp - ExponentBias)
			//       = (2^TrailingSignificandLength + mantissa) * 2^(exp - ExponentBias - TrailingSignificandLength)
			//
			// So f = (2^TrailingSignificandLength + mantissa), e = exp - ExponentBias - TrailingSignificandLength;

			fraction |= (1UL << Hack<TNumber>.DenormalMantissaBits);
			exponent -= Hack<TNumber>.ExponentBias + Hack<TNumber>.DenormalMantissaBits;
		}
		else
		{
			// For denormalized value,
			// value = 0.fraction * 2^(MinBinaryExponent)
			//       = (mantissa / 2^TrailingSignificandLength) * 2^(MinBinaryExponent)
			//       = mantissa * 2^(MinBinaryExponent - TrailingSignificandLength)
			//       = mantissa * 2^(MinBinaryExponent - TrailingSignificandLength)
			// So f = mantissa, e = MinBinaryExponent - TrailingSignificandLength
			exponent = Hack<TNumber>.MinBinaryExponent - Hack<TNumber>.DenormalMantissaBits;
		}

		return fraction;
	}
}

internal static class Hack<TNumber>
{
	public static ushort ExponentBias => typeof(TNumber) == typeof(float) ? (ushort)127 : (ushort)1023;
	public static short MinExponent => typeof(TNumber) == typeof(float) ? (short)-126 : (short)-1022;
	public static short MaxExponent => typeof(TNumber) == typeof(float) ? (short)+127 : (short)+1023;
	public static int MinBinaryExponent => 1 - MaxExponent;
	public static ushort DenormalMantissaBits => typeof(TNumber) == typeof(float) ? (ushort)23 : (ushort)52;

	public static ulong DenormalMantissaMask =>
		typeof(TNumber) == typeof(float) ? 0x007F_FFFFul : 0x000F_FFFF_FFFF_FFFFul;

	public static int NumberBufferLength => typeof(TNumber) == typeof(float) ? 112 + 1 + 1 : 767 + 1 + 1;
	public static int MaxPrecisionCustomFormat => typeof(TNumber) == typeof(float) ? 7 : 15;
	public static int MaxRoundTripDigits => typeof(TNumber) == typeof(float) ? 9 : 17;
	public static int InfinityExponent => typeof(TNumber) == typeof(float) ? 0xFF : 0x7FF;

	public static ulong FloatToBits(TNumber value) => typeof(TNumber) == typeof(float)
		? BitConverter.SingleToUInt32Bits((float)(object)value!)
		: BitConverter.DoubleToUInt64Bits((double)(object)value!);
}

internal ref partial struct ValueListBuilder<T>
{
	private Span<T> _span;
	private T[]? _arrayFromPool;
	private int _pos;

	public ValueListBuilder(Span<T?> scratchBuffer)
	{
		_span = scratchBuffer!;
	}

	public ValueListBuilder(int capacity)
	{
		Grow(capacity);
	}

	public int Length {
		get => _pos;
		set {
			Debug.Assert(value >= 0);
			Debug.Assert(value <= _span.Length);
			_pos = value;
		}
	}

	public ref T this[int index] {
		get {
			Debug.Assert(index < _pos);
			return ref _span[index];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Append(T item)
	{
		int pos = _pos;

		// Workaround for https://github.com/dotnet/runtime/issues/72004
		Span<T> span = _span;
		if ((uint)pos < (uint)span.Length)
		{
			span[pos] = item;
			_pos = pos + 1;
		}
		else
		{
			AddWithResize(item);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Append(scoped ReadOnlySpan<T> source)
	{
		int pos = _pos;
		Span<T> span = _span;
		if (source.Length == 1 && (uint)pos < (uint)span.Length)
		{
			span[pos] = source[0];
			_pos = pos + 1;
		}
		else
		{
			AppendMultiChar(source);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AppendMultiChar(scoped ReadOnlySpan<T> source)
	{
		if ((uint)(_pos + source.Length) > (uint)_span.Length)
		{
			Grow(source.Length);
		}

		source.CopyTo(_span.Slice(_pos));
		_pos += source.Length;
	}

	public void Insert(int index, scoped ReadOnlySpan<T> source)
	{
		Debug.Assert(index == 0, "Implementation currently only supports index == 0");

		if ((uint)(_pos + source.Length) > (uint)_span.Length)
		{
			Grow(source.Length);
		}

		_span.Slice(0, _pos).CopyTo(_span.Slice(source.Length));
		source.CopyTo(_span);
		_pos += source.Length;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Span<T> AppendSpan(int length)
	{
		Debug.Assert(length >= 0);

		int pos = _pos;
		Span<T> span = _span;
		if ((uint)(pos + length) <= (uint)span.Length)
		{
			_pos = pos + length;
			return span.Slice(pos, length);
		}
		else
		{
			return AppendSpanWithGrow(length);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private Span<T> AppendSpanWithGrow(int length)
	{
		int pos = _pos;
		Grow(length);
		_pos += length;
		return _span.Slice(pos, length);
	}

	// Hide uncommon path
	[MethodImpl(MethodImplOptions.NoInlining)]
	private void AddWithResize(T item)
	{
		Debug.Assert(_pos == _span.Length);
		int pos = _pos;
		Grow(1);
		_span[pos] = item;
		_pos = pos + 1;
	}

	public ReadOnlySpan<T> AsSpan()
	{
		return _span.Slice(0, _pos);
	}

	public bool TryCopyTo(Span<T> destination, out int itemsWritten)
	{
		if (_span.Slice(0, _pos).TryCopyTo(destination))
		{
			itemsWritten = _pos;
			return true;
		}

		itemsWritten = 0;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Dispose()
	{
		int pos = _pos;
		T[]? toReturn = _arrayFromPool;

		this = default;

		if (toReturn != null)
		{
#if SYSTEM_PRIVATE_CORELIB
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    ArrayPool<T>.Shared.Return(toReturn, pos);
                }
                else
                {
                    ArrayPool<T>.Shared.Return(toReturn);
                }
#else
			if (!typeof(T).IsPrimitive)
			{
				Array.Clear(toReturn, 0, pos);
			}

			ArrayPool<T>.Shared.Return(toReturn);
#endif
		}
	}

	/// <summary>
	/// Resize the internal buffer either by doubling current buffer size or
	/// by adding <paramref name="additionalCapacityBeyondPos"/> to
	/// <see cref="_pos"/> whichever is greater.
	/// </summary>
	/// <param name="additionalCapacityBeyondPos">
	/// Number of chars requested beyond current position.
	/// </param>
	/// <remarks>
	/// Note that consuming implementations depend on the list only growing if it's absolutely
	/// required.  If the list is already large enough to hold the additional items be added,
	/// it must not grow. The list is used in a number of places where the reference is checked
	/// and it's expected to match the initial reference provided to the constructor if that
	/// span was sufficiently large.
	/// </remarks>
	private void Grow(int additionalCapacityBeyondPos)
	{
		Debug.Assert(additionalCapacityBeyondPos > 0);
		Debug.Assert(_pos > _span.Length - additionalCapacityBeyondPos,
			"Grow called incorrectly, no resize is needed.");

		const int ArrayMaxLength = 0x7FFFFFC7; // same as Array.MaxLength

		// Double the size of the span.  If it's currently empty, default to size 4,
		// although it'll be increased in Rent to the pool's minimum bucket size.
		int nextCapacity = Math.Max(_span.Length != 0 ? _span.Length * 2 : 4, _pos + additionalCapacityBeyondPos);

		// If the computed doubled capacity exceeds the possible length of an array, then we
		// want to downgrade to either the maximum array length if that's large enough to hold
		// an additional item, or the current length + 1 if it's larger than the max length, in
		// which case it'll result in an OOM when calling Rent below.  In the exceedingly rare
		// case where _span.Length is already int.MaxValue (in which case it couldn't be a managed
		// array), just use that same value again and let it OOM in Rent as well.
		if ((uint)nextCapacity > ArrayMaxLength)
		{
			nextCapacity = Math.Max(Math.Max(_span.Length + 1, ArrayMaxLength), _span.Length);
		}

		T[] array = ArrayPool<T>.Shared.Rent(nextCapacity);
		_span.CopyTo(array);

		T[]? toReturn = _arrayFromPool;
		_span = _arrayFromPool = array;
		if (toReturn != null)
		{
#if SYSTEM_PRIVATE_CORELIB
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    ArrayPool<T>.Shared.Return(toReturn, _pos);
                }
                else
                {
                    ArrayPool<T>.Shared.Return(toReturn);
                }
#else
			if (!typeof(T).IsPrimitive)
			{
				Array.Clear(toReturn, 0, _pos);
			}

			ArrayPool<T>.Shared.Return(toReturn);
#endif
		}
	}
}