using DnWModLoader;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace KoboldVision
{
    // Assignable keys
    public enum VisionKey
    {
        None,
        V,
        Q,
        F,
        G,
        R,
        T,
        X,
        Z,
        B,
        Tab,
        CapsLock,
        Backquote,
        MiddleMouse,
        MouseBack,
        MouseForward,
    }

    // Assignable gamepad buttons
    public enum VisionButton
    {
        None,
        North,
        West,
        RightStickPress,
        DpadUp,
        DpadDown,
        Select,
    }

    internal static class VisionInput
    {
        public static bool WasPressed(VisionKey key, VisionButton button)
        {
            var keyControl = Control(key);
            var buttonControl = Control(button);
            return (keyControl != null && keyControl.wasPressedThisFrame) || (buttonControl != null && buttonControl.wasPressedThisFrame);
        }

        // Don't trigger while the player is busy (cutscenes, menus, etc)
        public static bool GameTakesInput()
        {
            if (ModLoader.IsOverlayVisible) return false;
            var state = GameStateManager.Instance;
            if (state != null && state.IsPaused) return false;
            return MenuManager.actions.Player.enabled;
        }

        private static ButtonControl Control(VisionKey key)
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            switch (key)
            {
                case VisionKey.None: return null;
                case VisionKey.MiddleMouse: return mouse != null ? mouse.middleButton : null;
                case VisionKey.MouseBack: return mouse != null ? mouse.backButton : null;
                case VisionKey.MouseForward: return mouse != null ? mouse.forwardButton : null;
            }
            if (keyboard == null) return null;
            switch (key)
            {
                case VisionKey.V: return keyboard.vKey;
                case VisionKey.Q: return keyboard.qKey;
                case VisionKey.F: return keyboard.fKey;
                case VisionKey.G: return keyboard.gKey;
                case VisionKey.R: return keyboard.rKey;
                case VisionKey.T: return keyboard.tKey;
                case VisionKey.X: return keyboard.xKey;
                case VisionKey.Z: return keyboard.zKey;
                case VisionKey.B: return keyboard.bKey;
                case VisionKey.Tab: return keyboard.tabKey;
                case VisionKey.CapsLock: return keyboard.capsLockKey;
                case VisionKey.Backquote: return keyboard.backquoteKey;
                default: return null;
            }
        }

        private static ButtonControl Control(VisionButton button)
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return null;
            switch (button)
            {
                case VisionButton.North: return gamepad.buttonNorth;
                case VisionButton.West: return gamepad.buttonWest;
                case VisionButton.RightStickPress: return gamepad.rightStickButton;
                case VisionButton.DpadUp: return gamepad.dpad.up;
                case VisionButton.DpadDown: return gamepad.dpad.down;
                case VisionButton.Select: return gamepad.selectButton;
                default: return null;
            }
        }
    }
}
