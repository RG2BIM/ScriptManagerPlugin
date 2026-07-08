using System;

namespace ScriptManagerPlugin
{
    public class ScriptCache : IDisposable
    {
        public CompilationResult Result { get; private set; }
        public DateTime LastWriteTime { get; private set; }
        public int LastFileCount { get; private set; }
        private object _lastInstance;

        public bool NeedsRecompile(DateTime currentWriteTime, int currentFileCount)
        {
            return Result == null || currentWriteTime != LastWriteTime || currentFileCount != LastFileCount;
        }

        public void Update(CompilationResult result, DateTime writeTime, int fileCount, object instance)
        {
            // Если сборка та же самая, мы не должны её выгружать. Очищаем только старый экземпляр.
            if (Result != result)
            {
                ClearAssembly();
            }
            ClearInstance();
            
            Result = result;
            LastWriteTime = writeTime;
            LastFileCount = fileCount;
            _lastInstance = instance;
        }

        public void Clear()
        {
            ClearInstance();
            ClearAssembly();
        }

        private void ClearInstance()
        {
            try
            {
                if (_lastInstance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex) { ScriptManager.Logger?.LogError(ex, "Ошибка при освобождении экземпляра скрипта"); }
            _lastInstance = null;
        }

        private void ClearAssembly()
        {
            if (Result != null && Result.LoadContext != null)
            {
                try
                {
                    Result.LoadContext.Unload();
                }
                catch (Exception ex) { ScriptManager.Logger?.LogError(ex, "Ошибка при выгрузке контекста сборки"); }
                
                // Удаляем все сильные ссылки, необходимые для сборки мусора ALC
                Result.Type = null;
                Result.Method = null;
                Result.LoadContext = null;
                Result = null;
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
