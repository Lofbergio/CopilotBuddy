// Decompiled with JetBrains decompiler
// Type: Styx.Logic.Profiles.Quest.QuestBehaviorHelper
// Assembly: Honorbuddy, Version=2.0.0.5999, Culture=neutral, PublicKeyToken=50a565ab5c01ae50
// MVID: FB7FEB85-27C0-4D17-B8DE-615FDFDA7752
// Assembly location: C:\Users\Texy6\Desktop\Honorbuddy-cleaned.exe

using Styx.Helpers;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;

#nullable disable
namespace Styx.Logic.Profiles.Quest;

public class QuestBehaviorHelper
{
    private static readonly Dictionary<string, Assembly> dictionary_0 = new Dictionary<string, Assembly>();
    private static readonly object object_0 = new object();
    private Assembly assembly_0;
    private bool bool_0;

    public QuestBehaviorHelper(string path)
    {
        this.Path = path;
        if (!StyxSettings.Instance.ProfileDebuggingMode)
            return;
        this.assembly_0 = this.method_0();
        this.bool_0 = true;
    }

    public string Path { get; private set; }

    private Assembly method_0()
    {
        string lower = this.Path.ToLower();
        Assembly assembly1;
        if (dictionary_0.TryGetValue(lower, out assembly1))
            return assembly1;
        Assembly assembly2;
        lock (object_0)
        {
            Logging.WriteDebug("Compiling quest behavior from '{0}'", this.Path);
            
            // Compile the quest behavior
            CSharpCodeProvider provider = new CSharpCodeProvider();
            CompilerParameters parameters = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false
            };
            
            // Add references
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Core.dll");
            parameters.ReferencedAssemblies.Add("System.Xml.dll");
            parameters.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
            
            CompilerResults compilerResults;
            if (Directory.Exists(this.Path))
            {
                string[] files = Directory.GetFiles(this.Path, "*.cs", SearchOption.AllDirectories);
                compilerResults = provider.CompileAssemblyFromFile(parameters, files);
            }
            else
            {
                compilerResults = provider.CompileAssemblyFromFile(parameters, this.Path);
            }
            
            if (compilerResults.Errors.HasErrors)
            {
                Logging.WriteDebug("Could not compile quest behavior from '{0}'", this.Path);
                foreach (CompilerError compilerError in compilerResults.Errors.OfType<CompilerError>().Where(e => !e.IsWarning))
                    Logging.WriteDebug("Line {0}: {1}", compilerError.Line, compilerError.ErrorText);
                dictionary_0.Add(lower, null);
                assembly2 = null;
            }
            else
            {
                dictionary_0.Add(lower, compilerResults.CompiledAssembly);
                assembly2 = compilerResults.CompiledAssembly;
            }
        }
        return assembly2;
    }

    public Assembly GetAssembly()
    {
        if (!this.bool_0)
        {
            this.assembly_0 = this.method_0();
            this.bool_0 = true;
        }
        return this.assembly_0;
    }

    public static Func<Assembly> GetAssemblyCompiler(string path)
    {
        string path1 = System.IO.Path.Combine(System.IO.Path.Combine(Logging.ApplicationPath, "Quest Behaviors"), path);
        if (!Directory.Exists(path1))
            path1 = System.IO.Path.ChangeExtension(path1, ".cs");
        return new Func<Assembly>(new QuestBehaviorHelper(path1).GetAssembly);
    }
}
