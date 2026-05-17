using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace JsonBinMin.BinV1;

internal class DictBuilder(JbmOptions options)
{
	private const int MaxDictEntries = 0x7F;
	
	private readonly Dictionary<string, DictEntry> _buildDictNum = [];
	private readonly Dictionary<string, DictEntry> _buildDictStr = [];

	public bool IsFinalized { get; private set; } = false;

	public byte[]? DictSerialized { get; private set; }

	public void BuildDictionary(JsonElement elem)
	{
		CheckNotFinalized();

		switch (elem.ValueKind)
		{
			case JsonValueKind.Object:
				var propCount = 0;
				foreach (var kvp in elem.EnumerateObject())
				{
					AddStringToDict(kvp.Name);
					BuildDictionary(kvp.Value);
					propCount++;
				}

				AddNumberToDict(propCount.ToString(CultureInfo.InvariantCulture));
				break;

			case JsonValueKind.Array:
				var arrCount = 0;
				foreach (var arrItem in elem.EnumerateArray())
				{
					BuildDictionary(arrItem);
					arrCount++;
				}

				AddNumberToDict(arrCount.ToString(CultureInfo.InvariantCulture));
				break;

			case JsonValueKind.String:
				AddStringToDict(elem.GetString());
				break;

			case JsonValueKind.Number:
				AddNumberToDict(elem.GetRawText());
				break;
		}
	}

	private void AddNumberToDict(string num)
	{
		if (num.Length <= 3 && byte.TryParse(num, out var byteValue) && byteValue is >= 0 and <= 0x1F)
		{
			return;
		}

		ref var de = ref CollectionsMarshal.GetValueRefOrAddDefault(_buildDictNum, num, out var exists);

		if (exists)
		{
			de.Count++;
		}
		else
		{
			scoped var list = new ValueListBuilder<byte>(stackalloc byte[JbmEncoder.ScratchBufferSize]);
			JbmEncoder.WriteNumberValue(num, ref list, options);
			de = new DictEntry(list.AsSpan().ToArray()) { Count = 1, };
			list.Dispose();
		}
	}

	private void AddStringToDict(string? str)
	{
		if (string.IsNullOrEmpty(str))
		{
			return;
		}

		ref var de = ref CollectionsMarshal.GetValueRefOrAddDefault(_buildDictStr, str, out var exists);
		if (exists)
		{
			de.Count++;
		}
		else
		{
			scoped var list = new ValueListBuilder<byte>(stackalloc byte[JbmEncoder.ScratchBufferSize]);
			JbmEncoder.WriteStringValue(str, ref list, options);
			de = new DictEntry(list.AsSpan().ToArray()) { Count = 1, };
			list.Dispose();
		}
	}

	[Conditional("DEBUG")]
	private void CheckNotFinalized()
	{
		if (IsFinalized)
		{
			throw new InvalidOperationException();
		}
	}

	public void FinalizeDictionary()
	{
		if (IsFinalized)
		{
			return;
		}

		IsFinalized = true;

		var dictValues = Enumerable.Empty<(string Key, DictEntry Entry, DictElemKind Kind)>()
			.Concat(_buildDictNum.Select(x => (x.Key, Entry: x.Value, Kind: DictElemKind.Number)))
			.Concat(_buildDictStr.Select(x => (x.Key, Entry: x.Value, Kind: DictElemKind.String)))
			.Where(x => x.Entry.Count * x.Entry.Data.Length > x.Entry.Count + x.Entry.Data.Length)
			.OrderByDescending(x => x.Entry.Count * x.Entry.Data.Length)
			.Take(MaxDictEntries)
			.OrderBy(x => x.Kind)
			.Select(x => x.Entry)
			.ToArray();

		if (dictValues.Length == 0)
		{
			DictSerialized = [];
			return;
		}

		scoped var list = new ValueListBuilder<byte>(1024);
		list.Append((byte)JbmType.MetaDictDef);
		Trace.Assert(dictValues.Length <= MaxDictEntries);
		JbmEncoder.WriteNumberValue(dictValues.Length.ToString(CultureInfo.InvariantCulture), ref list, options);

		for (var i = 0; i < dictValues.Length; i++)
		{
			ref var entry = ref dictValues[i];
			entry.Index = (byte)i;
			list.Append(entry.Data);
		}

		DictSerialized = list.AsSpan().ToArray();
		list.Dispose();
	}

	[Conditional("DEBUG")]
	private void CheckFinalized()
	{
		if (!IsFinalized)
		{
			throw new InvalidOperationException();
		}
	}

	public bool TryGetString(string key, [MaybeNullWhen(false)] out DictEntry entry)
	{
		CheckFinalized();
		return _buildDictStr.TryGetValue(key, out entry);
	}

	public bool TryGetNumber(string key, [MaybeNullWhen(false)] out DictEntry entry)
	{
		CheckFinalized();
		return _buildDictNum.TryGetValue(key, out entry);
	}

	internal enum DictElemKind
	{
		Number, // Do not move! Number needs to lower than other values
		String,
	}
}

[DebuggerDisplay("{Count, nq} @{Index, nq}")]
internal class DictEntry(byte[] data)
{
	public byte[] Data { get; } = data;
	public int Count { get; set; } = 0;
	public byte Index { get; set; } = byte.MaxValue;
	public bool IsIndexed => Index != byte.MaxValue;
}