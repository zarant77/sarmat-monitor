using SarmatVisionHold.Core;
namespace SarmatVisionHold.Integration { public sealed class OpticalFlowMavlinkPublisher { private readonly IVehicleGateway gateway;public OpticalFlowMavlinkPublisher(IVehicleGateway g){gateway=g;}public bool Publish(FlowSample f,TelemetrySample t,VisionHoldSettings s){if(s.DiagnosticsOnly||!s.EnableLiveControl)return false;return gateway.PublishOpticalFlow(f,t);} } }
