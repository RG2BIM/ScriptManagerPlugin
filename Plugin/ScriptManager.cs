using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Renga;

namespace ScriptManagerPlugin
{
    public class ScriptManager : IPlugin
    {
        private static string _staticScriptsFolder;
        
        public static ILogger Logger { get; private set; }
        public static Control UiInvoker { get; private set; }

        private IApplication _rengaApp;
        private IUI _ui;
        private string _scriptsFolder;
        private readonly List<ScriptActionHandler> _handlers = new List<ScriptActionHandler>();

        static ScriptManager()
        {
            try
            {
                // Предварительная загрузка сборок
                _ = typeof(System.ComponentModel.BindingList<object>).Assembly;
                _ = typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly;
                _ = typeof(System.Collections.ReadOnlyCollectionBase).Assembly;
            }
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine(ex); 
            }
        }

        public bool Initialize(string pluginFolder)
        {
            try
            {
                Logger = new OutputConsoleLogger();
                _rengaApp = new Renga.Application();
                _ui = _rengaApp.UI;

                _scriptsFolder = Path.Combine(pluginFolder, Constants.ScriptsFolder);
                _staticScriptsFolder = _scriptsFolder;

                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

                if (!Directory.Exists(_scriptsFolder))
                {
                    Directory.CreateDirectory(_scriptsFolder);
                }

                return Start();
            }
            catch (Exception ex)
            {
                ShowInitializationError(ex);
                return false;
            }
        }

