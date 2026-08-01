namespace SarmatPlugin.Core
{
    public sealed class LimaModeLatch
    {
        private bool wasPressed;
        private string returnMode;

        public string Update(bool pressed, string currentMode, string pressedMode)
        {
            if (pressed && !wasPressed)
            {
                wasPressed = true;
                returnMode = currentMode;
                return pressedMode;
            }
            if (!pressed && wasPressed)
            {
                wasPressed = false;
                var mode = returnMode;
                returnMode = null;
                return string.IsNullOrWhiteSpace(mode) ? null : mode;
            }
            return null;
        }

        public void Reset()
        {
            wasPressed = false;
            returnMode = null;
        }
    }
}
