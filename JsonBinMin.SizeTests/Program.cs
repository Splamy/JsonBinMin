// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonBinMin;
using JsonBinMin.Aos;

var opt = new JsonSerializerOptions { WriteIndented = false };

var testData = """
{
	"_version": "2.0.0",
	"_songName": "speedcore/exratone what is this",
	"_songSubName": "",
	"_songAuthorName": "me lol",
	"_levelAuthorName": "Dasher3992",
	"_beatsPerMinute": 350,
	"_shuffle": 0,
	"_shufflePeriod": 0.5,
	"_previewStartTime": 12,
	"_previewDuration": 10,
	"_songFilename": "speedcore.egg",
	"_coverImageFilename": "paper.jpg",
	"_environmentName": "DefaultEnvironment",
	"_songTimeOffset": 0,
	"_customData": {
		"_contributors": [],
		"_editors": {
			"_lastEditedBy": "MMA2",
			"MMA2": {
				"version": "4.8.1"
			}
		}
	},
	"_difficultyBeatmapSets": [
		{
			"_beatmapCharacteristicName": "Standard",
			"_difficultyBeatmaps": [
				{
					"_difficulty": "ExpertPlus",
					"_difficultyRank": 9,
					"_beatmapFilename": "ExpertPlusStandard.dat",
					"_noteJumpMovementSpeed": 22,
					"_noteJumpStartBeatOffset": 0,
					"_customData": {
						"_difficultyLabel": "brrrrrrrrrr",
						"_editorOffset": 0,
						"_editorOldOffset": 0,
						"_warnings": [],
						"_information": [],
						"_suggestions": [],
						"_requirements": []
					}
				}
			]
		}
	]
}
""";
var testJson = JsonNode.Parse(testData).AsObject();


if (true)
{
	var sw = Stopwatch.StartNew();

	var file1 = File.ReadAllBytes(@"E:\maps\3918e\ExpertPlusStandard.dat");
	var json = JsonSerializer.Deserialize<JsonNode>(file1);
	Track("Deser");
	var aos = AosConverter.Encode(json);
	Track("Encode");
	var serText = aos.ToJsonString(opt);
	File.WriteAllText("split.json", serText);
	Track("Ser");
	var tmpSer = JsonSerializer.Deserialize<AosData<JsonElement>>(serText)!;
	Track("Deser");
	var aosNodeRt = AosConverter.Decode(tmpSer);
	Track("Decode");

	var basic = JsonSerializer.SerializeToUtf8Bytes(json, opt);
	Console.WriteLine("Basic           : {0}", basic.Length);
	var basicJBM = JBMConverter.Encode(basic);
	Console.WriteLine("Basic JBM       : {0}", basicJBM.Length);
	var basicBrotli = CompressBrotli(basic);
	Console.WriteLine("Basic Brotli    : {0}", basicBrotli.Length);
	var basicJBMbrotli = CompressBrotli(basicJBM);
	Console.WriteLine("Basic JBM Brotli: {0}", basicJBMbrotli.Length);

	//var aos = AosEncoder.EncodeAosObject(json);

	var split = JsonSerializer.SerializeToUtf8Bytes(aos, opt);
	Console.WriteLine("Split           : {0}", split.Length);
	var splitJBM = JBMConverter.Encode(split);
	Console.WriteLine("Split JBM       : {0}", splitJBM.Length);
	var splitBrotli = CompressBrotli(split);
	Console.WriteLine("Split Brotli    : {0}", splitBrotli.Length);
	var splitJBMbrotli = CompressBrotli(splitJBM);
	Console.WriteLine("Split JBM Brotli: {0}", splitJBMbrotli.Length);

	void Track(string text) { Console.WriteLine("{0}: {1}", text, sw.ElapsedMilliseconds); sw.Restart(); }
}

//return;

object fileLock = new object();
using var resultsFs = File.Open("results.csv", FileMode.Create, FileAccess.Write, FileShare.Read);
using var resultsSw = new StreamWriter(resultsFs);

resultsSw.WriteLine(
	"Id" +
	",Basic" +
	",Basic JBM" +
	",Basic Brotli" +
	",Basic JBM Brotli" +
	",Basic Zstd" +
	",Basic JBM Zstd" +
	",Split" +
	",Split JBM" +
	",Split Brotli" +
	",Split JBM Brotli");
resultsSw.Flush();


Parallel.ForEach(Directory.EnumerateFiles(@"E:\maps", "ExpertPlusStandard.dat", SearchOption.AllDirectories), new ParallelOptions() { MaxDegreeOfParallelism = 1 }, file => {
	Console.WriteLine("Gen {0}", file);

	try
	{
		using var fs = File.OpenRead(file);

		var json = JsonSerializer.Deserialize<JsonNode>(fs);

		var sw = Stopwatch.StartNew();
		void Track(string text) { Console.WriteLine("{0}: {1}", text, sw.ElapsedMilliseconds); sw.Restart(); }

		var basic = JsonSerializer.SerializeToUtf8Bytes(json, opt);
		Track("Basic");
		var basicJBM = JBMConverter.Encode(basic, new JBMOptions() { UseDict = UseDict.Off, UseFloats = UseFloats.None });
		Track("Basic JBM");
		var basicBrotli = CompressBrotli(basic);
		Track("Basic Brotli");
		var basicJBMbrotli = CompressBrotli(basicJBM);
		Track("Basic JBM Brotli");

		var aos = AosConverter.Encode(json);

		var split = JsonSerializer.SerializeToUtf8Bytes(aos, opt);
		Track("Split");
		var splitJBM = JBMConverter.Encode(split, new JBMOptions() { UseDict = UseDict.Off, UseFloats = UseFloats.None });
		Track("Split JBM");
		var splitBrotli = CompressBrotli(split);
		Track("Split Brotli");
		var splitJBMbrotli = CompressBrotli(splitJBM);
		Track("Split JBM Brotli");

		lock (fileLock)
		{
			resultsSw.WriteLine(
				$"{new FileInfo(file).Directory.Name}" +
				$",{basic.Length}" +
				$",{basicJBM.Length}" +
				$",{basicBrotli.Length}" +
				$",{basicJBMbrotli.Length}" +
				$",{split.Length}" +
				$",{splitJBM.Length}" +
				$",{splitBrotli.Length}" +
				$",{splitJBMbrotli.Length}");
			resultsSw.Flush();
		}
	}
	catch (Exception ex)
	{
		lock (fileLock)
		{
			resultsSw.WriteLine($"{new FileInfo(file).Directory.Name}, FAIL");
			resultsSw.Flush();
		}
	}
});


static ReadOnlyMemory<byte> CompressBrotli(byte[] input)
{
	var maxOutputSize = BrotliEncoder.GetMaxCompressedLength(input.Length);
	var output = new byte[maxOutputSize];

	try
	{
		BrotliEncoder.TryCompress(input, output, out var written, 11, 24);
		return output.AsMemory(0, written);
	}
	catch
	{
		return ReadOnlyMemory<byte>.Empty;
	}
}
