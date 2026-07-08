using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ScriptManagerPlugin
{
    public class OutputConsoleLogger : ILogger
    {
        private Form _form;
        private TextBox _textBox;

        public void LogInfo(object message)
        {
            Print(message);
        }

        public void LogError(Exception exception, string contextMessage = null)
        {
            if (string.IsNullOrEmpty(contextMessage))
            {
                Print($"[Ошибка] {exception.Message}\n{exception.StackTrace}");
            }
            else
            {
                Print($"[Ошибка] {contextMessage}: {exception.Message}\n{exception.StackTrace}");
            }
        }

        public void LogError(string message)
        {
            Print($"[Ошибка] {message}");
        }

        public void Clear()
        {
            var invoker = ScriptManager.UiInvoker;
            if (invoker == null || invoker.IsDisposed) return;

            if (invoker.InvokeRequired)
            {
                try
                {
                    invoker.Invoke(new Action(() => Clear()));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                return;
            }

            if (_textBox != null && !_textBox.IsDisposed)
            {
                _textBox.Clear();
            }
        }

        private void Print(object msg)
        {
            var invoker = ScriptManager.UiInvoker;
            if (invoker == null || invoker.IsDisposed)
            {
                Debug.WriteLine(msg?.ToString());
                return;
            }

            if (invoker.InvokeRequired)
            {
                try
                {
                    invoker.Invoke(new Action(() => Print(msg)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                return;
            }

            if (_form == null || _form.IsDisposed)
            {
                _form = new Form 
                { 
                    Text = "Консоль скриптов Renga", 
                    Width = 1000, 
                    Height = 400, 
                    TopMost = true,
                    ShowIcon = false
                };
                
                _form.FormClosing += (s, e) =>
                {
                    if (e.CloseReason == CloseReason.UserClosing)
                    {
                        e.Cancel = true;
                        _form.Hide();
                    }
                };
                
                _textBox = new TextBox 
                { 
                    Multiline = true, 
                    Dock = DockStyle.Fill, 
                    ScrollBars = ScrollBars.Vertical, 
                    ReadOnly = true, 
                    Font = new System.Drawing.Font("Consolas", 10) 
                };
                
                _form.Controls.Add(_textBox);
                _form.Show();
            }
            
            if (!_form.Visible) 
            {
                _form.Show();
            }
            
            _textBox.AppendText(msg?.ToString() + Environment.NewLine);
            _textBox.SelectionStart = _textBox.Text.Length;
            _textBox.ScrollToCaret();
        }
    }
}
