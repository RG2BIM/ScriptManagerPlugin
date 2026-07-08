using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ScriptManagerPlugin
{
    public class CompilationResult
    {
        public Type Type { get; set; }
        public System.Reflection.MethodInfo Method { get; set; }
        public CollectibleAssemblyLoadContext LoadContext { get; set; }
        public bool IsClassBased { get; set; }
    }

    public class CompilationException : Exception
    {
        public CompilationException(string message) : base(message) { }
    }

    public class ScriptCompiler
    {
        private static readonly Regex UseLibRegex = new Regex(@"^\s*//\s*@UseLib", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        // Кэш для MetadataReference. Создается один раз на процесс.
        private static List<MetadataReference> _commonReferencesCache;
        private static readonly object _cacheLock = new object();

        public CompilationResult Compile(string[] csFiles, string scriptsFolder, string pluginDir)
        {
            var syntaxTrees = new List<SyntaxTree>();
            bool useLib = false;

            // Читаем файлы и ищем директиву
            foreach (var f in csFiles)
            {
                string content = File.ReadAllText(f);
                if (!useLib && UseLibRegex.IsMatch(content))
                {
                    useLib = true;
                }
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(content, path: f));
            }

            if (useLib)
            {
                string libFolder = Path.Combine(scriptsFolder, Constants.LibFolder);
                if (Directory.Exists(libFolder))
                {
                    foreach (var f in Directory.GetFiles(libFolder, Constants.CSharpExtension))
                    {
                        syntaxTrees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f));
                    }
                }
            }

            bool hasGlobalStatements = syntaxTrees.Any(tree => 
                tree.GetRoot().DescendantNodes().OfType<GlobalStatementSyntax>().Any());
            bool isClassBased = !hasGlobalStatements && syntaxTrees.Any(tree => 
                tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Any());

            // Если это Top-Level Script, парсим как один SourceCodeKind.Script
            if (!isClassBased)
            {
                syntaxTrees.Clear();
                var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Script);

                var sb = new System.Text.StringBuilder();
                foreach (var f in csFiles)
                {
                    sb.AppendLine(File.ReadAllText(f));
                    sb.AppendLine();
                }

                if (useLib)
                {
                    string libFolder = Path.Combine(scriptsFolder, Constants.LibFolder);
                    if (Directory.Exists(libFolder))
                    {
                        foreach (var f in Directory.GetFiles(libFolder, Constants.CSharpExtension))
                        {
                            sb.AppendLine(File.ReadAllText(f));
                            sb.AppendLine();
                        }
                    }
                }

                syntaxTrees.Add(CSharpSyntaxTree.ParseText(sb.ToString(), parseOptions));
            }

            var references = GetReferences(useLib, scriptsFolder);

            string assemblyName = Constants.DynamicAssemblyPrefix + Guid.NewGuid().ToString("N");
            CSharpCompilation compilation;

            if (isClassBased)
            {
                compilation = CSharpCompilation.Create(
                    assemblyName,
                    syntaxTrees,
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                );
            }
            else
            {
                compilation = CSharpCompilation.CreateScriptCompilation(
                    assemblyName,
                    syntaxTree: syntaxTrees[0],
                    references: references,
                    options: new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary, 
                        scriptClassName: Constants.ScriptClassName,
                        usings: new[] { 
                            "System", 
                            "System.IO", 
                            "System.Collections.Generic", 
                            "System.Linq", 
                            "System.Threading.Tasks" 
                        }),
                    globalsType: typeof(ScriptGlobals)
                );
            }

            var loadContext = new CollectibleAssemblyLoadContext();
            loadContext.Resolving += (context, assemblyNameArg) =>
            {
                string requestedName = assemblyNameArg.Name;
                string assemblyPath = Path.Combine(pluginDir, requestedName + ".dll");
                if (File.Exists(assemblyPath))
                {
                    return context.LoadFromAssemblyPath(assemblyPath);
                }

                string libDir = Path.Combine(scriptsFolder, Constants.LibFolder);
                string libAssemblyPath = Path.Combine(libDir, requestedName + ".dll");
                if (File.Exists(libAssemblyPath))
                {
                    return context.LoadFromAssemblyPath(libAssemblyPath);
                }
                return null;
            };

            using (var ms = new MemoryStream())
            {
                var emitResult = compilation.Emit(ms);
                if (!emitResult.Success)
                {
                    var errors = string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                    throw new CompilationException("Ошибка компиляции сборки:\n" + errors);
                }

                ms.Seek(0, SeekOrigin.Begin);
                var assembly = loadContext.LoadFromStream(ms);
                Type targetType = null;
                System.Reflection.MethodInfo executeMethod = null;

                if (isClassBased)
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        var method = type.GetMethods().FirstOrDefault(m => 
                            m.Name == Constants.ExecuteMethodName && 
                            m.GetParameters().Length >= 1 && 
                            m.GetParameters()[0].ParameterType.Name.Contains("IApplication"));
                            
                        if (method != null)
                        {
                            targetType = type;
                            executeMethod = method;
                            break;
                        }
                    }
                }
                else
                {
                    targetType = assembly.GetType(Constants.ScriptClassName);
                    executeMethod = targetType?.GetMethod(Constants.FactoryMethodName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                }

                if (targetType == null || executeMethod == null)
                {
                    string debugInfo = "";
                    if (isClassBased && assembly != null)
                    {
                        foreach (var t in assembly.GetTypes())
                        {
                            debugInfo += $"Class {t.Name}: ";
                            foreach (var m in t.GetMethods())
                            {
                                debugInfo += $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName))}) | ";
                            }
                            debugInfo += "\n";
                        }
                    }
                    throw new CompilationException(isClassBased 
                        ? $"Не найден подходящий класс с методом '{Constants.ExecuteMethodName}...'. Available:\n{debugInfo}"
                        : $"Не удалось получить точку входа скрипта {Constants.ScriptClassName}.{Constants.FactoryMethodName}.");
                }

                return new CompilationResult
                {
                    Type = targetType,
                    Method = executeMethod,
                    LoadContext = loadContext,
                    IsClassBased = isClassBased
                };
            }
        }

        private List<MetadataReference> GetReferences(bool useLib, string scriptsFolder)
        {
            var references = new List<MetadataReference>();

            if (_commonReferencesCache == null)
            {
                lock (_cacheLock)
                {
                    if (_commonReferencesCache == null)
                    {
                        var cache = new List<MetadataReference>();
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                                {
                                    cache.Add(MetadataReference.CreateFromFile(assembly.Location));
                                }
                            }
                            catch (Exception ex) 
                            { 
                                System.Diagnostics.Debug.WriteLine(ex); 
                            }
                        }
                        _commonReferencesCache = cache;
                    }
                }
            }

            references.AddRange(_commonReferencesCache);

            if (useLib)
            {
                string libFolder = Path.Combine(scriptsFolder, Constants.LibFolder);
                if (Directory.Exists(libFolder))
                {
                    foreach (var dll in Directory.GetFiles(libFolder, Constants.DllExtension))
                    {
                        references.Add(MetadataReference.CreateFromFile(dll));
                    }
                }
            }

            return references;
        }
    }
}
