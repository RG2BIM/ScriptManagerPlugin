namespace ScriptManagerPlugin
{
    public static class Constants
    {
        // Папки и расширения
        public const string ScriptsFolder = "Scripts";
        public const string LibFolder = "lib";
        public const string FolderExtension = ".folder";
        public const string ButtonExtension = ".button";
        public const string ActionsExtension = ".actions";
        public const string ContextExtension = ".context";

        // Файлы
        public const string ConfigFileName = "config.json";
        public const string IconFileName = "icon.png";
        public const string CSharpExtension = "*.cs";
        public const string DllExtension = "*.dll";

        // Компиляция
        public const string UseLibPragma = "//@UseLib";
        public const string ScriptClassName = "SubmissionClass";
        public const string ExecuteMethodName = "Execute";
        public const string FactoryMethodName = "<Factory>";

        // Разное
        public const string SeparatorItem = "---";
        public const string DynamicAssemblyPrefix = "DynamicAssembly_";
    }
}
