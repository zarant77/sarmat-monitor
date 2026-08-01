namespace SarmatPlugin.Core
{
    /// <summary>
    /// Produces an OBS recording command only for observed ARMED-state edges.
    /// A transition remains pending until OBS confirms a successful connection.
    /// </summary>
    internal sealed class ObsArmingTransitionTracker
    {
        private bool confirmedArmed;

        public ObsArmingTransitionTracker(bool initialArmed)
        {
            confirmedArmed = initialArmed;
        }

        public bool? PendingCommand(bool currentArmed)
        {
            return currentArmed == confirmedArmed ? (bool?)null : currentArmed;
        }

        public void Confirm(bool currentArmed)
        {
            confirmedArmed = currentArmed;
        }
    }
}
