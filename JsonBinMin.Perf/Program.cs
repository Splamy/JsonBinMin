using System;
using System.Diagnostics;
using System.IO;

namespace JsonBinMin.Perf;

public class Program
{
	public static void Main(string[] args)
	{
		TestPerf();
	}

	public static void TestPerf()
	{
		var data = File.ReadAllText(Path.Combine("Assets", "test_02.json"));
		var sw = new Stopwatch();
		for (int i = 0; i < 20; i++)
		{
			sw.Restart();
			var comp = JBMConverter.Encode(data);
			var round = JBMConverter.DecodeToString(comp);
			Console.Write(round[0..1]);
			Console.WriteLine("{0:00} in {1}", i, sw.Elapsed.TotalSeconds);
		}
	}
}
