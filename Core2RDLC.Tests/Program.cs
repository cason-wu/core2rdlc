using System.Diagnostics;
using System.Xml.Linq;
using RdlcEditorLib;

var directory = Path.Combine(Path.GetTempPath(), "core2rdlc-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
var path = Path.Combine(directory, "report.rdlc");
var assembly = typeof(Fixtures.Customer).Assembly.Location;
var source = typeof(Fixtures.Customer).Assembly.GetName().Name!;
XNamespace ns = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";
string Report(string sets) => $"<Report xmlns='{ns}' xmlns:rd='http://schemas.microsoft.com/SQLServer/reporting/reportdesigner'><DataSources/><DataSets>{sets}</DataSets></Report>";
string Set(string name, string owner) => $"<DataSet Name='{name}'><Query><DataSourceName>{owner}</DataSourceName><CommandText/></Query><Fields><Field Name='Original'><DataField>Original</DataField></Field></Fields></DataSet>";
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(string[] names, string xml)
{
    File.WriteAllText(path, xml);
    var editor = new RdlcEditor(path);
    var before = editor.GetString();
    try { editor.AddDataSets(assembly, names); }
    catch (ArgumentException)
    {
        Check(editor.GetString() == before, "Rejected update changed report in memory");
        Check(File.ReadAllText(path) == xml, "Rejected update changed file");
        return;
    }
    throw new Exception("Expected rejection: " + string.Join(",", names));
}
int Cli(params string[] args) => RunCli(args).ExitCode;
CliResult RunCli(params string[] args)
{
    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
    { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
    start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Core2RDLC.dll"));
    foreach (var arg in args) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    process.StandardInput.Close();
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(15000))
    {
        process.Kill(entireProcessTree: true);
        throw new Exception("CLI waited for input in unattended mode: " + string.Join(" ", args));
    }
    Task.WaitAll(stdout, stderr);
    return new CliResult(process.ExitCode, stdout.Result, stderr.Result);
}

try
{
    var original = Report(Set("Customer", source) + Set("Order", source));
    Reject(["Custmer"], original);
    Reject(["Customer", "Missing"], original);
    Reject([], original);
    Reject([" "], original);
    Reject(["Duplicate"], original);
    Reject(["Sales.Duplicate", "Support.Duplicate"], original);
    Reject(["Customer"], Report(Set("Customer", "OtherAssembly")));
    var originalOrder = XDocument.Parse(original).Descendants(ns + "DataSet").Single(e => (string?)e.Attribute("Name") == "Order");
    File.WriteAllText(path, original);
    var editor = new RdlcEditor(path);
    editor.AddDataSets(assembly, [" Fixtures.Customer ", "Fixtures.Customer"]);
    editor.AddDataSets(assembly, ["Customer"]);
    editor.Save();
    var sets = XDocument.Load(path).Root!.Element(ns + "DataSets")!;
    Check(sets.Elements().Count() == 2, "Unexpected DataSet count");
    Check(XNode.DeepEquals(sets.Elements().Single(e => (string?)e.Attribute("Name") == "Order"), originalOrder), "Order changed");
    Check(sets.Elements().Single(e => (string?)e.Attribute("Name") == "Customer").Descendants(ns + "Field").Any(e => (string?)e.Attribute("Name") == "Id"), "Customer not updated");
    File.WriteAllText(path, original);
    Check(Cli("--help") == 0, "Help failed");
    Check(Cli(path, assembly, "Customer", "--typo") != 0, "Unknown option returned success");
    Check(Cli(path, assembly, "Missing") != 0, "Missing type returned success");
    Check(File.ReadAllText(path) == original, "Failed CLI changed file");
    Check(Cli(path, assembly, "Customer", "--preview") == 0, "Preview failed");
    Check(File.ReadAllText(path) == original, "Preview changed file");
    Check(Cli(path, assembly, "Customer", "--PREVIEW") == 0, "Legacy uppercase preview failed");
    Check(Cli(path, assembly, "Customer", "--Preview") == 0, "Legacy mixed-case preview failed");
    Check(File.ReadAllText(path) == original, "Legacy case-insensitive preview changed file");
    Check(Cli(path, assembly, "Customer") == 0, "Valid CLI failed");
    Check(File.ReadAllText(path) != original, "CLI did not save");
    // Discovery and generation must agree about eligible classes and readable fields.
    var catalog = AssemblyTypeCatalog.Load(assembly);
    var customer = catalog.Types.Single(t => t.FullName == "Fixtures.Customer");
    Check(customer.Fields.Select(f => f.Name).SequenceEqual(new[] { "Id", "Name" }), "Indexer, static or private getter leaked into fields");
    Check(!catalog.Types.Any(t => t.FullName is "Fixtures.AbstractModel" or "Fixtures.StaticModel" or "Fixtures.InternalModel"
        || t.FullName.StartsWith("Fixtures.GenericModel", StringComparison.Ordinal)), "Ineligible class was listed");
    // Explicit type selection retains the library's support for non-concrete-class models.
    var compatibility = new RdlcEditor(path);
    foreach (var typeName in new[] { "Fixtures.AbstractModel", "Fixtures.StructModel", "Fixtures.IModel" })
        Check(compatibility.PreviewDataSets(assembly, [typeName]).Single().Fields.Single().Name == "Id",
            "Explicit type compatibility was lost: " + typeName);
    Check(Cli("edit", path, "--assembly", assembly, "--type", "Fixtures.StructModel", "--preview") == 0,
        "Explicit struct selection failed");

    // Preview describes additions, removals and type changes without changing XML or the file.
    var typed = Report($"<DataSet Name='Customer'><Query><DataSourceName>{source}</DataSourceName></Query><Fields>"
        + "<Field Name='Id'><DataField>Id</DataField><rd:TypeName>System.String</rd:TypeName></Field>"
        + "<Field Name='Retired'><DataField>Retired</DataField><rd:TypeName>System.Int32</rd:TypeName></Field>"
        + "</Fields></DataSet>");
    File.WriteAllText(path, typed);
    editor = new RdlcEditor(path);
    var before = editor.GetString();
    var change = editor.PreviewDataSets(assembly, ["Customer"]).Single();
    Check(!change.IsNew && change.AddedFields.Single().Name == "Name" && change.RemovedFields.Single().Name == "Retired", "Incorrect field diff");
    Check(change.ChangedFields.Single() == new FieldTypeChange("Id", "System.String", "System.Int32"), "Incorrect type change");
    Check(editor.GetString() == before && File.ReadAllText(path) == typed, "Preview changed report");
    var addition = editor.PreviewDataSets(assembly, ["Sales.Duplicate"]).Single();
    Check(addition.IsNew && addition.AddedFields.Count == 1, "New DataSet was not described as an addition");

    var help = RunCli("edit", "--help");
    Check(help.ExitCode == 0 && help.Output.Contains("--type") && help.Output.Contains("--yes"), "Edit help is incomplete");
    var listing = RunCli("types", "--assembly", assembly, "--show-fields");
    Check(listing.ExitCode == 0 && listing.Output.Contains("Fixtures.Customer") && listing.Output.Contains("System.Int32"), "Class/field listing failed");
    var preview = RunCli("edit", path, "--assembly", assembly, "--type", "Customer", "--preview", "--show-fields");
    Check(preview.ExitCode == 0 && preview.Output.Contains("Retired") && preview.Output.Contains("System.String"), "CLI preview did not describe changes");
    Check(File.ReadAllText(path) == typed, "New preview command saved report");
    Check(Cli("edit", path, "--assembly", assembly, "--type", "Customer", "--preview", "--yes") == 0,
        "Preview combined with --yes failed");
    Check(File.ReadAllText(path) == typed, "--yes overrode preview and saved report");
    var missingSelection = RunCli("edit", path, "--assembly", assembly, "--yes");
    Check(missingSelection.ExitCode != 0 && missingSelection.Error.Contains("--type"), "Redirected input did not require explicit classes");
    var missingConfirmation = RunCli("edit", path, "--assembly", assembly, "--type", "Customer");
    Check(missingConfirmation.ExitCode != 0 && missingConfirmation.Error.Contains("--yes"), "Unattended update did not require confirmation option");
    var invalidType = RunCli("edit", path, "--assembly", assembly, "--type", "Missing", "--yes");
    Check(invalidType.ExitCode != 0 && invalidType.Error.Contains("Missing"), "Type error was not written to stderr");
    Check(Cli("edit", path, "--assembly", assembly, "--type", "Customer", "--preview", "--typo") != 0, "Unknown option was accepted");
    Check(File.ReadAllText(path) == typed, "Invalid arguments modified report");
    var invalidDll = Path.Combine(directory, "invalid.dll");
    File.WriteAllText(invalidDll, "not an assembly");
    var invalidAssembly = RunCli("edit", path, "--assembly", invalidDll, "--type", "Customer", "--yes");
    Check(invalidAssembly.ExitCode != 0 && invalidAssembly.Error.Length > 0, "Invalid DLL returned success");
    Check(File.ReadAllText(path) == typed, "Invalid DLL modified report");
    Check(Cli("edit", path, "--assembly", assembly, "--type", "Customer", "--type", "Sales.Duplicate", "--yes") == 0, "Repeated --type update failed");
    sets = XDocument.Load(path).Root!.Element(ns + "DataSets")!;
    Check(sets.Elements().Count() == 2, "Not all explicitly selected classes were saved");
    Console.WriteLine("All regression checks passed.");
}
finally { Directory.Delete(directory, true); }

internal sealed record CliResult(int ExitCode, string Output, string Error);

namespace Fixtures
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string this[int index] => index.ToString();
        public static int StaticValue => 1;
        public int PrivateGetter { private get; set; }
    }
    public class Order { public int Id { get; set; } }
    public struct StructModel { public int Id { get; set; } }
    public interface IModel { int Id { get; } }
    public abstract class AbstractModel { public int Id { get; set; } }
    public class GenericModel<T> { public T? Value { get; set; } }
    public static class StaticModel { public static int Id => 1; }
    internal class InternalModel { public int Id { get; set; } }
}
namespace Sales { public class Duplicate { public int Id { get; set; } } }
namespace Support { public class Duplicate { public int Id { get; set; } } }
