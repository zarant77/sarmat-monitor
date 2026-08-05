using SarmatVisionHold.Core;
namespace SarmatVisionHold.Integration { public interface IVehicleGateway { TelemetrySample ReadTelemetry(int rcChannel); string CurrentMode{get;} } }
