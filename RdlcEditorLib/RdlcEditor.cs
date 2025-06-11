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
        var defaultNamespace = _rdlDoc.DocumentElement.NamespaceURI;
        _nsManager.AddNamespace("ns", defaultNamespace);

        // 獲取 rd 命名空間
        foreach (XmlAttribute attr in _rdlDoc.DocumentElement.Attributes)
        {
            if (attr.Prefix == "xmlns" && attr.LocalName == "rd")
            {
                var rdNamespace = attr.Value;
                _nsManager.AddNamespace("rd", rdNamespace);
                break;
            }
        }
    }

    public void AddDataSets(string assemblyPath, string[] typeList)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);

        cleanOldNodes(assembly.GetName().Name);

        // 使用命名空間管理器
        var dataSourcesNode = _rdlDoc.DocumentElement.SelectSingleNode("ns:DataSources", _nsManager);
        var dataSourceNode = genDataSourceNode(assembly);
        dataSourcesNode.AppendChild(dataSourceNode);

        // 建立與此 Assembly 關聯的 DataSet
        var types = assembly.GetTypes().Where(t => typeList.Contains(t.Name));
        foreach (var type in types)
        {
            var dataSetNode = genDataSetNode(type);

            var dataSetsNode = _rdlDoc.DocumentElement.SelectSingleNode("ns:DataSets", _nsManager);
            dataSetsNode.AppendChild(dataSetNode);
        }
    }

    private XmlElement CreateElementWithAttribute(string elementName, string attributeName, string attributeValue)
    {
        var element = _rdlDoc.CreateElement(elementName, _rdlDoc.DocumentElement.NamespaceURI);
        var attr = _rdlDoc.CreateAttribute(attributeName);
        attr.Value = attributeValue;
        element.Attributes.Append(attr);
        return element;
    }

    private void cleanOldNodes(string assemblyName)
    {
        // 使用命名空間管理器
        var dataSourcesNode = _rdlDoc.DocumentElement.SelectSingleNode("ns:DataSources", _nsManager);
        if (dataSourcesNode is null)
        {
            dataSourcesNode = _rdlDoc.CreateElement("DataSources", _rdlDoc.DocumentElement.NamespaceURI);
            _rdlDoc.DocumentElement.AppendChild(dataSourcesNode);
        }
        else
        {
            var existingSource = dataSourcesNode.SelectSingleNode($"ns:DataSource[@Name='{assemblyName}']", _nsManager);

            if (existingSource != null)
                dataSourcesNode.RemoveChild(existingSource);
        }

        // 再移除與此 Assembly 關聯的 DataSet (根據 Query.DataSourceName = AssemblyName)
        var dataSetsNode = _rdlDoc.DocumentElement.SelectSingleNode("ns:DataSets", _nsManager);
        if (dataSetsNode is null)
        {
            dataSetsNode = _rdlDoc.CreateElement("DataSets", _rdlDoc.DocumentElement.NamespaceURI);
            _rdlDoc.DocumentElement.AppendChild(dataSetsNode);
        }
        else
        {
            var dataSetNodes = dataSetsNode.SelectNodes($"ns:DataSet[ns:Query/ns:DataSourceName='{assemblyName}']", _nsManager);

            foreach (XmlNode node in dataSetNodes!)
                dataSetsNode.RemoveChild(node);
        }
    }

    private XmlElement genDataSourceNode(Assembly assembly)
    {
        // 使用新方法建立 DataSource
        var dataSourceNode = CreateElementWithAttribute("DataSource", "Name", assembly.GetName().Name);

        var connectionProps = _rdlDoc.CreateElement("ConnectionProperties", _rdlDoc.DocumentElement.NamespaceURI);
        var dataProvider = _rdlDoc.CreateElement("DataProvider", _rdlDoc.DocumentElement.NamespaceURI);
        dataProvider.InnerText = "System.Data.DataSet";
        connectionProps.AppendChild(dataProvider);

        var connectString = _rdlDoc.CreateElement("ConnectString", _rdlDoc.DocumentElement.NamespaceURI);
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

        var queryNode = _rdlDoc.CreateElement("Query", _rdlDoc.DocumentElement.NamespaceURI);
        var dataSourceName = _rdlDoc.CreateElement("DataSourceName", _rdlDoc.DocumentElement.NamespaceURI);
        dataSourceName.InnerText = type.Assembly.GetName().Name;
        queryNode.AppendChild(dataSourceName);

        var commandText = _rdlDoc.CreateElement("CommandText", _rdlDoc.DocumentElement.NamespaceURI);
        commandText.InnerText = "/* Local Query */";
        queryNode.AppendChild(commandText);

        dataSetNode.AppendChild(queryNode);

        // Fields
        var fieldsNode = _rdlDoc.CreateElement("Fields", _rdlDoc.DocumentElement.NamespaceURI);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(p => p.CanRead)
                             .OrderBy(p => p.Name);

        foreach (var property in properties)
        {
            var fieldNode = CreateElementWithAttribute("Field", "Name", property.Name);

            var dataFieldNode = _rdlDoc.CreateElement("DataField", _rdlDoc.DocumentElement.NamespaceURI);
            dataFieldNode.InnerText = property.Name;
            fieldNode.AppendChild(dataFieldNode);

            var rdTypeNameNode = _rdlDoc.CreateElement("rd:TypeName",
                _nsManager.LookupNamespace("rd"));
            rdTypeNameNode.InnerText = property.PropertyType.FullName;
            fieldNode.AppendChild(rdTypeNameNode);

            fieldsNode.AppendChild(fieldNode);
        }
        dataSetNode.AppendChild(fieldsNode);

        // rd:DataSetInfo
        var dataSetInfoNode = _rdlDoc.CreateElement("rd:DataSetInfo",
            _nsManager.LookupNamespace("rd"));

        var rdDataSetName = _rdlDoc.CreateElement("rd:DataSetName", dataSetInfoNode.NamespaceURI);
        rdDataSetName.InnerText = type.Assembly.GetName().Name;
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
