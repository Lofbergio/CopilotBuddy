#nullable disable
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CSharp;

namespace Styx.Loaders
{
    /// <summary>
    /// Compiles C# source files at runtime.
    /// Ported from HB's Class52 (ns5).
    /// </summary>
    internal class SourceCompiler
    {
        private readonly long _timestamp;

        public SourceCompiler(string path)
        {
            _timestamp = DateTime.Now.Ticks;
            CompilerVersion = 3.5f;
            SourceFilePaths = new List<string>();

            if (File.Exists(path))
            {
                FileStructure = FileStructureType.SingleFile;
            }
            else if (Directory.Exists(path))
            {
                FileStructure = FileStructureType.Directory;
            }

            SourcePath = path;
            Options = new CompilerParameters
            {
                GenerateExecutable = false,
                GenerateInMemory = true,
                IncludeDebugInformation = true,
                CompilerOptions = "/d:COPILOTBUDDY",
                TempFiles = new TempFileCollection(Path.GetTempPath()),
                OutputAssembly = Path.Combine(Path.GetTempPath(), AssemblyName)
            };
            CompiledToLocation = Options.OutputAssembly;

            // Add all currently loaded assemblies as references - EXCEPT previously-compiled plugin/routine
            // builds, which are emitted to the temp dir. Referencing a prior build of a plugin both
            // (a) causes duplicate-type "ambiguous" compile errors on recompile (e.g. BuddyControlPanel's
            // Extensions.ShowAtFront), and (b) grows the metadata reference set every recompile - which is
            // what eventually OOMs the 32-bit process (MetadataReference.CreateFromFile over an ever-growing
            // pile of stale plugin DLLs). Skipping them makes the reference set bounded and recompiles clean.
            string tempDir = Path.GetTempPath();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string loc = assembly.Location;
                // Skip previously-compiled drop-in builds — both the temp dir AND the persistent
                // CompileCache (below). Referencing them causes duplicate-type "ambiguous" compile
                // errors and grows the metadata reference set (which eventually OOMs the 32-bit proc).
                if (!string.IsNullOrEmpty(loc)
                    && (loc.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase)
                        || loc.StartsWith(CacheDir, StringComparison.OrdinalIgnoreCase)))
                    continue;
                AddReference(loc);
            }

            // Try to add common WPF / Windows Forms integration assemblies if available
            var wpfAssemblyNames = new[] {
                "PresentationCore",
                "PresentationFramework",
                "WindowsBase",
                "WindowsFormsIntegration",
                "System.Xaml",
                "System.Windows.Forms",
                // The WinForms Form/Control base implements COM-interop interfaces (IOleObject, IViewObject…)
                // defined here; it loads lazily so it isn't in GetAssemblies() yet. Without it, ANY drop-in
                // that subclasses Form fails Roslyn with "type X is defined in an assembly that is not
                // referenced (System.Windows.Forms.Primitives)". Force-load so compiled forms resolve.
                "System.Windows.Forms.Primitives"
            };

            foreach (var name in wpfAssemblyNames)
            {
                try
                {
                    var asm = Assembly.Load(new AssemblyName(name));
                    if (asm != null && !string.IsNullOrEmpty(asm.Location))
                        AddReference(asm.Location);
                }
                catch
                {
                    // ignore if assembly not available
                }
            }

