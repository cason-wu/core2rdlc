namespace RdlcEditorLib;

public sealed record FieldTypeChange(string Name, string OldType, string NewType);

public sealed record DataSetChange(
    string Name,
    bool IsNew,
    IReadOnlyList<ReportField> Fields,
    IReadOnlyList<ReportField> AddedFields,
    IReadOnlyList<ReportField> RemovedFields,
    IReadOnlyList<FieldTypeChange> ChangedFields);
