using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JsonBinMin
{
	internal class DictBuilder
	{
		private readonly MemoryStream mem = new();
		private readonly Dictionary<(string, DictElemKind), DictEntry> buildDict = new();
		private readonly JsonBinMinOptions options;

		public bool IsFinalized => Dict != null;

		public DictBuilder(JsonBinMinOptions options)
		{
			this.options = options;
		}

		public Dictionary<(string, DictElemKind), DictEntry>? Dict { get; private set; }
		public byte[]? DictSerialized { get; private set; }

		public void BuildDictionary(JsonElement elem)
		{
			CheckNotFinalized();

			switch (elem.ValueKind)
			{
				case JsonValueKind.Object:
					var objElemems = elem.EnumerateObject().ToArray();

					AddNumberToDict(objElemems.Length.ToString(CultureInfo.InvariantCulture));

					foreach (var kvp in objElemems)
					{
						AddStringToDict(kvp.Name);
						BuildDictionary(kvp.Value);
					}
					break;

				case JsonValueKind.Array:
					var arrElemems = elem.EnumerateArray().ToArray();

					AddNumberToDict(arrElemems.Length.ToString(CultureInfo.InvariantCulture));

					foreach (var arrItem in arrElemems)
					{
						BuildDictionary(arrItem);
					}
					break;

				case JsonValueKind.String:
					AddStringToDict(elem.GetString());
					break;

				case JsonValueKind.Number:
					AddNumberToDict(elem.GetRawText());
					break;

				default:
					break;
			}
		}

		private void AddNumberToDict(string num)
		{
			if (byte.TryParse(num, out var byteValue) && byteValue is >= 0 and <= 0x1F)
				return;

			mem.SetLength(0);
			CompressCtx.WriteNumberValue(num, mem, options);
			mem.Position = 0;
			var binary = mem.ToArray();

			if (!buildDict.TryGetValue((num, DictElemKind.Number), out var de))
				buildDict[(num, DictElemKind.Number)] = de = new DictEntry(binary);
			de.Count++;
		}

		private void AddStringToDict(string? str)
		{
			if (string.IsNullOrEmpty(str)) return;

			AddNumberToDict(str.Length.ToString(CultureInfo.InvariantCulture));

			mem.SetLength(0);
			CompressCtx.WriteStringValue(str, mem, options);
			mem.Position = 0;
			var binary = mem.ToArray();

			if (!buildDict.TryGetValue((str, DictElemKind.String), out var de))
				buildDict[(str, DictElemKind.String)] = de = new DictEntry(binary);
			de.Count++;
		}

		private void CheckNotFinalized()
		{
			if (IsFinalized)
				throw new InvalidOperationException();
		}

		public void FinalizeDictionary()
		{
			if (IsFinalized)
				return;

			if (buildDict.Values.All(x => x.Count <= 1))
			{
				Dict = new();
				DictSerialized = Array.Empty<byte>();
				return;
			}

			var dictValues = buildDict
				.Where(x => x.Value.Count > 1)
				.OrderByDescending(x => x.Value.Count * x.Value.Data.Length)
				.Take(0x7F)
				.OrderBy(x => x.Key.Item2)
				.ToArray();

			mem.SetLength(0);
			mem.WriteByte((byte)JBMType.MetaDictDef);
			CompressCtx.WriteNumberValue(dictValues.Length.ToString(CultureInfo.InvariantCulture), mem, options);

			Dict = new();
			for (int i = 0; i < dictValues.Length; i++)
			{
				var (k, v) = dictValues[i];
				v.Index = i;
				Dict.Add(k, v);
				mem.Write(v.Data);
			}
			mem.Position = 0;

			DictSerialized = mem.ToArray();
		}
	}

	internal enum DictElemKind
	{
		Number, // Do not move! Number needs to lower than other values
		String,
	}

	[DebuggerDisplay("{Count, nq} @{Index, nq}")]
	internal class DictEntry
	{
		public byte[] Data { get; set; }
		public int Count { get; set; } = 0;
		public int Index { get; set; } = 0;

		public DictEntry(byte[] data)
		{
			Data = data;
		}
	}
}
