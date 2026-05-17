// See https://aka.ms/new-console-template for more information
using JsonBinMin;
using JsonBinMin.Analysis;

Console.WriteLine("Analyzing!");

foreach (var file in Directory.GetFiles("Assets", "*.json"))
{
	var data = File.ReadAllBytes(file);
	var comp = JbmConverter.Encode(data);
	var analysis = JbmAnalyzer.Analyze(comp);
	Console.WriteLine("{0}: {1}", Path.GetFileName(file), analysis);
}

//StaticAnalysis.Run();