import { useEffect, useRef, useState } from "react";
import { Camera, Check, RefreshCw, X } from "lucide-react";
import type { CheckerModule } from "@sbm/shared";
import { useI18n } from "../i18n";

const OUTPUT_WIDTH = 900;
const OUTPUT_HEIGHT = 1400;

export interface CapturedCheckerImage {
  blob: Blob;
  width: number;
  height: number;
}

export function CheckerCamera({ module, onCancel, onConfirm }: {
  module: CheckerModule;
  onCancel: () => void;
  onConfirm: (image: CapturedCheckerImage) => void;
}) {
  const { t } = useI18n();
  const videoRef = useRef<HTMLVideoElement>(null);
  const stageRef = useRef<HTMLDivElement>(null);
  const frameRef = useRef<HTMLDivElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const [ready, setReady] = useState(false);
  const [captured, setCaptured] = useState<CapturedCheckerImage | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    const start = async () => {
      try {
        if (!navigator.mediaDevices?.getUserMedia) throw new Error("camera-unavailable");
        const stream = await navigator.mediaDevices.getUserMedia({
          audio: false,
          video: { facingMode: { ideal: "environment" }, width: { ideal: 1920 }, height: { ideal: 1080 } }
        });
        if (!active) { stream.getTracks().forEach(track => track.stop()); return; }
        streamRef.current = stream;
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
          await videoRef.current.play();
        }
      } catch {
        if (active) setError(t("camera.permissionError"));
      }
    };
    void start();
    return () => {
      active = false;
      streamRef.current?.getTracks().forEach(track => track.stop());
    };
  }, [t]);

  useEffect(() => () => { if (previewUrl) URL.revokeObjectURL(previewUrl); }, [previewUrl]);

  const capture = async () => {
    const video = videoRef.current, stage = stageRef.current, frame = frameRef.current;
    if (!video || !stage || !frame || !video.videoWidth || !video.videoHeight) return;
    const stageRect = stage.getBoundingClientRect();
    const frameRect = frame.getBoundingClientRect();
    const scale = Math.max(stageRect.width / video.videoWidth, stageRect.height / video.videoHeight);
    const renderedWidth = video.videoWidth * scale;
    const renderedHeight = video.videoHeight * scale;
    const offsetX = (stageRect.width - renderedWidth) / 2;
    const offsetY = (stageRect.height - renderedHeight) / 2;
    const sourceX = Math.max(0, (frameRect.left - stageRect.left - offsetX) / scale);
    const sourceY = Math.max(0, (frameRect.top - stageRect.top - offsetY) / scale);
    const sourceWidth = Math.min(video.videoWidth - sourceX, frameRect.width / scale);
    const sourceHeight = Math.min(video.videoHeight - sourceY, frameRect.height / scale);
    const canvas = document.createElement("canvas");
    canvas.width = OUTPUT_WIDTH;
    canvas.height = OUTPUT_HEIGHT;
    const context = canvas.getContext("2d");
    if (!context) return;
    context.drawImage(video, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, OUTPUT_WIDTH, OUTPUT_HEIGHT);
    const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, "image/jpeg", 0.92));
    if (!blob) { setError(t("camera.captureError")); return; }
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    const url = URL.createObjectURL(blob);
    setCaptured({ blob, width: OUTPUT_WIDTH, height: OUTPUT_HEIGHT });
    setPreviewUrl(url);
    video.pause();
  };

  const retry = () => {
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    setPreviewUrl(null);
    setCaptured(null);
    setError("");
    void videoRef.current?.play();
  };

  const confirm = () => {
    if (!captured) return;
    onConfirm(captured);
  };

  return <div className="camera-overlay" role="dialog" aria-modal="true" aria-label={t("camera.title", { module })}>
    <header className="camera-header"><button type="button" className="camera-close" onClick={onCancel} aria-label={t("common.cancel")}><X/></button><div><strong>{t("camera.module", { module })}</strong><small>{t("camera.align")}</small></div></header>
    <div className="camera-stage" ref={stageRef}>
      <video ref={videoRef} muted playsInline autoPlay onLoadedMetadata={() => setReady(true)}/>
      {previewUrl && <img className="camera-preview-image" src={previewUrl} alt={t("camera.preview", { module })}/>} 
      {!captured && <div className="camera-frame" ref={frameRef}><span>{t("camera.frameLabel")}</span><b aria-hidden="true"/><i/><i/><i/><i/></div>}
      {!ready && !error && <div className="camera-loading">{t("camera.starting")}</div>}
    </div>
    <footer className="camera-controls">
      {error && <p className="camera-error">{error}</p>}
      {!captured ? <><p>{t("camera.hint", { module })}</p><button type="button" className="capture-button" onClick={capture} disabled={!ready || Boolean(error)} aria-label={t("camera.capture", { module })}><Camera/></button><button type="button" className="camera-text-button" onClick={onCancel}>{t("common.cancel")}</button></> : <><p>{t("camera.review")}</p><div className="camera-review-actions"><button type="button" className="button secondary" onClick={retry}><RefreshCw/> {t("camera.retry")}</button><button type="button" className="button primary" onClick={confirm}><Check/> {t("camera.confirm")}</button></div></>}
    </footer>
  </div>;
}
