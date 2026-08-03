using SarmatVisionHold.Core;
namespace SarmatVisionHold.Integration { public interface IVehicleGateway { TelemetrySample ReadTelemetry(int rcChannel); bool PublishOpticalFlow(FlowSample flow,TelemetrySample telemetry); string CurrentMode{get;} bool SetMode(string mode); int CurrentEkfSourceSet{get;} bool SetEkfSourceSet(int set); } }
