namespace SarmatPlugin.Core
{
    public static class GStreamerPipelineBuilder
    {
        public static string Build(PluginSettings value)
        {
            value = value ?? new PluginSettings();
            value.Normalize();
            return "rtspsrc location=" + value.CameraUrl +
                " protocols=" + value.CameraProtocol +
                " latency=" + value.CameraLatencyMs +
                " drop-on-latency=" + value.CameraDropOnLatency.ToString().ToLowerInvariant() +
                " ! " + value.CameraDepayloader +
                " ! " + value.CameraParser +
                " ! " + value.CameraDecoder +
                " ! queue max-size-buffers=" + value.CameraQueueMaxBuffers +
                " leaky=" + value.CameraQueueLeaky +
                " ! " + value.CameraConverter +
                " ! video/x-raw,format=" + value.CameraRawFormat +
                " ! appsink name=" + value.CameraAppSinkName +
                " sync=" + value.CameraSync.ToString().ToLowerInvariant();
        }
    }
}
