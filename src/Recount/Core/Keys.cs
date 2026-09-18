using BepInEx.Configuration;
using UnityEngine;

namespace Recount.Core
{
    /// <summary>
    /// Key tests that ignore unrelated keys. BepInEx's KeyboardShortcut.IsPressed/IsDown insist
    /// that every key not part of the shortcut is up, so holding Alt while walking with WASD
    /// would drop the mouse mode and F7 would not toggle the window mid-stride.
    /// </summary>
    public static class Keys
    {
        public static bool Down(KeyboardShortcut k)
        {
            if (k.MainKey == KeyCode.None)
                return false;
            return Input.GetKeyDown(k.MainKey) && Modifiers(k);
        }

        public static bool Held(KeyboardShortcut k)
        {
            if (k.MainKey == KeyCode.None)
                return false;
            return Input.GetKey(k.MainKey) && Modifiers(k);
        }

        private static bool Modifiers(KeyboardShortcut k)
        {
            foreach (KeyCode m in k.Modifiers)
                if (!Input.GetKey(m))
                    return false;
            return true;
        }
    }
}
