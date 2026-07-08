using System.Collections.Generic;

namespace ScriptManagerPlugin
{
    public class ConfigData
    {
        public string Title { get; set; }
        public string ToolTip { get; set; }
        public string ViewType { get; set; }
        public string ShowCase { get; set; }
        public List<string> Items { get; set; } = new List<string>();
    }
}
