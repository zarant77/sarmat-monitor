using SarmatVisionHold.Replay.Processing;
using SarmatVisionHold.Replay.Telemetry;
using SarmatVisionHold.ReplayAnalyzer.Input;
using SarmatVisionHold.ReplayAnalyzer.Processing;

namespace SarmatVisionHold.ReplayAnalyzer.Output
{
    public sealed class ReplayRecord
    {
        public VideoReplayFrame Frame;
        public double ReplayTimeSeconds, TlogTimeSeconds, SyncErrorSeconds, ProcessingMilliseconds;
        public ReplayTelemetrySample Telemetry;
        public ReplayVisionResult Vision;
        public OpticalFlowRadModel OpticalFlowRad;
    }
}
