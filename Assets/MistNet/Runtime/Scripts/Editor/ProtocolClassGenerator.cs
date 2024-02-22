using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using YamlDotNet.Serialization;

public class ProtocolClassGenerator : MonoBehaviour
{
    private static readonly string EnumFileName = "MistNetMessageType";
    private static readonly string ParentPath = "Assets/MistNet/Runtime/Scripts";
    private static readonly string OutputPath = "Assets/MistNet/Runtime/Scripts/Protocol";
    private static readonly string YamlFileName = "protocol.yaml";


    [MenuItem("Tools/MistNet/Generate Protocol Classes")]
    private static void GenerateClassesFromYAML()
    {
        var data = Read($"{ParentPath}/{YamlFileName}");
        GenerateClasses(data);
        AssetDatabase.Refresh();
    }

    private static void GenerateClasses(Dictionary<string, object> data)
    {
        var types = new List<string>();
        foreach (var message in (List<object>)data["messages"])
        {
            var messageInfo = (IDictionary)message;
            var className = messageInfo["name"].ToString();

            var fields = new List<object>();
            if (messageInfo.Contains("fields"))
            {
                fields = messageInfo["fields"] as List<object>;
            }

            types.Add(className);
            GenerateClass(className, fields);
        }

        GenerateEnumFile(types);
    }

    private static void GenerateEnumFile(List<string> types)
    {
        var txt = $"public enum {EnumFileName}" +
            "\r\n{\r\n";
        foreach (var type in types)
        {
            txt += $"    {type},\r\n";
        }
        txt += "}\r\n";
        File.WriteAllText($"{OutputPath}/{EnumFileName}.cs", txt);
    }

    private static void GenerateClass(string className, List<object> fields)
    {
        className = $"P_{className}";

        var generatedCode = $"using MemoryPack;\r\n" +
            $"using UnityEngine;\r\n\r\n" +
            $"[MemoryPackable]\r\n" +
            $"public partial class {className}\r\n";
        generatedCode += "{\r\n";

        foreach (var field in fields)
        {
            var fieldInfo = (IDictionary)field;
            var fieldName = fieldInfo["name"] as string;
            var fieldType = fieldInfo["type"] as string;

            generatedCode += $"    public {GetCSharpType(fieldType)} {fieldName} {{ get; set; }}\r\n";
        }

        generatedCode += "}\r\n";

        File.WriteAllText($"{OutputPath}/{className}.cs", generatedCode);
    }

    private static string GetCSharpType(string fieldType)
    {
        var typeMapping = new Dictionary<string, string>
        {
            { "string", "string" },
            { "int", "int" },
            { "float", "float" },
            { "boolean", "bool" },
            { "Vector3", "Vector3" },
            { "string[]", "string[]" },
            { "(int, int, int)", "(int, int, int)" },
        };

        if (typeMapping.ContainsKey(fieldType))
        {
            return typeMapping[fieldType];
        }

        return fieldType;
    }

    private static Dictionary<string, object> Read(string path)
    {
        if (!File.Exists(path)) return null;

        var input = File.ReadAllText(path);
        var result = ReadText(input);

        return result;
    }

    private static Dictionary<string, object> ReadText(string input)
    {
        var deserializer = new DeserializerBuilder().Build();
        var result = deserializer.Deserialize<Dictionary<string, object>>(input);

        return result;
    }
}