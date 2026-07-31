namespace SarmatPlugin.Core
{
    public sealed class RisingEdgeTrigger
    {
        private bool wasPressed;

        public bool Update(bool pressed)
        {
            var triggered = pressed && !wasPressed;
            wasPressed = pressed;
            return triggered;
        }

        public void Reset() { wasPressed = false; }
    }
}
