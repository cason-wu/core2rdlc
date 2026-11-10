# Core2RDLC

A .NET 8 command-line tool and library for adding and updating RDLC DataSets
from classes in a .NET assembly. The CLI uses Spectre.Console.Cli for commands
and Spectre.Console for interactive selection and change tables.

## Interactive editing

```sh
dotnet run --project Core2RDLC -- edit Report.rdlc --assembly MyApp.Models.dll
```

1. Search by class name or namespace, or press Enter to show all classes.
2. Use the arrow keys to move, Space to select classes and Enter to continue.
   The list shows fully qualified names, field counts and add/update status.
3. Review added, removed and type-changed fields, plus the selected field details.
4. Confirm saving. The default answer is **No**; declining leaves the file unchanged.

Use Ctrl+C to cancel selection. No classes are constructed during discovery.
The interactive/class list shows public, concrete, non-generic classes. Explicit
`--type` selection and the library retain support for other .NET types with
properties, such as structs, interfaces and abstract classes. Fields come from
public instance getters, excluding indexers. Keep the DLL's dependencies beside it.
Two selected classes cannot share a simple name, since that name becomes the
DataSet name. Use a full type name to resolve ambiguous simple names.

## Commands and options

The examples below assume the built executable is available as `core2rdlc`.
With `dotnet run`, put the same arguments after `--`, as shown above.

```sh
core2rdlc --help
core2rdlc edit --help

# Discover classes, optionally displaying their fields.
core2rdlc types --assembly MyApp.Models.dll --show-fields

# Preview selected classes without modifying the report.
core2rdlc edit Report.rdlc --assembly MyApp.Models.dll --type MyApp.Customer --preview

# Update multiple classes in a script, without confirmation.
core2rdlc edit Report.rdlc --assembly MyApp.Models.dll --type MyApp.Customer --type MyApp.Order --yes
```

| Option | Purpose |
| --- | --- |
| `-a, --assembly <PATH>` | Required DLL path. |
| `-t, --type <TYPE>` | Class to update; repeat for multiple classes. Omit for interactive selection. |
| `--preview` | Validate and show the change summary without saving, including when combined with `--yes`. |
| `--show-fields` | Show the class/DataSet field details; also available with `types`. |
| `-y, --yes` | Save without confirmation. |

Redirected input/output, CI, and terminals without interactive support never
start prompts. In those environments, `edit` requires explicit `--type` values
and either `--preview` or `--yes`. Invalid or unknown arguments fail before saving.
Exit code `0` means success (including preview or a declined confirmation).
Failures return a nonzero code and write the error to standard error.

## Update behavior

Only selected DataSets are replaced; other DataSets are preserved. Preview
validates the same update rules as saving, without changing the XML in memory
or the file. Missing/ambiguous classes and DataSet name conflicts fail before
modification. A DataSet owned by another data source cannot be overwritten.

Selected DataSets are regenerated: manually added calculated fields and other
custom settings may be removed. The change summary shows field additions,
removals and type changes; it is not a complete diff of all custom RDLC settings.

The library exposes `AssemblyTypeCatalog.Load(...)`, `PreviewDataSets(...)`,
`AddDataSets(...)` and `Save()` independently of the interactive UI.

## Legacy compatibility

The original positional format remains available:

```sh
core2rdlc Report.rdlc MyAssembly.dll "Customer,Order" --preview
core2rdlc Report.rdlc MyAssembly.dll "Customer,Order"
```

It prints a migration notice to standard error. Without `--preview`, the legacy
format retains its immediate-save behavior. Preview now displays a change
summary rather than the full XML. Prefer `edit` for new scripts.

## Build and regression checks

```sh
dotnet build Core2RDLC.sln -c Release
dotnet run --project Core2RDLC.Tests -c Release
```

The regression runner uses no external test framework. It checks discovery,
field diffs, validation without modification, partial updates, CLI errors,
unattended operation, preview and legacy compatibility. A PTY smoke test for
interactive selection, preview, confirmation and cancellation is available on
Linux/macOS:

```sh
python3 Core2RDLC.Tests/interactive_smoke.py
```
