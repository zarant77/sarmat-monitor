# Sarmat Altitude Assist

Standalone Mission Planner plugin. It has no dependency on Sarmat Vision Hold, OpenCV, RTSP, optical flow, or Kestrel.

Build the plugin with `scripts\build.cmd "C:\Program Files (x86)\Mission Planner"`, then build the standalone installer with `dotnet build installer\SarmatAltitudeAssist.Installer.wixproj -c Release`.

The initial release is diagnostics-only: physical vertical control output is compile/runtime locked.
