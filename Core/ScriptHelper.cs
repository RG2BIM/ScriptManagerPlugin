using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ScriptManagerPlugin
{
    public static class ScriptHelper
    {
        public static void SafeTask(Action action)
        {
            Task.Run(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Фоновая ошибка] {ex.Message}\n{ex.StackTrace}");
                    ScriptManager.Logger?.LogError(ex, "Фоновая ошибка");
                }
            });
        }

        public static void SafeTask(Func<Task> func)
        {
            Task.Run(async () =>
            {
                try
                {
                    await func();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Фоновая ошибка] {ex.Message}\n{ex.StackTrace}");
                    ScriptManager.Logger?.LogError(ex, "Фоновая ошибка");
                }
            });
        }

        public static void SafeTaskWithUI<T>(Func<T> bgAction, Action<T> uiAction)
        {
            Task.Run(() =>
            {
                try
                {
                    T result = bgAction();
                    RunUI(() =>
                    {
                        try
                        {
                            uiAction(result);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Ошибка в UI-потоке] {ex.Message}\n{ex.StackTrace}");
                            ScriptManager.Logger?.LogError(ex, "Ошибка в UI-потоке");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Фоновая ошибка] {ex.Message}\n{ex.StackTrace}");
                    ScriptManager.Logger?.LogError(ex, "Фоновая ошибка");
                }
            });
        }

        public static void RunUI(Action action)
        {
            var invoker = ScriptManager.UiInvoker;
            if (invoker != null && !invoker.IsDisposed)
            {
                if (invoker.InvokeRequired)
                {
                    try
                    {
                        invoker.Invoke(action);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                    return;
                }
            }
            action();
        }
    }
}
