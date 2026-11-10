using Spectre.Console.Cli;

namespace Core2RDLC;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var app = new CommandApp();
            app.Configure(config =>
            {
                config.SetApplicationName("core2rdlc");
                config.PropagateExceptions();
                config.UseStrictParsing();
                config.AddCommand<EditCommand>("edit")
                    .WithDescription("Select classes, preview changes and update an RDLC report.");
                config.AddCommand<TypesCommand>("types")
                    .WithDescription("List available classes and optionally their fields.");
            });
            return app.Run(NormalizeLegacyArguments(args));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"錯誤: {ex.Message}");
            return 1;
        }
    }

    private static string[] NormalizeLegacyArguments(string[] args)
    {
        if (args.Length is < 3 or > 4 || args[0] is "edit" or "types" || args[0].StartsWith('-'))
            return args;
        Console.Error.WriteLine("舊版格式仍可使用；建議改用 edit <rdlcPath> --assembly <DLL> --type <TYPE> --yes。");
        var result = new List<string> { "edit", args[0], "--assembly", args[1] };
        foreach (var type in args[2].Split(','))
            result.AddRange(["--type", type]);
        if (args.Length == 4)
            result.Add(args[3].Equals("--preview", StringComparison.OrdinalIgnoreCase) ? "--preview" : args[3]);
        else
            result.Add("--yes"); // Preserve the original positional command's save behavior.
        return result.ToArray();
    }
}
