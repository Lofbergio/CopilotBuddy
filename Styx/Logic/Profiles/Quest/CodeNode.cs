// Decompiled with JetBrains decompiler
// Type: Styx.Logic.Profiles.Quest.CodeNode
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe


using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Profiles.Quest;

public class CodeNode : OrderNode
{
    public CodeNode(
        string path,
        Func<Assembly> assemblyGetter,
        Dictionary<string, string> args,
        XElement element)
        : base(OrderNodeType.Code, element)
    {
        this.Path = path;
        this.AssemblyGetter = assemblyGetter;
        this.Arguments = args;
    }

    public string Path { get; private set; }

    public Func<Assembly> AssemblyGetter { get; private set; }

    public Dictionary<string, string> Arguments { get; private set; }

    public new static OrderNode FromXml(XElement element)
    {
        var fileAttr = element.Attribute("File") ?? element.Attribute("file");
        if (fileAttr == null)
            throw new ProfileMissingAttributeException<string>("File", element);

        Func<Assembly> assemblyGetter = QuestBehaviorHelper.GetAssemblyCompiler(fileAttr.Value);
        if (assemblyGetter == null)
            throw new ProfileException();

        var attributes = element.Attributes()
            .Where(a => a != null && a.Name.ToString().ToLowerInvariant() != "file")
            .ToList();

        Dictionary<string, string> args = new Dictionary<string, string>();
        foreach (var attr in attributes)
        {
            string key = attr.Name.ToString();
            if (!args.ContainsKey(key))
                args.Add(key, attr.Value);
        }
        return new CodeNode(fileAttr.Value, assemblyGetter, args, element);
    }

    public override string ToString() => $"CodeNode: {this.Path} - {this.Element}";
}
