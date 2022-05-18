// See https://aka.ms/new-console-template for more information
using Scherf.CorrectieTool;
using System.Diagnostics;

Console.WriteLine("Running...");

string[] packages = new string[] { "E1000 Uitnodigen en Uitstel doen van aangifte.dsop", "E2000 Voorlopige aanslag.dsop", "E3000 Verzoek om informatie (aanslagregeling).dsop",
    "E4000 Correctiebrief.dsop", "E6000 Bezwaar.dsop", "E7000 Vooroverleg.dsop", "E8000 Opvraag uit archief.dsop", "E9000 Diversen.dsop", "S1000 Uitnodigen en Uitstel doen van aangifte.dsop",
    "S2000 Voorlopige aanslag.dsop", "S3000 Verzoek om informatie (aanslagregeling).dsop" };

foreach (var filename in packages)
{
    var package = new FileInfo(String.Format(@"D:\test\{0}", filename));
    CorrectNamingConventionHandler correctTextBlockHandler = new CorrectNamingConventionHandler(package);
    Console.WriteLine("Updating...");
    correctTextBlockHandler.Update();
    string output = correctTextBlockHandler.ExportOutput();
    Process.Start("notepad.exe", output);
}

Console.WriteLine("Done...");

