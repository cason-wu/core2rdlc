using RdlcEditorLib;

namespace Core2RDLC;

internal class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 3 || args.Length > 4)
        {
            ShowUsage();
            return;
        }

        try
        {
            string rdlcPath = args[0];
            string assemblyPath = args[1];
            string[] typeList = args[2].Split(',');

            var editor = new RdlcEditor(rdlcPath);
            editor.AddDataSets(assemblyPath, typeList);
            
            var txt = editor.GetString();
            Console.WriteLine(txt);
            
            var preview = HasArg(args, "--preview");
            
            if (preview == false)
                editor.Save();
            
            Console.WriteLine("完成！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"錯誤: {ex.Message}");
        }
    }

    static void ShowUsage()
    {
        Console.WriteLine("Usage: Core2RDLC <rdlcPath> <assemblyPath> <typeList> [--preview]");
        Console.WriteLine();
        Console.WriteLine("Parameters:");
        Console.WriteLine("  rdlcPath      Path to the RDLC file");
        Console.WriteLine("  assemblyPath  Path to the assembly file");
        Console.WriteLine("  typeList      Comma-separated list of type names");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --preview    Preview changes without saving to the RDLC file");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  Core2RDLC Report.rdlc MyAssembly.dll \"Customer,Order,Product\"");
    }

    static string GetArgValue(string[] args, string argName, string defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(argName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return defaultValue;
    }

    static bool HasArg(string[] args, string argName)
    {
        return Array.Exists(args, arg => arg.Equals(argName, StringComparison.OrdinalIgnoreCase));
    }
}
