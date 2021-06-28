using System;
using System.Globalization;
using System.IO;

namespace JsonBinMin.Perf
{
	class Program
	{
		static void Main(string[] args)
		{
			TestPerf();
		}

		static void TestPerf()
		{
			var data = File.ReadAllText(Path.Combine("Assets", "test_02.json"));
			for (int i = 0; i < 20; i++)
			{
				var comp = JsonBinMin.Compress(data);
				var round = JsonBinMin.Decompress(comp);
				Console.Write(round.Substring(0, 0));
				Console.WriteLine(i);
			}
		}

		static void TestIntSizes()
		{
			var mem = new MemoryStream(128);

			var cnt = new int[6];

			for (int i = 0; i < int.MaxValue; i++)
			{
				mem.SetLength(0);
				CompressCtx.WriteNumberValue(i.ToString(CultureInfo.InvariantCulture), mem, JsonBinMinOptions.Default);
				cnt[mem.Length]++;

				if ((i & 0xFFFFF) == 0)
				{
					Console.WriteLine("===>{1,10}", 0, i);
					for (int j = 0; j < cnt.Length; j++)
					{
						Console.WriteLine("{0,2}: {1,10}", j, cnt[j]);
					}
				}
			}

			Console.ReadKey();
		}
	}
}
