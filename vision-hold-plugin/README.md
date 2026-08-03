# Sarmat Vision Hold

Незалежний плагін Mission Planner, який перетворює налаштований RTSP-потік на optical flow. Він не використовує RC override і не керує roll/pitch. У live-режимі надсилає стандартний `OPTICAL_FLOW_RAD`, а стабілізацію виконує ArduPilot FlowHold/EKF.

> За замовчуванням `DiagnosticsOnly=true`, а `EnableLiveControl=false`. На реальному апараті нічого не надсилається і режими не перемикаються.

## Модулі

- `CameraFrameSource` — RTSP/FFmpeg capture; `OpticalFlowTracker` — Shi–Tomasi + pyramidal Lucas–Kanade.
- `AttitudeCompensator`, `GroundVelocityEstimator`, `FlowQualityEstimator` — компенсація IMU, масштабування relative altitude та gates.
- `RcSwitchListener` — пасивний hysteresis/debounce/stale listener без запису RC.
- `OpticalFlowMavlinkPublisher`, `EkfSourceController` — SITL-only live path.
- `VisionHoldStateMachine` — `Disabled`, `WarmingUp`, `Ready`, `Active`, `Degraded`, `Lost`.
- `VisionHoldPanel` — окрема вкладка Flight Data.

Налаштування й лог незалежні від Sarmat Plugin: `%APPDATA%\Sarmat\VisionHold\settings.json` і `vision-hold.log`.

## Збірка DLL

```powershell
.\scripts\build.ps1 -MissionPlannerPath 'C:\Program Files (x86)\Mission Planner' -Configuration Release
```

Скопіюйте весь вміст `dist\plugins` до `Mission Planner\plugins`.

## Unit-тести

Core-тести не потребують Mission Planner, MAVLink, RTSP, мережі або обладнання:

```powershell
dotnet run --project .\tests\SarmatVisionHold.Tests\SarmatVisionHold.Tests.csproj -c Release
```

Offline optical-flow тести на детермінованих синтетичних кадрах:

```powershell
dotnet run --project .\tests\SarmatVisionHold.OfflineTests\SarmatVisionHold.OfflineTests.csproj -c Release
```

Вони перевіряють нульовий flow нерухомої камери, знак і масштаб X/Y, компенсацію attitude-only руху, падіння quality при blur/декорельованій текстурі/втраті кадрів та явний axis-convention guard. Тест не читає відеофайли, не відкриває RTSP і не використовує обладнання.

## Offline Video Analyzer

`SarmatVisionHold.VideoAnalyzer` — окремий headless console tool для безпечного аналізу одного змішаного запису із зависанням, трансляцією, зупинками та обертанням. Shared processor використовує forward-backward LK і `estimateAffinePartial2D`/RANSAC та повертає translation центра кадру, rotation, scale, inliers, inlier ratio і quality. Tool не підключає Mission Planner, MAVLink, RTSP, IMU, EKF або live control.

Windows PowerShell:

```powershell
dotnet run --project .\vision-hold-plugin\tools\SarmatVisionHold.VideoAnalyzer -- `
  --input "C:\Users\PegasArm\Videos\01-static.mkv" `
  --output ".\artifacts\01-static"
```

З preview-вікном і параметрами алгоритму:

```powershell
dotnet run --project .\vision-hold-plugin\tools\SarmatVisionHold.VideoAnalyzer -- `
  --input ".\flight.mkv" --output ".\artifacts\flight" --preview `
  --start 10 --duration 90 --labels ".\labels.csv" `
  --max-features 500 --quality-level 0.01 --minimum-distance 8 `
  --lk-window-size 21 --pyramid-levels 3 --fb-threshold 1.5 `
  --outlier-threshold 3 --ransac-threshold 2 --ransac-confidence 0.99 `
  --minimum-accepted-points 25 --minimum-quality 0.35 `
  --translation-threshold 1 --rotation-threshold 0.25 --scale-threshold 0.003 `
  --roi 100,80,1080,560 --mask ".\ground-mask.png"
```

Також можна передати `--config .\tools\SarmatVisionHold.VideoAnalyzer\config.example.json`; окремі CLI-параметри перевизначають JSON. `--preview` вимкнений за замовчуванням. ROI та mask перетинаються, якщо вказані разом.

У каталозі `--output` створюються:

- `motion.csv` — raw/smoothed translation, rotation і scale, raw/smoothed classification, texture/camera direction, points, RANSAC inliers, quality, processing time і status;
- `annotated.mp4` або fallback `annotated.mkv` — accepted/rejected tracks, vectors, median vector, FPS, quality та status;
- `report.md` — тривалість, роздільність, FPS, processing time, quality, частки motion states, median/P95 magnitude, стрибки, low-quality intervals та optional labels validation.

Формат ручної розмітки — `start,end,expectedMotion`; приклад є у `tools\SarmatVisionHold.VideoAnalyzer\labels.example.csv`. Підтримуються `LEFT`, `RIGHT`, `UP`, `DOWN`, комбіновані напрямки, `STILL`, `ROTATING_CW`, `ROTATING_CCW`, `MIXED`, `DEGRADED`, `LOST`. Без `--labels` validation section не створюється.

Табличні тести покривають RC threshold/hysteresis/debounce/inversion/stale та edge events; усі переходи state machine і `Lost` re-arm latch; frame/telemetry freshness, FPS/points/quality/height boundaries; FOV-based velocity, signs, scaling і saturation; roll/pitch/yaw compensation та invalid IMU; припинення публікації і зафіксовані причини для video/MAVLink/RC/height/quality loss, tracker/publisher exceptions і зупинки плагіна.

## RC switch

Після першого запуску закрийте Mission Planner і відредагуйте `settings.json`: `RcChannel` (1–18), `RcEnableThreshold` (1700), `RcDisableThreshold` (1300), `RcInverted`, `RcDebounceMs` (300), `RcStaleMs` (1000). Значення лише читаються з телеметрії Mission Planner. Зміна фіксується один раз після debounce; недоступний або застарілий канал примусово дає OFF.

## SITL

1. Залиште `DiagnosticsOnly=true`, `EnableLiveControl=false`; подайте RTSP і перевірте FPS, frame age, points, quality, raw/compensated flow та height.
2. Перевірте RC9: вище 1700 — ON, нижче 1300 — OFF; середня зона зберігає стан.
3. У ArduCopter SITL заздалегідь налаштуйте EKF source set без GPS XY та FlowHold. GPS фізично не вимикайте.
4. Лише для SITL задайте `DiagnosticsOnly=false`, `EnableLiveControl=true`, `NonGpsEkfSourceSet`, `FallbackMode`, потім перезапустіть Mission Planner.
5. `Active` можливий лише після warm-up, свіжих кадрів, достатніх FPS/quality/points, валідної висоти та MAVLink.
6. Заморозьте RTSP, зірвіть tracking або MAVLink: публікація має припинитися, стан перейти в `Lost/Degraded`, а режим — повернутися або перейти в `AltHold`.

Live control експериментальний і призначений лише для SITL до окремої льотної валідації.
