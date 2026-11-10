using System.Reflection;
using System.Text;
using System.Xml;

namespace RdlcEditorLib;

public class RdlcEditor
{
    private XmlDocument _rdlDoc;
    private string _rdlcPath;
    private XmlNamespaceManager _nsManager;


    public RdlcEditor(string rdlcPath)
    {
        _rdlcPath = rdlcPath;

        // 讀取並載入 RDLC XML
        _rdlDoc = new XmlDocument();
        _rdlDoc.Load(rdlcPath);

        // 從文件根元素中提取命名空間
        _nsManager = new XmlNamespaceManager(_rdlDoc.NameTable);

        // 獲取默認命名空間
        var root = _rdlDoc.DocumentElement ?? throw new InvalidDataException("The RDLC file has no root element.");
        var defaultNamespace = root.NamespaceURI;
        _nsManager.AddNamespace("ns", defaultNamespace);

        // 獲取 rd 命名空間
        foreach (XmlAttribute attr in root.Attributes)
        {
            if (attr.Prefix == "xmlns" && attr.LocalName == "rd")
            {
                var rdNamespace = attr.Value;
                _nsManager.AddNamespace("rd", rdNamespace);
                break;
            }
        }
    }

    public IReadOnlyList<string> GetDataSetNames() => Array.AsReadOnly(
        (_rdlDoc.DocumentElement!.SelectNodes("ns:DataSets/ns:DataSet", _nsManager)!
            .Cast<XmlNode>().Select(n => n.Attributes!["Name"]!.Value)).ToArray());

    /// <summary>Validates and describes an update without modifying the report or file.</summary>
    public IReadOnlyList<DataSetChange> PreviewDataSets(string assemblyPath, string[] typeList)
    {
        var update = PrepareUpdate(assemblyPath, typeList);
        return Array.AsReadOnly(update.NewSets.Select(node =>
        {
            var name = node.GetAttribute("Name");
            var old = update.ExistingSets.SingleOrDefault(n => n.Attributes?["Name"]?.Value == name);
            var fields = ReadFields(node);
            var previous = old == null ? [] : ReadFields(old);
            var currentByName = fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
            var oldByName = previous.ToDictionary(f => f.Name, StringComparer.Ordinal);
            return new DataSetChange(name, old == null, Array.AsReadOnly(fields),
                Array.AsReadOnly(fields.Where(f => !oldByName.ContainsKey(f.Name)).ToArray()),
                Array.AsReadOnly(previous.Where(f => !currentByName.ContainsKey(f.Name)).ToArray()),
                Array.AsReadOnly(fields.Where(f => oldByName.TryGetValue(f.Name, out var value) && value.TypeName != f.TypeName)
                    .Select(f => new FieldTypeChange(f.Name, oldByName[f.Name].TypeName, f.TypeName)).ToArray()));
        }).ToArray());
    }

