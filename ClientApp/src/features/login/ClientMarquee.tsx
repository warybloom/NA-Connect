// Ficheiro: ClientApp/src/features/login/ClientMarquee.tsx
import type { CSSProperties } from "react";
import type { ClientBrand } from "./clients";

interface ClientMarqueeProps {
  clients: ClientBrand[];
  label: string;
  /** Segundos por logo numa volta completa; a duração total cresce com a lista e a velocidade fica constante. */
  secondsPerLogo?: number;
}

// Tamanho óptico: todos os logos ocupam aproximadamente a mesma área, então marcas quadradas não somem
// ao lado das horizontais. Altura limitada para caber na faixa e largura limitada para não dominar.
const LOGO_AREA = 3400;
const MIN_HEIGHT = 20;
const MAX_HEIGHT = 46;
const MAX_WIDTH = 150;

function logoBox({ width, height }: ClientBrand) {
  const ratio = width / height;
  let h = Math.min(MAX_HEIGHT, Math.max(MIN_HEIGHT, Math.sqrt(LOGO_AREA / ratio)));
  if (h * ratio > MAX_WIDTH) h = MAX_WIDTH / ratio;
  return { width: Math.round(h * ratio), height: Math.round(h) };
}

const KNOCKOUT_FILTER_ID = "na-logo-knockout";

/**
 * Faixa contínua de logos (CSS puro). A lista é renderizada duas vezes e a trilha anda -50%,
 * então a emenda coincide com o início e não há salto. Pausa no hover/foco e para com prefers-reduced-motion.
 */
export function ClientMarquee({ clients, label, secondsPerLogo = 4.5 }: ClientMarqueeProps) {
  if (clients.length === 0) return null;

  const style = { "--na-marquee-duration": `${Math.max(30, clients.length * secondsPerLogo)}s` } as CSSProperties;

  const renderSet = (copy: number) => (
    <ul className="na-marquee__set" aria-hidden={copy > 0 || undefined}>
      {clients.map((client) => {
        const box = logoBox(client);
        return (
          <li key={client.src} className="na-marquee__item">
            <img
              className="na-marquee__logo"
              data-tone={client.tone ?? "mono"}
              src={client.src}
              alt={copy > 0 ? "" : client.alt}
              width={box.width}
              height={box.height}
              loading="lazy"
              decoding="async"
              draggable={false}
            />
          </li>
        );
      })}
    </ul>
  );

  return (
    <section className="na-marquee" aria-label={label} style={style}>
      {/* Logos com fundo claro opaco: alfa = 3,3 × (alfa − luminância). O claro some, o resto vira branco. */}
      <svg className="na-marquee__defs" aria-hidden="true" focusable="false">
        <filter id={KNOCKOUT_FILTER_ID} colorInterpolationFilters="sRGB">
          <feColorMatrix
            type="matrix"
            values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  -0.7016 -2.3602 -0.2383 3.3 0"
          />
        </filter>
      </svg>
      <p className="na-marquee__label">{label}</p>
      <div className="na-marquee__viewport" tabIndex={0}>
        <div className="na-marquee__track">
          {renderSet(0)}
          {renderSet(1)}
        </div>
      </div>
    </section>
  );
}
