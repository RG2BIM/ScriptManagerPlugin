using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Renga;

namespace ScriptManagerPlugin
{
    public class RemoteScriptException : Exception
    {
        public RemoteScriptException(string message) : base(message) { }
    }

    public class ScriptEngine : IDisposable
    {
        private readonly ScriptCache _cache;
        private readonly ScriptCompiler _compiler;
        private readonly ScriptExecutor _executor;
        private readonly string _buttonFolder;
        private readonly string _scriptsFolder;
        private readonly string _pluginDir;

        public ScriptEngine(IApplication rengaApp, string buttonFolder, string scriptsFolder, string pluginDir)
        {
            _buttonFolder = buttonFolder;
            _scriptsFolder = scriptsFolder;
            _pluginDir = pluginDir;
            
            _cache = new ScriptCache();
            _compiler = new ScriptCompiler();
            _executor = new ScriptExecutor(rengaApp);
        }

        public async Task ExecuteAsync()
        {
            // Очищаем консоль перед запуском нового скрипта
            ScriptManager.Logger?.Clear();

            string[] csFiles = Directory.GetFiles(_buttonFolder, Constants.CSharpExtension);
            if (csFiles.Length == 0)
            {
                await ExecuteRemoteAsync();
                return;
            }

            DateTime currentWriteTime = csFiles.Max(f => File.GetLastWriteTime(f));
            
            if (_cache.NeedsRecompile(currentWriteTime, csFiles.Length))
            {
                // Очищаем кэш до компиляции, чтобы старая сборка могла выгрузиться
                _cache.Clear();

                var compilationResult = await Task.Run(() => 
                    _compiler.Compile(csFiles, _scriptsFolder, _pluginDir));
                
                object instance = await _executor.ExecuteAsync(compilationResult);
                
                _cache.Update(compilationResult, currentWriteTime, csFiles.Length, instance);
            }
            else
            {
                if (_cache.Result == null || _cache.Result.Method == null)
                {
                    throw new CompilationException("Не удалось получить скомпилированный скрипт из кэша. Пожалуйста, измените скрипт для его перекомпиляции.");
                }

                object instance = await _executor.ExecuteAsync(_cache.Result);
                _cache.Update(_cache.Result, currentWriteTime, csFiles.Length, instance);
            }
        }

        private async Task ExecuteRemoteAsync()
        {
            string configPath = Path.Combine(_scriptsFolder, "server_config.json");
            if (!File.Exists(configPath))
            {
                configPath = Path.Combine(_pluginDir, "server_config.json");
                if (!File.Exists(configPath))
                {
                    throw new RemoteScriptException("Для работы удаленных скриптов необходимо создать файл server_config.json в корневой папке Scripts или рядом с плагином!");
                }
            }

            ServerConfig config = null;
            try
            {
                string json = File.ReadAllText(configPath);
                config = System.Text.Json.JsonSerializer.Deserialize<ServerConfig>(json);
            }
            catch (Exception)
            {
                throw new RemoteScriptException("Не удалось прочитать server_config.json (неверный формат файла).");
            }

            if (config == null || string.IsNullOrWhiteSpace(config.BaseUrl))
            {
                throw new RemoteScriptException("Файл server_config.json не содержит BASE_URL.");
            }

            string remoteManifestPath = Path.Combine(_buttonFolder, "remote_files.json");
            var remoteFilesToDownload = new System.Collections.Generic.List<string> { "script.cs" };

            if (File.Exists(remoteManifestPath))
            {
                try
                {
                    string manifestJson = File.ReadAllText(remoteManifestPath);
                    var files = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(manifestJson);
                    if (files != null && files.Count > 0)
                    {
                        remoteFilesToDownload = files;
                    }
                }
                catch (Exception)
                {
                    throw new RemoteScriptException("Файл remote_files.json имеет неверный формат!");
                }
            }

            // Вычисляем относительный путь: заменяем Scripts на SMPRenga
            string relativePath = _buttonFolder.Substring(_scriptsFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            relativePath = relativePath.Replace('\\', '/');
            
            var tempFiles = new System.Collections.Generic.List<string>();
            string combinedCodeStr = "";

            using (var client = new System.Net.Http.HttpClient())
            {
                if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
                {
                    // Расшифровываем пароль из Base64
                    string realPassword = config.Password;
                    try
                    {
                        var base64EncodedBytes = Convert.FromBase64String(config.Password);
                        realPassword = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
                    }
                    catch { /* Если пароль не в base64, используем как есть */ }

                    var authBytes = System.Text.Encoding.ASCII.GetBytes($"{config.Username}:{realPassword}");
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                }

                try
                {
                    foreach (var fileName in remoteFilesToDownload)
                    {
                        string remoteUrl = $"{config.BaseUrl.TrimEnd('/')}/SMPRenga/{relativePath}/{fileName}";
                        string remoteCode = null;

                        try
                        {
                            remoteCode = await client.GetStringAsync(remoteUrl);
                        }
                        catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("404"))
                        {
                            throw new RemoteScriptException("Скрипта нету!");
                        }
                        catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("403"))
                        {
                            throw new RemoteScriptException("Нет прав доступа к серверу!");
                        }
                        catch (Exception)
                        {
                            throw new RemoteScriptException("Сервер не доступен!");
                        }

                        combinedCodeStr += remoteCode;
                        
                        string tempFile = Path.Combine(Path.GetTempPath(), "ScriptManager_Remote_" + Guid.NewGuid().ToString("N") + ".cs");
                        File.WriteAllText(tempFile, remoteCode);
                        tempFiles.Add(tempFile);
                    }

                    // Используем хэш всего скачанного кода для кэширования
                    int currentHash = combinedCodeStr.GetHashCode();

                    if (_cache.NeedsRecompile(DateTime.MinValue, currentHash))
                    {
                        _cache.Clear();

                        CompilationResult compilationResult = await Task.Run(() => 
                            _compiler.Compile(tempFiles.ToArray(), _scriptsFolder, _pluginDir));
                        
                        object instance = await _executor.ExecuteAsync(compilationResult);
                        
                        _cache.Update(compilationResult, DateTime.MinValue, currentHash, instance);
                    }
                    else
                    {
                        if (_cache.Result == null || _cache.Result.Method == null)
                            throw new CompilationException("Не удалось получить скрипт из кэша.");

                        object instance = await _executor.ExecuteAsync(_cache.Result);
                        _cache.Update(_cache.Result, DateTime.MinValue, currentHash, instance);
                    }
                }
                finally
                {
                    foreach (var tempFile in tempFiles)
                    {
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                    }
                }
            }
        }

        public void Dispose()
        {
            _cache.Dispose();
        }
    }
}