            // WCF assemblies — only present when the System.ServiceModel.* NuGet packages
            // are restored (shipped next to CopilotBuddy.exe). Force-load them here so they
            // appear in AppDomain.CurrentDomain.GetAssemblies() and so a compiled plugin can
            // actually instantiate ChannelFactory<>, NetNamedPipeBinding, etc. at runtime.
            var wcfAssemblyNames = new[] {
                "System.ServiceModel",
                "System.ServiceModel.Primitives",
                "System.ServiceModel.NetNamedPipe"
            };
            foreach (var name in wcfAssemblyNames)
            {
                try
                {
                    var asm = Assembly.Load(new AssemblyName(name));
                    if (asm != null && !string.IsNullOrEmpty(asm.Location))
                        AddReference(asm.Location);
                }
                catch
                {
                    // ignore if assembly not available
                }
            }
        }

        public Assembly CompiledAssembly { get; private set; }
        public string SourcePath { get; private set; }
        public FileStructureType FileStructure { get; private set; }
        public CompilerParameters Options { get; private set; }
        public float CompilerVersion { get; private set; }
        public string CompiledToLocation { get; private set; }
        public List<string> SourceFilePaths { get; private set; }

        public string AssemblyName
        {
            get
            {
                string name = FileStructure == FileStructureType.SingleFile
                    ? Path.GetFileNameWithoutExtension(SourcePath)
                    : new DirectoryInfo(SourcePath).Name;
                return $"{name}_{_timestamp}.dll";
            }
        }

        public void AddReference(string assembly)
        {
            if (!string.IsNullOrEmpty(assembly) && !Options.ReferencedAssemblies.Contains(assembly))
            {
                Options.ReferencedAssemblies.Add(assembly);
            }
        }

        public void AddEmbeddedResource(string path)
        {
            Options.EmbeddedResources.Add(path);
        }

        /// <summary>
        /// Collects source files from the path.
        /// </summary>
        private void CollectSourceFiles()
        {
            if (FileStructure == FileStructureType.Directory)
            {
                foreach (string file in Directory.GetFiles(SourcePath, "*.cs", SearchOption.AllDirectories))
                {
                    SourceFilePaths.Add(file);
                }
                foreach (string resx in Directory.GetFiles(SourcePath, ".resx", SearchOption.AllDirectories))
                {
                    AddEmbeddedResource(resx);
                }
            }
            else
            {
                SourceFilePaths.Add(SourcePath);
            }
        }

        /// <summary>
        /// Parses source files for compiler options.
        /// </summary>
        private void ParseCompilerOptions()
        {
            foreach (string filePath in SourceFilePaths)
            {
                string[] lines = File.ReadAllLines(filePath);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("//!CompilerOption:"))
                    {
                        string[] parts = trimmed.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);

                        switch (parts[1])
                        {
                            case "AddRef":
                                if (parts.Length == 3 && !string.IsNullOrEmpty(parts[2]) && parts[2].EndsWith(".dll"))
                                {
                                    AddReference(parts[2]);
                                }
                                break;

                            case "Optimise":
                            case "Optimize":
                                if (parts.Length == 3 && !string.IsNullOrEmpty(parts[2]) && parts[2] == "On" 
                                    && !Options.CompilerOptions.Contains("/optimise"))
                                {
                                    Options.IncludeDebugInformation = false;
                                    Options.CompilerOptions += " /optimise";
                                }
                                break;

                            case "Version":
                                if (parts.Length == 3 && !string.IsNullOrEmpty(parts[2]) && parts[2] == "v4.0" 
                                    && FrameworkVersionDetection.DotNet4Installed)
                                {
                                    CompilerVersion = 4f;
                                }
                                break;
                        }
                    }
                }
            }
        }

        // ── Persistent compile cache ───────────────────────────────────────────────
        // CB recompiles every drop-in (plugins/routines/bots) from scratch on every launch, and it
        // restarts a lot (relogger/crash recovery). This caches each compiled assembly on disk keyed
        // by its source content + the CopilotBuddy.dll identity (so an API change invalidates every
        // cached build) + compiler options. Fail-safe: any cache miss/error falls back to a normal
        // Roslyn compile, so worst case is exactly the old behaviour.

        private static string _cacheDir;
        internal static string CacheDir
        {
            get
            {
                if (_cacheDir == null)
                {
                    string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Path.GetTempPath();
                    _cacheDir = Path.Combine(baseDir, "CompileCache");
                    try { Directory.CreateDirectory(_cacheDir); } catch { }
                }
                return _cacheDir;
            }
        }

        // Changes whenever CopilotBuddy.dll is rebuilt (module MVID — deterministic builds tie it to
        // content) plus write-time/size as belt-and-suspenders. This is what stops a plugin cached
        // against an old API from being loaded after a DLL update.
        private static string _apiIdentity;
        private static string ApiIdentity
        {
            get
            {
                if (_apiIdentity == null)
                {
                    try
                    {
                        Assembly api = Assembly.GetExecutingAssembly();
                        Guid mvid = api.ManifestModule.ModuleVersionId;
                        var fi = new FileInfo(api.Location);
                        _apiIdentity = $"{mvid:N}_{fi.LastWriteTimeUtc.Ticks}_{fi.Length}";
                    }
                    catch { _apiIdentity = Guid.NewGuid().ToString("N"); }  // unknown → never reuse cache
                }
                return _apiIdentity;
            }
        }

        private string BaseName => FileStructure == FileStructureType.SingleFile
            ? Path.GetFileNameWithoutExtension(SourcePath)
            : new DirectoryInfo(SourcePath).Name;

        private string ComputeCacheKey()
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            {
                bw.Write("v1");
                bw.Write(ApiIdentity);
                bw.Write(Options.CompilerOptions ?? "");
                bw.Write(Options.IncludeDebugInformation);
                bw.Write(CompilerVersion);
                foreach (string f in SourceFilePaths.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    bw.Write(f);
                    bw.Write(File.ReadAllBytes(f));
                }
            }
            ms.Position = 0;
            return Convert.ToHexString(sha.ComputeHash(ms)).Substring(0, 32);
        }

        // Keep only the current build for this drop-in; drop older-hash entries so the cache is bounded.
        private static void PruneStaleCache(string baseName, string keepPath)
        {
            try
            {
                foreach (string f in Directory.GetFiles(CacheDir, baseName + "_*.dll"))
                    if (!string.Equals(f, keepPath, StringComparison.OrdinalIgnoreCase))
                        try { File.Delete(f); } catch { }
            }
            catch { }
        }

        /// <summary>
        /// Compiles the source files using Roslyn compiler.
        /// </summary>
        /// <returns>The compiler results.</returns>
        public CompilerResults Compile()
        {
            CollectSourceFiles();
            ParseCompilerOptions();

            if (SourceFilePaths.Count == 0)
            {
                return null;
            }

            // Stable name = drop-in + content/API hash → identical across launches (so a cache hit's
            // assembly name matches the trust prefix) and new when the source or API changes.
            string stableName = BaseName;
            string cachePath = null;
            try
            {
                stableName = BaseName + "_" + ComputeCacheKey();
                cachePath = Path.Combine(CacheDir, stableName + ".dll");
            }
            catch { cachePath = null; }   // hashing failed → compile normally, no cache

            // Add to trusted assemblies list (prefix match — must be set before the assembly loads).
            AssemblyVerifier.TrustedAssemblies.Add(stableName);

            // Cache hit — load the prior build and skip Roslyn entirely. Any failure (missing/corrupt/
            // API-incompatible) falls through to a normal compile below.
            if (cachePath != null && File.Exists(cachePath))
            {
                try
                {
                    Assembly cached = Assembly.LoadFrom(cachePath);
                    if (cached != null && cached.GetTypes().Length > 0)
                    {
                        CompiledAssembly = cached;
                        try { File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow); } catch { }
                        var hitResults = new CompilerResults(new TempFileCollection(Path.GetTempPath()));
                        typeof(CompilerResults)
                            .GetField("compiledAssembly", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.SetValue(hitResults, CompiledAssembly);
                        return hitResults;
                    }
                }
                catch { /* incompatible/corrupt cache → recompile */ }
            }

            // Parse all source files into syntax trees
            var syntaxTrees = new List<SyntaxTree>();
            foreach (var sourceFile in SourceFilePaths)
            {
                try
                {
                    var code = File.ReadAllText(sourceFile);
                    var syntaxTree = CSharpSyntaxTree.ParseText(code,
                        path: sourceFile,
                        options: new CSharpParseOptions(LanguageVersion.Latest));
                    syntaxTrees.Add(syntaxTree);
                }
                catch (Exception ex)
                {
                    // Create error result
                    var errorResults = new CompilerResults(new TempFileCollection(Path.GetTempPath()));
                    errorResults.Errors.Add(new CompilerError(sourceFile, 0, 0, "Parse", 
                        $"Failed to parse source file: {ex.Message}"));
                    return errorResults;
                }
            }

            // Collect references from loaded assemblies
            var references = new List<MetadataReference>();
            foreach (var assemblyPath in Options.ReferencedAssemblies)
            {
                try
                {
                    string resolvedPath = assemblyPath;

                    // If bare filename (e.g. "System.Design.dll"), try to resolve from loaded assemblies
                    // or shared framework directories
                    if (!string.IsNullOrWhiteSpace(resolvedPath) && !File.Exists(resolvedPath) 
                        && !Path.IsPathRooted(resolvedPath))
                    {
                        // Try loaded assemblies first
                        var loaded = AppDomain.CurrentDomain.GetAssemblies();
                        var match = loaded.FirstOrDefault(a => 
                            !string.IsNullOrEmpty(a.Location) &&
                            Path.GetFileName(a.Location).Equals(resolvedPath, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            resolvedPath = match.Location;
                        }
                        else
                        {
                            // Try to load by assembly name
                            try
                            {
                                var asm = Assembly.Load(new AssemblyName(Path.GetFileNameWithoutExtension(resolvedPath)));
                                if (asm != null && !string.IsNullOrEmpty(asm.Location))
                                    resolvedPath = asm.Location;
                            }
                            catch { }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
                        continue;

                    references.Add(MetadataReference.CreateFromFile(resolvedPath));
                }
                catch
                {
                    // Skip assemblies we cannot reference
                }
            }

            // Add essential .NET runtime references
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimeDir != null)
            {
                var essentialRefs = new[] {
                    "System.Runtime.dll",
                    "System.Collections.dll",
                    "netstandard.dll",
                    "System.Drawing.Primitives.dll",
                    "System.Text.RegularExpressions.dll",
                    "System.Linq.dll",
                    "System.Linq.Expressions.dll",
                    "System.Data.Common.dll",
                    // Type-forwarded APIs required by dynamic plugins
                    "System.IO.FileSystem.Watcher.dll",             // FileSystemWatcher, FileSystemEventArgs, NotifyFilters
                    "System.Private.DataContractSerialization.dll", // DataContractSerializer
                    "System.Net.Mail.dll",                           // System.Net.Mime.MediaTypeNames
                    "System.IO.Packaging.dll",               // System.IO.Packaging.Package
                    "System.Windows.Forms.dll",
                    "System.Drawing.Common.dll",
                    // Modern .NET plugin support
                    "System.Text.Json.dll",                         // JsonSerializer, JsonPropertyName, etc.
                    "System.Text.Encodings.Web.dll",                 // JsonSerializerOptions encoder
                    "System.Buffers.dll",                            // ArrayPool used by System.Text.Json
                    // WCF support for plugins that use NetNamedPipeBinding (e.g. HBRelogHelper)
                    "System.ServiceModel.dll",
                };
                foreach (var refName in essentialRefs)
                {
                    var refPath = Path.Combine(runtimeDir, refName);
                    if (File.Exists(refPath) && !references.Any(r => r.Display?.Contains(refName) == true))
                    {
                        references.Add(MetadataReference.CreateFromFile(refPath));
                    }
                }
            }

            // Add System.Drawing.Common from application directory (NuGet package)
            var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (appDir != null)
            {
                var drawingCommonPath = Path.Combine(appDir, "System.Drawing.Common.dll");
                if (File.Exists(drawingCommonPath) && !references.Any(r => r.Display?.Contains("System.Drawing.Common") == true))
                {
                    references.Add(MetadataReference.CreateFromFile(drawingCommonPath));
                }

                // WCF assemblies ship via NuGet (System.ServiceModel.* packages) and are
                // copied next to CopilotBuddy.exe. They are NOT in the .NET shared runtime
                // since .NET 5+, so we have to load them from the application directory.
                // HBRelogHelper needs ChannelFactory<>, NetNamedPipeBinding, EndpointAddress,
                // ServiceContractAttribute, OperationContractAttribute, CommunicationState.
                var wcfAssemblies = new[] {
                    "System.ServiceModel.dll",
                    "System.ServiceModel.Primitives.dll",
                    "System.ServiceModel.NetNamedPipe.dll",
                };
                foreach (var asmName in wcfAssemblies)
                {
                    var asmPath = Path.Combine(appDir, asmName);
                    if (File.Exists(asmPath) && !references.Any(r => r.Display?.Contains(asmName) == true))
                    {
                        references.Add(MetadataReference.CreateFromFile(asmPath));
                    }
                }
            }

            // Determine optimization level from compiler options
            var optimizationLevel = Options.CompilerOptions?.Contains("/optimise") == true
                ? OptimizationLevel.Release
                : OptimizationLevel.Debug;

            // Create compilation
            var compilation = CSharpCompilation.Create(
                assemblyName: stableName,
                syntaxTrees: syntaxTrees,
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOptimizationLevel(optimizationLevel)
                    .WithPlatform(Platform.AnyCpu));

            // Emit to the cache path (a stable file reused next launch) so the loaded assembly has a
            // valid Location. If the cache file is locked (e.g. an incompatible build is already
            // loaded), fall back to a unique temp path and skip caching this one.
            string emitPath = cachePath ?? Path.Combine(Path.GetTempPath(), $"{stableName}_{Guid.NewGuid():N}.dll");
            EmitResult emitResult;
            try { emitResult = compilation.Emit(emitPath); }
            catch (IOException)
            {
                emitPath = Path.Combine(Path.GetTempPath(), $"{stableName}_{Guid.NewGuid():N}.dll");
                cachePath = null;
                emitResult = compilation.Emit(emitPath);
            }

            // Create CompilerResults for compatibility
            var results = new CompilerResults(new TempFileCollection(Path.GetTempPath()));

            if (!emitResult.Success)
            {
                // Clean up the (empty) output file on failure.
                try { if (File.Exists(emitPath)) File.Delete(emitPath); } catch { }

                foreach (var diagnostic in emitResult.Diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error || 
                        diagnostic.Severity == DiagnosticSeverity.Warning)
                    {
                        var lineSpan = diagnostic.Location.GetLineSpan();
                        var error = new CompilerError
                        {
                            FileName = lineSpan.Path ?? "",
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            ErrorNumber = diagnostic.Id,
                            ErrorText = diagnostic.GetMessage(),
                            IsWarning = diagnostic.Severity == DiagnosticSeverity.Warning
                        };
                        results.Errors.Add(error);
                    }
                }
            }
            else
            {
                CompiledAssembly = Assembly.LoadFrom(emitPath);
                if (cachePath != null)
                    PruneStaleCache(BaseName, cachePath);

                // Set the compiled assembly in results
                typeof(CompilerResults)
                    .GetField("compiledAssembly", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(results, CompiledAssembly);
            }

            return results;
        }

        public enum FileStructureType
        {
            SingleFile,
            Directory
        }
    }
}
