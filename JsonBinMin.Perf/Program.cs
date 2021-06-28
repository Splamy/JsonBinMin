using System;
using System.IO;

namespace JsonBinMin.Perf
{
	public class Program
	{
		public static void Main(string[] args)
		{
			TestPerf();
		}

		public static void TestPerf()
		{
			var data = File.ReadAllText(Path.Combine("Assets", "test_02.json"));
			for (int i = 0; i < 20; i++)
			{
				var comp = JsonBinMin.Compress(data);
				var round = JsonBinMin.DecompressToString(comp);
				Console.Write(round.Substring(0, 0));
				Console.WriteLine(i);
			}
		}
	}
}
