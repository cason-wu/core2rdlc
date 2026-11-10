using System.ComponentModel;
using RdlcEditorLib;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Core2RDLC;

public class AssemblySettings : CommandSettings
{
    [CommandOption("-a|--assembly <PATH>")]
    [Description("Path to the DLL containing the report classes.")]
    public string AssemblyPath { get; init; } = "";

    [CommandOption("--show-fields")]
    [Description("Display the readable fields of each class.")]
    public bool ShowFields { get; init; }

    public override ValidationResult Validate() => File.Exists(AssemblyPath)
        ? ValidationResult.Success()
        : ValidationResult.Error("Specify an existing DLL with --assembly <PATH>.");
}

public sealed class EditSettings : AssemblySettings
{
    [CommandArgument(0, "<rdlcPath>")]
    [Description("Path to the RDLC report to update.")]
    public string RdlcPath { get; init; } = "";

    [CommandOption("-t|--type <TYPE>")]
    [Description("Class to update; repeat for multiple classes. Omit to choose interactively.")]
    public string[] Types { get; init; } = [];

    [CommandOption("--preview")]
    [Description("Validate and display changes without saving.")]
    public bool Preview { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Save without confirmation (required for unattended updates).")]
    public bool Yes { get; init; }

    public override ValidationResult Validate()
    {
        var assembly = base.Validate();
        return !assembly.Successful ? assembly : File.Exists(RdlcPath)
            ? ValidationResult.Success()
            : ValidationResult.Error("Specify an existing RDLC report.");
    }
}

public sealed class TypesCommand : Command<AssemblySettings>
{
    public override int Execute(CommandContext context, AssemblySettings settings, CancellationToken cancellationToken)
    {
        var types = AssemblyTypeCatalog.Load(settings.AssemblyPath).Types;
        var table = new Table().AddColumn("Class").AddColumn("DataSet").AddColumn("Fields");
        foreach (var type in types)
            table.AddRow(new Text(type.FullName), new Text(type.DataSetName), new Text(type.Fields.Count.ToString()));
        AnsiConsole.Write(table);
        if (settings.ShowFields)
            CliDisplay.Fields(types.Select(t => (t.FullName, t.Fields)));
        return 0;
    }
}

public sealed class EditCommand : Command<EditSettings>
{
    public override int Execute(CommandContext context, EditSettings settings, CancellationToken cancellationToken)
    {
        var interactive = CanPrompt();
        if (settings.Types.Length == 0 && !interactive)
            throw new ArgumentException("Class selection requires an interactive terminal. Specify --type <TYPE> (repeatable).");
        if (!settings.Preview && !settings.Yes && !interactive)
            throw new ArgumentException("Saving requires confirmation. Use --yes to save or --preview to inspect without saving.");

        var editor = new RdlcEditor(settings.RdlcPath);
        var names = settings.Types;
        if (names.Length == 0)
        {
            var types = AssemblyTypeCatalog.Load(settings.AssemblyPath).Types;
            if (types.Count == 0)
                throw new ArgumentException("No public, concrete, non-generic classes were found in the DLL.");
            var candidates = types;
            while (true)
            {
                var filter = AnsiConsole.Prompt(new TextPrompt<string>("搜尋 class 或 namespace（Enter 顯示全部）：")
                    .AllowEmpty()).Trim();
                candidates = types.Where(t => t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Count > 0)
                    break;
                AnsiConsole.WriteLine("沒有符合的 class，請重新搜尋。");
            }
            var existing = editor.GetDataSetNames().ToHashSet(StringComparer.Ordinal);
            names = AnsiConsole.Prompt(new MultiSelectionPrompt<ReportType>()
                .Title("選擇要加入或更新的 class")
                .PageSize(15)
                .Required()
                .InstructionsText("[grey]↑↓ 移動，空白鍵勾選，Enter 確認；Ctrl+C 取消[/]")
                .UseConverter(type => Markup.Escape($"{type.FullName}  ({(existing.Contains(type.DataSetName) ? "更新" : "新增")}, {type.Fields.Count} fields)"))
                .AddChoices(candidates)).Select(t => t.FullName).ToArray();
        }

        var changes = editor.PreviewDataSets(settings.AssemblyPath, names);
        CliDisplay.Changes(changes);
        if (settings.ShowFields || (interactive && !settings.Yes))
            CliDisplay.Fields(changes.Select(c => (c.Name, c.Fields)));
        if (settings.Preview)
        {
            AnsiConsole.WriteLine("預覽完成，尚未儲存。");
            return 0;
        }
        if (!settings.Yes && !AnsiConsole.Confirm("是否儲存以上變更？", defaultValue: false))
        {
            AnsiConsole.WriteLine("已取消，檔案未變更。");
            return 0;
        }
        editor.AddDataSets(settings.AssemblyPath, names);
        editor.Save();
        AnsiConsole.WriteLine("完成！");
        return 0;
    }

    private static bool CanPrompt()
    {
        var ci = Environment.GetEnvironmentVariable("CI");
        var inCi = !string.IsNullOrEmpty(ci) && ci != "0" && !ci.Equals("false", StringComparison.OrdinalIgnoreCase);
        return !inCi && !Console.IsInputRedirected && !Console.IsOutputRedirected
            && AnsiConsole.Profile.Capabilities.Interactive;
    }
}

internal static class CliDisplay
{
    public static void Changes(IReadOnlyList<DataSetChange> changes)
    {
        var table = new Table().AddColumn("DataSet").AddColumn("動作").AddColumn("欄位變更");
        foreach (var change in changes)
        {
            var details = change.AddedFields.Select(f => $"+ {f.Name}")
                .Concat(change.RemovedFields.Select(f => $"- {f.Name}"))
                .Concat(change.ChangedFields.Select(f => $"~ {f.Name}: {f.OldType} → {f.NewType}"));
            table.AddRow(new Text(change.Name), new Text(change.IsNew ? "新增" : "更新"),
                new Text(string.Join(", ", details.DefaultIfEmpty("欄位名稱與型別不變"))));
        }
        AnsiConsole.Write(table);
        if (changes.Any(c => !c.IsNew))
            AnsiConsole.WriteLine("注意：更新會重建所選 DataSet；手動計算欄位及其他自訂設定可能被移除。其他 DataSet 保留。");
    }

    public static void Fields(IEnumerable<(string Name, IReadOnlyList<ReportField> Fields)> types)
    {
        var table = new Table().AddColumn("Class / DataSet").AddColumn("欄位").AddColumn("型別");
        foreach (var (name, fields) in types)
        {
            if (fields.Count == 0)
                table.AddRow(new Text(name), new Text("（無可讀欄位）"), new Text(""));
            foreach (var field in fields)
                table.AddRow(new Text(name), new Text(field.Name), new Text(field.TypeName));
        }
        AnsiConsole.Write(table);
    }
}
