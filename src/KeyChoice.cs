using System.Windows.Forms;

namespace NudgeNest
{
    /// <summary>Which keys can be the custom nudge key, and how to name and send them.</summary>
    internal static class KeyChoice
    {
        /// <summary>Why a key can't be the custom key, or null if it can.</summary>
        public static string Problem(Keys key)
        {
            switch (key)
            {
                case Keys.None: return "no key was chosen";
                case Keys.Escape: return "Esc opens the Roblox menu";
                case Keys.Enter: return "Enter can send a half-typed chat message";
                case Keys.OemQuestion: return "/ opens the chat box";
                case Keys.Back: return "Backspace deletes text if the chat box is open";
                case Keys.Tab: return "Tab shows and hides the player list";
                case Keys.F9: return "F9 opens the developer console";
                case Keys.F11: return "F11 switches Roblox in and out of fullscreen";
                case Keys.LWin: case Keys.RWin: return "the Windows key opens the Start menu";
                case Keys.Apps: return "the menu key opens context menus";
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu: return "Alt opens menus";
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: return "Shift switches Shift Lock on and off";
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: return "Ctrl is only a modifier key";
                case Keys.CapsLock: case Keys.NumLock: case Keys.Scroll:
                    return "it would switch Caps Lock, Num Lock or Scroll Lock for your whole PC";
                case Keys.PrintScreen: return "Print Screen takes screenshots";
            }
            return Supported(key) ? null : "only letters, numbers, F-keys, arrows and punctuation keys can be used";
        }

        private static bool Supported(Keys k)
        {
            return (k >= Keys.A && k <= Keys.Z)
                || (k >= Keys.D0 && k <= Keys.D9)
                || (k >= Keys.NumPad0 && k <= Keys.Divide)      // number pad digits and * + - . /
                || (k >= Keys.F1 && k <= Keys.F12)
                || (k >= Keys.Space && k <= Keys.Down)          // Space, Page Up/Down, End, Home, arrows
                || k == Keys.Insert || k == Keys.Delete
                || (k >= Keys.Oem1 && k <= Keys.Oem3)           // ; = , - . / `
                || (k >= Keys.Oem4 && k <= Keys.Oem8)           // [ \ ] '
                || k == Keys.Oem102;
        }

        public static string Name(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (int)(k - Keys.NumPad0);
            switch (k)
            {
                case Keys.None: return "none";
                case Keys.Space: return "Space";
                case Keys.Left: return "Left arrow";
                case Keys.Right: return "Right arrow";
                case Keys.Up: return "Up arrow";
                case Keys.Down: return "Down arrow";
                case Keys.PageUp: return "Page Up";
                case Keys.PageDown: return "Page Down";
                case Keys.Multiply: return "Num *";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Decimal: return "Num .";
                case Keys.Divide: return "Num /";
            }
            if ((k >= Keys.Oem1 && k <= Keys.Oem3) || (k >= Keys.Oem4 && k <= Keys.Oem8) || k == Keys.Oem102)
            {
                // the character this key types on your keyboard layout
                uint ch = NativeMethods.MapVirtualKey((uint)k, 2) & 0x7FFFFFFF;
                if (ch > 32) return ((char)ch).ToString();
            }
            return k.ToString();
        }

        /// <summary>Keys from the separate arrow/navigation block, which must be sent as "extended" keys.</summary>
        public static bool IsExtended(Keys k)
        {
            return (k >= Keys.PageUp && k <= Keys.Down) || k == Keys.Insert || k == Keys.Delete || k == Keys.Divide;
        }
    }
}
