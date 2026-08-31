import { useEffect, useRef, useState } from "react";
import { Check, RefreshCw, X } from "lucide-react";
import type { CheckerModule } from "@sbm/shared";
import { evaluateStability, recognizeCheckerImage, type CheckerRecognitionResult, type ScannerState } from "../checker-recognition";
import { useI18n } from "../i18n";

const ANALYSIS_WIDTH = 540;
const ANALYSIS_HEIGHT = 800;
const ANALYSIS_INTERVAL_MS = 250;

export function CheckerCamera({ module, minCellVoltage, maxCellVoltage, onCancel, onConfirm }: {
  module: CheckerModule;
  minCellVoltage: number;
  maxCellVoltage: number;
  onCancel: () => void;
  onConfirm: (cells: number[]) => void;
}) {
  const { t } = useI18n();
  const debugEnabled = import.meta.env.DEV && new URLSearchParams(window.location.search).has("scannerDebug");
  const videoRef = useRef<HTMLVideoElement>(null); const stageRef = useRef<HTMLDivElement>(null); const frameRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null); const streamRef = useRef<MediaStream | null>(null); const historyRef = useRef<CheckerRecognitionResult[]>([]);
  const [ready, setReady] = useState(false); const [error, setError] = useState("");
  const [scannerState, setScannerState] = useState<ScannerState>("red");
  const [latest, setLatest] = useState<CheckerRecognitionResult | null>(null); const [observedCells, setObservedCells] = useState<Array<number | null> | null>(null); const [lockedCells, setLockedCells] = useState<number[] | null>(null);

  useEffect(() => {
    let active = true;
    void (async () => {
      try {
        if (!navigator.mediaDevices?.getUserMedia) throw new Error("camera-unavailable");
        const stream = await navigator.mediaDevices.getUserMedia({ audio: false, video: { facingMode: { ideal: "environment" }, width: { ideal: 1280 }, height: { ideal: 720 } } });
        if (!active) { stream.getTracks().forEach(track => track.stop()); return; }
        streamRef.current = stream;
        if (videoRef.current) { videoRef.current.srcObject = stream; await videoRef.current.play(); }
      } catch { if (active) setError(t("camera.permissionError")); }
    })();
    return () => { active = false; streamRef.current?.getTracks().forEach(track => track.stop()); };
  }, [t]);

  useEffect(() => {
    if (!ready || lockedCells || error) return;
    let cancelled = false; let frameId = 0; let lastAttempt = 0;
    const analyze = (now: number) => {
      if (cancelled) return;
      if (now - lastAttempt >= ANALYSIS_INTERVAL_MS) {
        lastAttempt = now;
        const video = videoRef.current, stage = stageRef.current, frame = frameRef.current, canvas = canvasRef.current;
        if (video && stage && frame && canvas && video.videoWidth && video.videoHeight) {
          const stageRect = stage.getBoundingClientRect(); const frameRect = frame.getBoundingClientRect();
          const scale = Math.max(stageRect.width / video.videoWidth, stageRect.height / video.videoHeight);
          const renderedWidth = video.videoWidth * scale; const renderedHeight = video.videoHeight * scale;
          const offsetX = (stageRect.width - renderedWidth) / 2; const offsetY = (stageRect.height - renderedHeight) / 2;
          const sourceX = Math.max(0, (frameRect.left - stageRect.left - offsetX) / scale);
          const sourceY = Math.max(0, (frameRect.top - stageRect.top - offsetY) / scale);
          const sourceWidth = Math.min(video.videoWidth - sourceX, frameRect.width / scale);
          const sourceHeight = Math.min(video.videoHeight - sourceY, frameRect.height / scale);
          const context = canvas.getContext("2d", { willReadFrequently: true });
          if (context && sourceWidth > 0 && sourceHeight > 0) {
            context.drawImage(video, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, ANALYSIS_WIDTH, ANALYSIS_HEIGHT);
            const sampledRoi = context.getImageData(0, 0, ANALYSIS_WIDTH, ANALYSIS_HEIGHT);
            const result = recognizeCheckerImage(sampledRoi, { min: minCellVoltage, max: maxCellVoltage }, debugEnabled ? {
              onDebug: debug => { (window as unknown as { __checkerScannerDebug: unknown }).__checkerScannerDebug = { sampledRoi, ...debug, state: evaluateStability([...historyRef.current.slice(-4), debug.result]).state }; }
            } : undefined);
            historyRef.current = [...historyRef.current.slice(-4), result];
            const stability = evaluateStability(historyRef.current);
            setLatest(result); setObservedCells(stability.observedCells); setScannerState(stability.state);
            if (stability.stableCells) { setLockedCells(stability.stableCells); video.pause(); }
          }
        }
      }
      frameId = requestAnimationFrame(analyze);
    };
    frameId = requestAnimationFrame(analyze);
    return () => { cancelled = true; cancelAnimationFrame(frameId); };
  }, [ready, lockedCells, error, minCellVoltage, maxCellVoltage, debugEnabled]);

  const retry = () => { historyRef.current = []; setLatest(null); setObservedCells(null); setLockedCells(null); setScannerState("red"); setError(""); void videoRef.current?.play(); };
  const visibleCells = lockedCells ?? observedCells ?? latest?.cells ?? Array(6).fill(null);

  return <div className="camera-overlay" role="dialog" aria-modal="true" aria-label={t("camera.title", { module })}>
    <header className="camera-header"><button type="button" className="camera-close" onClick={onCancel} aria-label={t("common.cancel")}><X/></button><div><strong>{t("camera.module", { module })}</strong><small>{t("camera.align")}</small></div></header>
    <div className="camera-stage" ref={stageRef}>
      <video ref={videoRef} muted playsInline autoPlay onLoadedMetadata={() => setReady(true)}/><canvas ref={canvasRef} width={ANALYSIS_WIDTH} height={ANALYSIS_HEIGHT} hidden/>
      <div className={`camera-frame scanner-${scannerState}`} ref={frameRef}><span>{t(`camera.state.${scannerState}`)}</span><i/><i/><i/><i/>{latest?.lcdQuad && <svg className="detected-lcd-outline" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true"><polygon points={latest.lcdQuad.map(point => `${point.x * 100},${point.y * 100}`).join(" ")}/></svg>}</div>
      {!ready && !error && <div className="camera-loading">{t("camera.starting")}</div>}
    </div>
    <footer className="camera-controls">
      {error ? <p className="camera-error">{error}</p> : <><p>{lockedCells ? t("camera.stable") : t("camera.liveHint")}</p><div className="camera-recognized-cells">{visibleCells.map((cell, index) => <span key={index}><small>{index + 1}</small><strong>{cell == null ? "—" : cell.toFixed(2)}</strong></span>)}</div></>}
      {lockedCells ? <div className="camera-review-actions"><button type="button" className="button secondary" onClick={retry}><RefreshCw/> {t("camera.retry")}</button><button type="button" className="button primary" onClick={() => onConfirm(lockedCells)}><Check/> {t("camera.confirm")}</button></div> : <button type="button" className="camera-text-button" onClick={onCancel}>{t("common.cancel")}</button>}
    </footer>
  </div>;
}
