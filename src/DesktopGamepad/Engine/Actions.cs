using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DesktopGamepad.Engine
{
    public enum ActionKind { None, MouseButton, Keys, MoveCursor, Scroll, ShowKeyboard, ToggleMouseMode, Layer, Precision }

    /// <summary>An action name from the UI ("Ctrl+W", "Num 5", "Volume up", "Left click"…) turned into something that can be sent.</summary>
    public sealed class ParsedAction
    {
        public ActionKind Kind;
        /// <summary>For MouseButton: 0 left, 1 right, 2 middle.</summary>
        public int Button;
        /// <summary>For Keys: modifiers first, then the main key.</summary>
        public ushort[] Keys = Array.Empty<ushort>();
        /// <summary>Held navigation, editing and volume keys repeat; everything else fires once per press.</summary>
        public bool Repeats;

        static readonly Dictionary<string, ParsedAction> Cache = new Dictionary<string, ParsedAction>(StringComparer.OrdinalIgnoreCase);

        public static ParsedAction Parse(string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return null;
            lock (Cache)
            {
                if (Cache.TryGetValue(action, out ParsedAction cached)) return cached;
                var parsed = ParseUncached(action.Trim());
                Cache[action] = parsed;
                return parsed;
            }
        }

        static ParsedAction ParseUncached(string action)
        {
            switch (action)
            {
                case "Left click": return new ParsedAction { Kind = ActionKind.MouseButton, Button = 0 };
                case "Right click": return new ParsedAction { Kind = ActionKind.MouseButton, Button = 1 };
                case "Middle click": return new ParsedAction { Kind = ActionKind.MouseButton, Button = 2 };
                case "Move cursor": return new ParsedAction { Kind = ActionKind.MoveCursor };
                case "Scroll": return new ParsedAction { Kind = ActionKind.Scroll };
                case "Show keyboard": return new ParsedAction { Kind = ActionKind.ShowKeyboard };
                case "Mouse mode on / off": return new ParsedAction { Kind = ActionKind.ToggleMouseMode };
                case "Hold for second layer": return new ParsedAction { Kind = ActionKind.Layer };
                case "Precision cursor": return new ParsedAction { Kind = ActionKind.Precision };
            }

            // Modifiers come first ("Ctrl+Shift+Tab"); the rest is one key name, which may itself contain "+" ("Num +").
            var parts = action.Split('+');
            var keys = new List<ushort>();
            int i = 0;
            for (; i < parts.Length - 1; i++)
            {
                ushort mod = ModifierVk(parts[i].Trim());
                if (mod == 0) break;
                keys.Add(mod);
            }
            string main = string.Join("+", parts, i, parts.Length - i).Trim();
            ushort vk = ModifierVk(main);
            bool modifierOnly = vk != 0;
            if (vk == 0) vk = KeyVk(main);
            if (vk == 0)
            {
                Log.Write("unknown action \"" + action + "\"");
                return new ParsedAction { Kind = ActionKind.None };
            }
            keys.Add(vk);
            // Only keys that make sense to hold repeat. Everything else fires once per press: a controller press often
            // lasts longer than a key tap, and a repeated Enter or Ctrl+W would act again and again.
            bool repeats = !modifierOnly && keys.Count == 1 && RepeatableKeys.Contains(vk);
            return new ParsedAction { Kind = ActionKind.Keys, Keys = keys.ToArray(), Repeats = repeats };
        }

        /// <summary>Arrows, Backspace, Delete, Page Up/Down, Home/End and volume up/down.</summary>
        static readonly HashSet<ushort> RepeatableKeys = new HashSet<ushort>
        {
            0x25, 0x26, 0x27, 0x28, 0x08, 0x2E, 0x21, 0x22, 0x24, 0x23, 0xAF, 0xAE
        };

        static ushort ModifierVk(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "ctrl": return 0xA2;  // left Ctrl
                case "alt": return 0xA4;   // left Alt
                case "shift": return 0xA0; // left Shift
                case "win": return 0x5B;   // left Windows key
                default: return 0;
            }
        }

        static readonly Dictionary<string, ushort> Named = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Tab"] = 0x09, ["Backspace"] = 0x08, ["Space"] = 0x20,
            ["Caps Lock"] = 0x14, ["CapsLock"] = 0x14, ["Delete"] = 0x2E, ["Del"] = 0x2E, ["Insert"] = 0x2D, ["Home"] = 0x24, ["End"] = 0x23,
            ["Page Up"] = 0x21, ["PageUp"] = 0x21, ["Page Down"] = 0x22, ["PageDown"] = 0x22, ["Print Screen"] = 0x2C, ["PrintScreen"] = 0x2C,
            ["Pause"] = 0x13, ["Scroll Lock"] = 0x91, ["ScrollLock"] = 0x91, ["Menu key"] = 0x5D, ["ContextMenu"] = 0x5D,
            ["Up arrow"] = 0x26, ["Up"] = 0x26, ["ArrowUp"] = 0x26, ["Down arrow"] = 0x28, ["Down"] = 0x28, ["ArrowDown"] = 0x28,
            ["Left arrow"] = 0x25, ["Left"] = 0x25, ["ArrowLeft"] = 0x25, ["Right arrow"] = 0x27, ["Right"] = 0x27, ["ArrowRight"] = 0x27,
            ["`"] = 0xC0, ["-"] = 0xBD, ["="] = 0xBB, ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC, [";"] = 0xBA, ["'"] = 0xDE,
            [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF,
            ["Num Lock"] = 0x90, ["Num *"] = 0x6A, ["Num +"] = 0x6B, ["Num -"] = 0x6D, ["Num ."] = 0x6E, ["Num /"] = 0x6F, ["Num Enter"] = 0x0D,
            ["Play / pause"] = 0xB3, ["Next track"] = 0xB0, ["Previous track"] = 0xB1, ["Volume up"] = 0xAF, ["Volume down"] = 0xAE, ["Mute"] = 0xAD
        };

        static ushort KeyVk(string name)
        {
            if (Named.TryGetValue(name, out ushort vk)) return vk;
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if (c >= 'A' && c <= 'Z') return c;
                if (c >= '0' && c <= '9') return c;
            }
            if (name.StartsWith("Num ", StringComparison.OrdinalIgnoreCase) && name.Length == 5 && char.IsDigit(name[4])) return (ushort)(0x60 + (name[4] - '0'));
            if ((name[0] == 'F' || name[0] == 'f') && int.TryParse(name.Substring(1), out int f) && f >= 1 && f <= 24) return (ushort)(0x70 + f - 1);
            return 0;
        }

        /// <summary>Keys Windows treats as "extended", which need the extended flag when injected.</summary>
        public static bool IsExtended(ushort vk, string actionName)
        {
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24: case 0x25: case 0x26: case 0x27: case 0x28:
                case 0x2D: case 0x2E: case 0x6F: case 0x5B: case 0x5D: case 0x2C:
                case 0xAD: case 0xAE: case 0xAF: case 0xB0: case 0xB1: case 0xB3:
                    return true;
                case 0x0D: return actionName != null && actionName.EndsWith("Num Enter", StringComparison.OrdinalIgnoreCase);
                default: return false;
            }
        }
    }

    /// <summary>Sends mouse and keyboard input, tagged as Desktop Gamepad's own.</summary>
    public static class Output
    {
        static readonly int InputSize = Marshal.SizeOf(typeof(Native.INPUT));

        static void Send(params Native.INPUT[] inputs)
        {
            if (inputs.Length > 0) Native.SendInput((uint)inputs.Length, inputs, InputSize);
        }

        static Native.INPUT Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0) => new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.InputUnion { mi = new Native.MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = Native.AppMarker } }
        };

        public static void MouseButton(int button, bool down)
        {
            uint flag = button == 0 ? (down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP)
                : button == 1 ? (down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP)
                : (down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP);
            Send(Mouse(flag));
        }

        /// <summary>Moves the pointer to a screen position with an absolute move, so Windows' pointer acceleration does not apply.</summary>
        public static void MoveTo(int x, int y)
        {
            int vx = Native.GetSystemMetrics(76), vy = Native.GetSystemMetrics(77);
            int vw = Native.GetSystemMetrics(78), vh = Native.GetSystemMetrics(79);
            int ax = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1));
            int ay = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1));
            Send(Mouse(Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK, ax, ay));
        }

        public static void Wheel(int delta, bool horizontal)
        {
            if (delta == 0) return;
            Send(Mouse(horizontal ? Native.MOUSEEVENTF_HWHEEL : Native.MOUSEEVENTF_WHEEL, 0, 0, (uint)delta));
        }

        public static void Keys(ParsedAction action, string name, bool down)
        {
            var list = new List<Native.INPUT>();
            if (down) foreach (var vk in action.Keys) list.Add(Key(vk, name, true));
            else for (int i = action.Keys.Length - 1; i >= 0; i--) list.Add(Key(action.Keys[i], name, false));
            Send(list.ToArray());
        }

        /// <summary>Presses and releases only the main key, for repeats while modifiers stay held.</summary>
        public static void RepeatMainKey(ParsedAction action, string name)
        {
            ushort vk = action.Keys[action.Keys.Length - 1];
            Send(Key(vk, name, true), Key(vk, name, false));
        }

        static Native.INPUT Key(ushort vk, string name, bool down)
        {
            uint flags = (down ? 0 : Native.KEYEVENTF_KEYUP) | (ParsedAction.IsExtended(vk, name) ? Native.KEYEVENTF_EXTENDEDKEY : 0);
            return new Native.INPUT
            {
                type = Native.INPUT_KEYBOARD,
                u = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = vk, wScan = (ushort)Native.MapVirtualKey(vk, 0), dwFlags = flags, dwExtraInfo = Native.AppMarker } }
            };
        }
    }
}