    private ReportField[] ReadFields(XmlNode dataSet) => dataSet.SelectNodes("ns:Fields/ns:Field", _nsManager)!
        .Cast<XmlNode>().Select(node => new ReportField(node.Attributes!["Name"]!.Value,
            node.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n.LocalName == "TypeName")?.InnerText ?? "(unspecified)"))
        .ToArray();

    private sealed record PreparedUpdate(string AssemblyName, XmlElement NewSource,
        XmlElement[] NewSets, XmlNode[] ExistingSets);

    private PreparedUpdate PrepareUpdate(string assemblyPath, string[] typeList)
    {
        var catalog = AssemblyTypeCatalog.Load(assemblyPath);
        var assembly = catalog.Assembly;
        var types = catalog.Resolve(typeList);

        var assemblyName = assembly.GetName().Name!;
        var dataSetsNode = _rdlDoc.DocumentElement!.SelectSingleNode("ns:DataSets", _nsManager);
        var existingSets = dataSetsNode?.SelectNodes("ns:DataSet", _nsManager)?.Cast<XmlNode>().ToArray()
                           ?? Array.Empty<XmlNode>();
        foreach (var type in types)
        {
            var conflicts = existingSets.Where(node => node.Attributes?["Name"]?.Value == type.Name);
            if (conflicts.Any(node => node.SelectSingleNode("ns:Query/ns:DataSourceName", _nsManager)?.InnerText != assemblyName))
                throw new ArgumentException($"DataSet '{type.Name}' already belongs to another data source.");
        }

        // Generate everything before modifying the report, so failures leave it unchanged.
        var newSource = genDataSourceNode(assembly);
        var newSets = types.Select(genDataSetNode).ToArray();
        return new PreparedUpdate(assemblyName, newSource, newSets, existingSets);
    }

    public void AddDataSets(string assemblyPath, string[] typeList)
    {
        var update = PrepareUpdate(assemblyPath, typeList);
        var dataSourcesNode = GetOrCreateContainer("DataSources");
        var dataSetsNode = GetOrCreateContainer("DataSets");
        foreach (var node in dataSourcesNode.SelectNodes("ns:DataSource", _nsManager)!.Cast<XmlNode>().ToArray())
            if (node.Attributes?["Name"]?.Value == update.AssemblyName)
                dataSourcesNode.RemoveChild(node);
        dataSourcesNode.AppendChild(update.NewSource);

        var selectedNames = update.NewSets.Select(t => t.GetAttribute("Name")).ToHashSet(StringComparer.Ordinal);
        foreach (var node in update.ExistingSets)
            if (selectedNames.Contains(node.Attributes?["Name"]?.Value ?? ""))
                dataSetsNode.RemoveChild(node);
        foreach (var node in update.NewSets)
            dataSetsNode.AppendChild(node);
    }

    private XmlNode GetOrCreateContainer(string name)
    {
        var root = _rdlDoc.DocumentElement!;
        var node = root.SelectSingleNode($"ns:{name}", _nsManager);
        if (node != null)
            return node;
        return root.AppendChild(_rdlDoc.CreateElement(name, root.NamespaceURI))!;
    }

    private XmlElement CreateElementWithAttribute(string elementName, string attributeName, string attributeValue)
    {
        var element = _rdlDoc.CreateElement(elementName, _rdlDoc.DocumentElement!.NamespaceURI);
        element.SetAttribute(attributeName, attributeValue);
        return element;
    }

    private XmlElement genDataSourceNode(Assembly assembly)
    {
        // 使用新方法建立 DataSource
        var dataSourceNode = CreateElementWithAttribute("DataSource", "Name", assembly.GetName().Name!);

        var connectionProps = _rdlDoc.CreateElement("ConnectionProperties", _rdlDoc.DocumentElement!.NamespaceURI);
        var dataProvider = _rdlDoc.CreateElement("DataProvider", _rdlDoc.DocumentElement!.NamespaceURI);
        dataProvider.InnerText = "System.Data.DataSet";
        connectionProps.AppendChild(dataProvider);

        var connectString = _rdlDoc.CreateElement("ConnectString", _rdlDoc.DocumentElement!.NamespaceURI);
        connectString.InnerText = "/* Local Connection */";
        connectionProps.AppendChild(connectString);

        dataSourceNode.AppendChild(connectionProps);

        var rdDataSourceId = _rdlDoc.CreateElement("rd:DataSourceID", _nsManager.LookupNamespace("rd"));
        rdDataSourceId.InnerText = Guid.NewGuid().ToString();
        dataSourceNode.AppendChild(rdDataSourceId);

        return dataSourceNode;
    }

    private XmlElement genDataSetNode(Type type)
    {
        // 修正回傳物件
        var dataSetNode = CreateElementWithAttribute("DataSet", "Name", type.Name);

        var queryNode = _rdlDoc.CreateElement("Query", _rdlDoc.DocumentElement!.NamespaceURI);
        var dataSourceName = _rdlDoc.CreateElement("DataSourceName", _rdlDoc.DocumentElement!.NamespaceURI);
        dataSourceName.InnerText = type.Assembly.GetName().Name!;
        queryNode.AppendChild(dataSourceName);

        var commandText = _rdlDoc.CreateElement("CommandText", _rdlDoc.DocumentElement!.NamespaceURI);
        commandText.InnerText = "/* Local Query */";
        queryNode.AppendChild(commandText);

        dataSetNode.AppendChild(queryNode);

        // Fields
        var fieldsNode = _rdlDoc.CreateElement("Fields", _rdlDoc.DocumentElement!.NamespaceURI);
        var properties = AssemblyTypeCatalog.GetProperties(type);

        foreach (var property in properties)
        {
            var fieldNode = CreateElementWithAttribute("Field", "Name", property.Name);

            var dataFieldNode = _rdlDoc.CreateElement("DataField", _rdlDoc.DocumentElement!.NamespaceURI);
            dataFieldNode.InnerText = property.Name;
            fieldNode.AppendChild(dataFieldNode);

            var rdTypeNameNode = _rdlDoc.CreateElement("rd:TypeName",
                _nsManager.LookupNamespace("rd"));
            rdTypeNameNode.InnerText = property.PropertyType.FullName ?? property.PropertyType.Name;
            fieldNode.AppendChild(rdTypeNameNode);

            fieldsNode.AppendChild(fieldNode);
        }
        dataSetNode.AppendChild(fieldsNode);

        // rd:DataSetInfo
        var dataSetInfoNode = _rdlDoc.CreateElement("rd:DataSetInfo",
            _nsManager.LookupNamespace("rd"));

        var rdDataSetName = _rdlDoc.CreateElement("rd:DataSetName", dataSetInfoNode.NamespaceURI);
        rdDataSetName.InnerText = type.Assembly.GetName().Name!;
        dataSetInfoNode.AppendChild(rdDataSetName);

        var tableNameNode = _rdlDoc.CreateElement("rd:TableName", dataSetInfoNode.NamespaceURI);
        tableNameNode.InnerText = type.Name;
        dataSetInfoNode.AppendChild(tableNameNode);

        var objectDataSourceType = _rdlDoc.CreateElement("rd:ObjectDataSourceType", dataSetInfoNode.NamespaceURI);
        objectDataSourceType.InnerText = propertyTypeName(type);
        dataSetInfoNode.AppendChild(objectDataSourceType);

        dataSetNode.AppendChild(dataSetInfoNode);
        return dataSetNode;
    }

    // 可依需求自行處理 AssemblyQualifiedName
    private string propertyTypeName(Type type)
    {
        return type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
    }

    public void Save()
    {
        // Configure XML writer settings to preserve formatting
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
        };

        // Create XmlWriter with proper settings
        using (XmlWriter writer = XmlWriter.Create(_rdlcPath, settings))
        {
            _rdlDoc.Save(writer);
        }
    }

    public string GetString()
    {
        // Also use proper formatting when getting the string representation
        var settings = new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = true,
        };

        using var stringWriter = new StringWriter();
        using (XmlWriter writer = XmlWriter.Create(stringWriter, settings))
        {
            _rdlDoc.Save(writer);
        }

        return stringWriter.ToString();
    }
}
