using System;

namespace ScriptManagerPlugin
{
    // Класс восстановлен для обратной совместимости старых скриптов, 
    // которые явно вызывают ScriptManagerPlugin.OutputConsole.Print(...)
    public static class OutputConsole
    {
        public static void Print(object msg)
        {
            ScriptManager.Logger?.LogInfo(msg);
        }
        
        public static void Clear()
        {
            ScriptManager.Logger?.Clear();
        }
    }
}
