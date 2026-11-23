using System;
using System.Windows.Forms;

namespace NyangTalk.App.Controls
{
    /// <summary>
    /// Message input. Enter sends, Shift+Enter inserts a line break. While an IME composition is open (Korean, Japanese…)
    /// Enter only commits the composition and never sends (50-SPEC 7장).
    /// </summary>
    public sealed class Composer : TextBox
    {
        public const int WM_IME_STARTCOMPOSITION = 0x010D;
        public const int WM_IME_ENDCOMPOSITION = 0x010E;
        public bool Composing { get; private set; }
        public event Action SendRequested;

        public Composer()
        {
            Multiline = true;
            AcceptsReturn = true;
            ScrollBars = ScrollBars.None;
            WordWrap = true;
            BorderStyle = BorderStyle.None;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_IME_STARTCOMPOSITION) Composing = true;
            else if (m.Msg == WM_IME_ENDCOMPOSITION) Composing = false;
            base.WndProc(ref m);
        }

        /// <summary>Feeds a window message (tests simulate IME messages through this).</summary>
        public void Simulate(int msg)
        {
            var m = Message.Create(Handle, msg, IntPtr.Zero, IntPtr.Zero);
            WndProc(ref m);
        }

        /// <summary>Returns true when the key was consumed as "send".</summary>
        public bool HandleKey(Keys keyCode, bool shift)
        {
            // Keys.ProcessKey (VK_PROCESSKEY) means the IME is handling the key.
            if (keyCode == Keys.ProcessKey || Composing) return false;
            if (keyCode == Keys.Enter && !shift)
            {
                var h = SendRequested; if (h != null) h();
                return true;
            }
            return false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (HandleKey(e.KeyCode, e.Shift)) { e.SuppressKeyPress = true; e.Handled = true; return; }
            base.OnKeyDown(e);
        }
    }
}
