using System;

namespace ScriptManagerPlugin
{
    public interface ILogger
    {
        void LogInfo(object message);
        void LogError(Exception exception, string contextMessage = null);
        void LogError(string message);
        void Clear();
    }
}
