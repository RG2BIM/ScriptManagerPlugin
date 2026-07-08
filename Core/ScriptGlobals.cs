using System;
using Renga;

namespace ScriptManagerPlugin
{
    public class ScriptGlobals
    {
        public Renga.IApplication RengaApp { get; set; }
        
        public void Print(object msg) => ScriptManager.Logger?.LogInfo(msg);
        public void SafeTask(Action action) => ScriptHelper.SafeTask(action);
        public void SafeTask(Func<System.Threading.Tasks.Task> func) => ScriptHelper.SafeTask(func);
        public void SafeTaskWithUI<T>(Func<T> bgAction, Action<T> uiAction) => ScriptHelper.SafeTaskWithUI(bgAction, uiAction);
        public void RunUI(Action action) => ScriptHelper.RunUI(action);
    }
}