        public bool Start()
        {
            try
            {
                UiInvoker = new Control();
                UiInvoker.CreateControl();

                var panelExtension = _ui.CreateUIPanelExtension();
                var rootConfig = LoadConfig(_scriptsFolder) ?? new ConfigData();

                foreach (var dir in GetOrderedItems(_scriptsFolder, rootConfig))
                {
                    if (dir == Constants.SeparatorItem) continue;
                    
                    string folderName = new DirectoryInfo(dir).Name;

                    if (folderName.EndsWith(Constants.FolderExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessFolder(dir, panelExtension);
                    }
                    else if (folderName.EndsWith(Constants.ContextExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessContextMenu(dir);
                    }
                    else if (folderName.EndsWith(Constants.ActionsExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessActions(dir);
                    }
                    else if (folderName.EndsWith(Constants.ButtonExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        var action = CreateActionFromButtonFolder(dir);
                        if (action != null)
                        {
                            panelExtension.AddToolButton(action);
                        }
                    }
                }

                _ui.AddExtensionToPrimaryPanel(panelExtension);
                return true;
            }
            catch (Exception ex)
            {
                ShowInitializationError(ex);
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

                foreach (var handler in _handlers)
                {
                    handler.Dispose();
                }
                _handlers.Clear();

                if (UiInvoker != null && !UiInvoker.IsDisposed)
                {
                    UiInvoker.Dispose();
                }
                UiInvoker = null;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Ошибка при остановке плагина");
            }
        }

        #region Обработка структурных папок

        private void ProcessFolder(string dir, IUIPanelExtension panelExtension)
        {
            var config = LoadConfig(dir) ?? new ConfigData();
            string folderName = new DirectoryInfo(dir).Name;
            string title = !string.IsNullOrEmpty(config.Title) ? config.Title : folderName.Replace(Constants.FolderExtension, "");
            
            var dropDown = CreateDropDown(dir, title, config.ToolTip);

            foreach (var btnDir in GetOrderedItems(dir, config, "*" + Constants.ButtonExtension))
            {
                if (btnDir == Constants.SeparatorItem)
                {
                    dropDown.AddSeparator();
                }
                else
                {
                    var action = CreateActionFromButtonFolder(btnDir);
                    if (action != null)
                        dropDown.AddAction(action);
                }
            }

            panelExtension.AddDropDownButton(dropDown);
        }

        private void ProcessContextMenu(string dir)
        {
            var config = LoadConfig(dir) ?? new ConfigData();
            var contextMenu = _ui.CreateContextMenu();
            
            PopulateContextMenu(contextMenu, dir, config);

            ViewType viewType = ParseEnum("ViewType_" + config.ViewType, ViewType.ViewType_Undefined);
            ContextMenuShowCase showCase = ParseEnum("ContextMenuShowCase_" + config.ShowCase, ContextMenuShowCase.ContextMenuShowCase_Selection);

            string uniqueId = Guid.NewGuid().ToString();
            _ui.AddContextMenuS(uniqueId, contextMenu, viewType, showCase);
        }

        private void ProcessActions(string dir)
        {
            var config = LoadConfig(dir) ?? new ConfigData();
            var actionsExtension = _ui.CreateUIPanelExtension();

            foreach (var itemDir in GetOrderedItems(dir, config))
            {
                if (itemDir == Constants.SeparatorItem) continue;
                string itemName = new DirectoryInfo(itemDir).Name;

                if (itemName.EndsWith(Constants.ButtonExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var action = CreateActionFromButtonFolder(itemDir);
                    if (action != null)
                    {
                        actionsExtension.AddToolButton(action);
                    }
                }
                else if (itemName.EndsWith(Constants.FolderExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var dropConfig = LoadConfig(itemDir) ?? new ConfigData();
                    string dropTitle = !string.IsNullOrEmpty(dropConfig.Title) ? dropConfig.Title : itemName.Replace(Constants.FolderExtension, "");
                    var dropDown = CreateDropDown(itemDir, dropTitle, dropConfig.ToolTip);

                    foreach (var btnDir in GetOrderedItems(itemDir, dropConfig, "*" + Constants.ButtonExtension))
                    {
                        if (btnDir == Constants.SeparatorItem)
                        {
                            dropDown.AddSeparator();
                        }
                        else
                        {
                            var action = CreateActionFromButtonFolder(btnDir);
                            if (action != null)
                                dropDown.AddAction(action);
                        }
                    }

                    actionsExtension.AddDropDownButton(dropDown);
                }
            }

            ViewType viewType = ParseEnum("ViewType_" + config.ViewType, ViewType.ViewType_Undefined);
            _ui.AddExtensionToActionsPanel(actionsExtension, viewType);
        }

        private void PopulateContextMenu(dynamic parentMenu, string dir, ConfigData parentConfig)
        {
            foreach (var itemDir in GetOrderedItems(dir, parentConfig))
            {
                if (itemDir == Constants.SeparatorItem)
                {
                    parentMenu.AddSeparator();
                    continue;
                }

                string itemName = new DirectoryInfo(itemDir).Name;

                if (itemName.EndsWith(Constants.ButtonExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var action = CreateActionFromButtonFolder(itemDir);
                    if (action != null)
                    {
                        parentMenu.AddActionItem(action);
                    }
                }
                else if (itemName.EndsWith(Constants.FolderExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var childConfig = LoadConfig(itemDir) ?? new ConfigData();
                    string title = !string.IsNullOrEmpty(childConfig.Title) ? childConfig.Title : itemName.Replace(Constants.FolderExtension, "");
                    
                    var childMenu = parentMenu.AddNodeItem();
                    childMenu.DisplayName = title;
                    
                    PopulateContextMenu(childMenu, itemDir, childConfig);
                }
            }
        }

        #endregion

        #region Фабричные и вспомогательные методы

        private IDropDownButton CreateDropDown(string dir, string title, string tooltip)
        {
            var dropDown = _ui.CreateDropDownButton();
            dropDown.ToolTip = title;
            dropDown.TextVisible = false;

            var image = LoadIcon(dir);
            if (image != null)
            {
                dropDown.Icon = image;
            }

            return dropDown;
        }

        private IAction CreateActionFromButtonFolder(string buttonFolder)
        {
            var config = LoadConfig(buttonFolder) ?? new ConfigData();
            string folderName = new DirectoryInfo(buttonFolder).Name;
            string title = !string.IsNullOrEmpty(config.Title) ? config.Title : folderName.Replace(Constants.ButtonExtension, "");
            string tooltip = config.ToolTip ?? "";

            string[] csFiles = Directory.GetFiles(buttonFolder, Constants.CSharpExtension);
            bool hasRemoteManifest = File.Exists(Path.Combine(buttonFolder, "remote_files.json"));

            // Кнопка появляется только если есть локальные скрипты или явно указан манифест удаленных файлов
            if (csFiles.Length == 0 && !hasRemoteManifest) 
                return null;

            var action = _ui.CreateAction();
            action.DisplayName = title;
            action.ToolTip = tooltip;

            var image = LoadIcon(buttonFolder);
            if (image != null)
            {
                action.Icon = image;
            }
            
            var handler = new ScriptActionHandler(_rengaApp, action, buttonFolder, _scriptsFolder);
            _handlers.Add(handler);
            return action;
        }

        private IImage LoadIcon(string directory)
        {
            string iconPath = Path.Combine(directory, Constants.IconFileName);
            if (File.Exists(iconPath))
            {
                var image = _ui.CreateImage();
                image.LoadFromFile(iconPath);
                return image;
            }
            return null;
        }

        private ConfigData LoadConfig(string folderPath)
        {
            string configPath = Path.Combine(folderPath, Constants.ConfigFileName);
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<ConfigData>(json, options);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, $"Ошибка JSON при чтении {configPath}");
                }
            }
            return null;
        }

        private IEnumerable<string> GetOrderedItems(string parentDir, ConfigData config, string searchPattern = "*")
        {
            var result = new List<string>();
            var allDirsOnDisk = new HashSet<string>(Directory.GetDirectories(parentDir, searchPattern), StringComparer.OrdinalIgnoreCase);
            
            if (config?.Items != null && config.Items.Count > 0)
            {
                foreach (var item in config.Items)
                {
                    if (item == Constants.SeparatorItem)
                    {
                        result.Add(Constants.SeparatorItem);
                    }
                    else
                    {
                        string fullPath = Path.Combine(parentDir, item);
                        if (allDirsOnDisk.Contains(fullPath))
                        {
                            result.Add(fullPath);
                            allDirsOnDisk.Remove(fullPath);
                        }
                    }
                }
            }

            var remaining = allDirsOnDisk.OrderBy(x => new DirectoryInfo(x).Name);
            result.AddRange(remaining);

            return result;
        }

        private T ParseEnum<T>(string value, T defaultValue) where T : struct
        {
            if (!string.IsNullOrEmpty(value) && Enum.TryParse(value, true, out T parsedValue))
            {
                return parsedValue;
            }
            return defaultValue;
        }

        private void ShowInitializationError(Exception ex)
        {
            MessageBox.Show($"Ошибка инициализации Script Manager Plugin:\n{ex.Message}", "Script Manager Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        #endregion

        #region Обработчики событий

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
        {
            Logger?.LogError(args.Exception, "Необработанная фоновая ошибка задачи");
            args.SetObserved();
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var requestedName = new AssemblyName(args.Name).Name;
            
            // Ищем в уже загруженных
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == requestedName)
                {
                    return assembly;
                }
            }

            try
            {
                // Ищем в папке плагина
                string pluginDir = Path.GetDirectoryName(typeof(ScriptManager).Assembly.Location);
                string assemblyPath = Path.Combine(pluginDir, requestedName + ".dll");
                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }

                // Ищем в папке lib
                if (!string.IsNullOrEmpty(_staticScriptsFolder))
                {
                    string libPath = Path.Combine(_staticScriptsFolder, Constants.LibFolder, requestedName + ".dll");
                    if (File.Exists(libPath))
                    {
                        return Assembly.LoadFrom(libPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, $"Ошибка при резолве сборки {requestedName}");
            }

            return null;
        }

        #endregion
    }
}
