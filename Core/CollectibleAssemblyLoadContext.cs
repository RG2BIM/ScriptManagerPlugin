using System;
using System.Reflection;
using System.Runtime.Loader;

namespace ScriptManagerPlugin
{
    public class CollectibleAssemblyLoadContext : AssemblyLoadContext
    {
        public CollectibleAssemblyLoadContext() : base(isCollectible: true)
        {
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Переиспользуем сборки, уже загруженные хостом.
            // Без этого CLR может повторно загружать зависимости в ALC,
            // что вызывает проблемы с приведением типов (InvalidCastException).
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.IsDynamic && asm.GetName().Name == assemblyName.Name)
                {
                    return asm;
                }
            }
            return null;
        }
    }
}
