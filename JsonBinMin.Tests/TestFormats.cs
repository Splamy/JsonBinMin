using System;
using System.Globalization;
using JsonBinMin.Formatting;

namespace JsonBinMin.Tests;

[TestClass]
public class TestFormats
{
	private const double Num = 2.9802322387695312E-08; 
	
	[TestMethod]
	public void TestFormat()
	{
		var res = Number.FormatFloatNet10(Num, null, NumberFormatInfo.InvariantInfo);
	}
	
	[TestMethod]
	public void TestFormatTS()
	{
		var res = Num.ToString();
		Console.WriteLine(res);
	}
	
}