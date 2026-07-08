using System;
using System.Reflection;
using System.Threading.Tasks;
using Renga;

namespace ScriptManagerPlugin
{
    public class ScriptExecutor
    {
        private readonly IApplication _rengaApp;

        public ScriptExecutor(IApplication rengaApp)
        {
            _rengaApp = rengaApp;
        }

        public async Task<object> ExecuteAsync(CompilationResult compilationResult)
        {
            if (compilationResult.IsClassBased)
            {
                return ExecuteClassBased(compilationResult);
            }
            else
            {
                return await ExecuteScriptBasedAsync(compilationResult);
            }
        }

        private object ExecuteClassBased(CompilationResult result)
        {
            var instance = Activator.CreateInstance(result.Type);
            var method = result.Method;
            var parameters = method.GetParameters();

            if (parameters.Length == 1)
            {
                method.Invoke(instance, new object[] { _rengaApp });
            }
            else if (parameters.Length >= 2)
            {
                if (parameters[1].ParameterType.FullName != null && parameters[1].ParameterType.FullName.Contains("System.String"))
                {
                    Action<string> printStrAction = (msg) => ScriptManager.Logger?.LogInfo(msg);
                    InvokeWithPrintAction(instance, method, printStrAction, parameters[1].ParameterType);
                }
                else
                {
                    Action<object> printAction = (msg) => ScriptManager.Logger?.LogInfo(msg);
                    InvokeWithPrintAction(instance, method, printAction, parameters[1].ParameterType);
                }
            }
            
            return instance;
        }

        private void InvokeWithPrintAction(object instance, MethodInfo method, Delegate action, Type targetDelegateType)
        {
            try 
            {
                method.Invoke(instance, new object[] { _rengaApp, action });
            } 
            catch (ArgumentException) 
            {
                var del = Delegate.CreateDelegate(targetDelegateType, action.Target, action.Method);
                method.Invoke(instance, new object[] { _rengaApp, del });
            }
        }

        private async Task<object> ExecuteScriptBasedAsync(CompilationResult result)
        {
            var globals = new ScriptGlobals { RengaApp = _rengaApp };
            var submissionStates = new object[2];
            submissionStates[0] = globals;
            
            var task = (Task<object>)result.Method.Invoke(null, new object[] { submissionStates });
            if (task != null)
            {
                await task;
            }
            return null;
        }
    }
}
