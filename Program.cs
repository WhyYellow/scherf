// See https://aka.ms/new-console-template for more information
using Scherf.CorrectieTool;
using System.Diagnostics;

Console.WriteLine("Running...");
var package = new FileInfo(@"D:\test.dsop");
CorrectNamingConventionHandler correctTextBlockHandler = new CorrectNamingConventionHandler(package);
Console.WriteLine("Updating...");
correctTextBlockHandler.Update();
string output = correctTextBlockHandler.ExportOutput();
Process.Start("notepad.exe", output);
Console.WriteLine("Done...");
Console.ReadKey();

