using System.Reflection;

namespace RdlcEditorLib;

public sealed record ReportField(string Name, string TypeName);
public sealed record ReportType(string FullName, string DataSetName, IReadOnlyList<ReportField> Fields);

/// <summary>Discovers public, concrete, closed classes without constructing their objects.</summary>
public sealed class AssemblyTypeCatalog
{
    private readonly Type[] _availableTypes;
    internal Assembly Assembly { get; }
    public IReadOnlyList<ReportType> Types { get; }

    private AssemblyTypeCatalog(Assembly assembly)
    {
        Assembly = assembly;
        try
        {
            _availableTypes = assembly.GetTypes();
            var classes = _availableTypes
                .Where(t => t.IsClass && t.IsVisible && !t.IsAbstract && !t.ContainsGenericParameters)
                .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
            Types = Array.AsReadOnly(classes.Select(t => new ReportType(
                t.FullName!, t.Name, Array.AsReadOnly(GetProperties(t)
                    .Select(p => new ReportField(p.Name, p.PropertyType.FullName ?? p.PropertyType.Name))
                    .ToArray()))).ToArray());
        }
        catch (ReflectionTypeLoadException ex)
        {
            var details = string.Join(Environment.NewLine, ex.LoaderExceptions
                .Where(e => e != null).Select(e => e!.Message).Distinct());
            throw new InvalidOperationException(
                $"Unable to inspect the assembly. Place its required dependencies beside the DLL.{Environment.NewLine}{details}", ex);
        }
    }

    public static AssemblyTypeCatalog Load(string assemblyPath) =>
        new(System.Reflection.Assembly.LoadFrom(Path.GetFullPath(assemblyPath)));

    internal Type[] Resolve(string[] typeNames)
    {
        ArgumentNullException.ThrowIfNull(typeNames);
        if (typeNames.Length == 0 || typeNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Specify at least one non-empty type name.", nameof(typeNames));
        var selected = typeNames.Select(name =>
        {
            name = name.Trim();
            var matches = _availableTypes.Where(t => t.FullName == name).ToArray();
            if (matches.Length == 0)
                matches = _availableTypes.Where(t => t.Name == name).ToArray();
            if (matches.Length != 1)
                throw new ArgumentException($"Type '{name}' was not found or is ambiguous; use its full name.");
            return matches[0];
        }).Distinct().ToArray();
        if (selected.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != selected.Length)
            throw new ArgumentException("Selected types must have unique DataSet names.");
        return selected;
    }

    internal static PropertyInfo[] GetProperties(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
        .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
}
