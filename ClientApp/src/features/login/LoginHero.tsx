// Ficheiro: ClientApp/src/features/login/LoginHero.tsx
import { useEffect, useRef, useState, type ReactNode } from "react";
import { LOGIN_HERO_MEDIA } from "./loginMedia";

const REDUCED_MOTION = "(prefers-reduced-motion: reduce)";

function usePrefersReducedMotion() {
  const [reduced, setReduced] = useState(() => window.matchMedia?.(REDUCED_MOTION).matches ?? false);
  useEffect(() => {
    const mq = window.matchMedia?.(REDUCED_MOTION);
    if (!mq) return;
    const onChange = () => setReduced(mq.matches);
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, []);
  return reduced;
}

/**
 * Palco de mídia do login: fundo navy com malha técnica (sempre presente) → poster → vídeo em loop → overlay.
 * Cada camada é opcional: se o poster ou o vídeo não existirem, a camada de baixo continua visível.
 * Com prefers-reduced-motion o vídeo não é carregado; fica só o poster.
 */
export function LoginHero({ children }: { children: ReactNode }) {
  const reducedMotion = usePrefersReducedMotion();
  const videoRef = useRef<HTMLVideoElement>(null);
  const [posterOk, setPosterOk] = useState(true);
  const [videoState, setVideoState] = useState<"loading" | "playing" | "failed">("loading");

  useEffect(() => {
    const video = videoRef.current;
    if (!video || reducedMotion) return;
    // React não reflete `muted` como atributo; sem ele alguns navegadores bloqueiam o autoplay.
    video.muted = true;
    video.play().catch(() => {
      /* autoplay bloqueado: o poster permanece */
    });
  }, [reducedMotion]);

  return (
    <div className="na-login-hero">
      <div className="na-login-hero__stage" aria-hidden="true">
        <div className="na-login-hero__fallback" />
        {posterOk && (
          <img
            className="na-login-hero__poster"
            src={LOGIN_HERO_MEDIA.poster}
            alt=""
            decoding="async"
            onError={() => setPosterOk(false)}
          />
        )}
        {!reducedMotion && videoState !== "failed" && (
          <video
            ref={videoRef}
            className="na-login-hero__video"
            data-state={videoState}
            autoPlay
            muted
            loop
            playsInline
            preload="metadata"
            disablePictureInPicture
            onPlaying={() => setVideoState("playing")}
            onError={() => setVideoState("failed")}
          >
            <source src={LOGIN_HERO_MEDIA.video} type="video/mp4" onError={() => setVideoState("failed")} />
          </video>
        )}
        <div className="na-login-hero__overlay" />
      </div>
      {children}
    </div>
  );
}
