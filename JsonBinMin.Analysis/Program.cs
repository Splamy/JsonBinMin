// See https://aka.ms/new-console-template for more information
using JsonBinMin;
using JsonBinMin.Analysis;

Console.WriteLine("Analyzing!");

foreach (var file in Directory.GetFiles("Assets", "*.json"))
{
	var data = File.ReadAllBytes(file);
	var comp = JBMConverter.Encode(data);
	var analysis = JBMAnalyzer.Analyze(comp);
	Console.WriteLine("{0}: {1}", Path.GetFileName(file), analysis);
}

//StaticAnalysis.Run();