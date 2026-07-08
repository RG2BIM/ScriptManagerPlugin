using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Renga;

namespace ScriptManagerPlugin
{
    public class ScriptActionHandler : IDisposable
    {
        private readonly ActionEventSource _eventSource;
        private readonly ScriptEngine _engine;
        private readonly string _scriptName;
        private bool _isExecuting;

        public ScriptActionHandler(IApplication rengaApp, IAction action, string buttonFolder, string scriptsFolder)
        {
            _scriptName = new DirectoryInfo(buttonFolder).Name;
            string pluginDir = Path.GetDirectoryName(typeof(ScriptManager).Assembly.Location);
            
            _engine = new ScriptEngine(rengaApp, buttonFolder, scriptsFolder, pluginDir);
            
            _eventSource = new ActionEventSource(action);
            _eventSource.Triggered += OnActionTriggered;
        }

        public void Dispose()
        {
            if (_eventSource != null)
            {
                _eventSource.Triggered -= OnActionTriggered;
            }
            _engine.Dispose();
        }

        private async void OnActionTriggered(object sender, EventArgs e)
        {
            if (_isExecuting) return;
            _isExecuting = true;
            try
            {
                await _engine.ExecuteAsync();
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
            finally
            {
                _isExecuting = false;
            }
        }

        private void HandleError(Exception ex)
        {
            string errorMsg = ex.Message;
            
            if (ex is TargetInvocationException targetEx && targetEx.InnerException != null)
            {
                errorMsg = $"{targetEx.InnerException.Message}\n{targetEx.InnerException.StackTrace}";
            }
            else if (ex is CompilationException || ex is RemoteScriptException)
            {
                errorMsg = ex.Message;
            }
            else
            {
                errorMsg = $"{ex.Message}\n{ex.StackTrace}";
            }

            if (!(ex is RemoteScriptException))
            {
                ScriptManager.Logger?.LogError(ex, $"Ошибка скрипта '{_scriptName}'");
            }
            
            MessageBox.Show($"Ошибка скрипта '{_scriptName}':\n\n{errorMsg}", "Script Manager Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
