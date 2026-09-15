using DnWModLoader.Config;

namespace KoboldVision
{
    internal sealed class VisionSettings
    {
        public readonly ConfigEntry<VisionKey> Key;
        public readonly ConfigEntry<VisionButton> GamepadButton;

        public VisionSettings(ModConfig config)
        {
            config.DescribeSection("Controls", "Controls", "Press the button for Kobold Vision!");
            Key = config.Bind("Controls", "Key", VisionKey.V, "Keyboard key or mouse button that shows Kobold Vision.");
            GamepadButton = config.Bind("Controls", "GamepadButton", VisionButton.North, "Gamepad button that shows Kobold Vision.");
        }
    }
}
