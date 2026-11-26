import { cn } from "@/lib/utils";
import { NA_LOGO, type LogoAsset } from "./brandAssets";

export type ThemeLogoVariant = "default" | "sidebar" | "sidebar-compact";

interface ThemeLogoProps {
  className?: string;
  /**
   * "default": apresentação padrão (NotFound, Projects).
   * "sidebar": topo do sidebar aberto. "sidebar-compact": sidebar recolhido, Home e TV.
   */
  variant?: ThemeLogoVariant;
  /**
   * "auto" (padrão) segue o tema global. "escuro" força a versão negativa — para superfícies que são sempre escuras
   * em qualquer tema (lado da marca no login, sidebar azul da Gestão à Vista).
   */
  superficie?: "auto" | "escuro";
}

const ALTURA: Record<ThemeLogoVariant, string> = {
  default: "h-14",
  sidebar: "h-16",
  "sidebar-compact": "h-7",
};

const Imagem = ({ asset, className, alt }: { asset: LogoAsset; className?: string; alt: string }) => (
  <img src={asset.src} alt={alt} style={{ aspectRatio: asset.aspectRatio }} decoding="async"
    className={cn("w-auto max-w-full object-contain", className)} />
);

/**
 * Logo N&A que troca de arquivo conforme o tema. A troca é só CSS (classe .dark no <html>): reage na hora a
 * Claro/Escuro/Automático, sem recarregar e sem piscar na primeira pintura. Assets em brandAssets.ts.
 */
export function ThemeLogo({ className, variant = "default", superficie = "auto" }: ThemeLogoProps) {
  const altura = ALTURA[variant];
  const wrapper = cn("flex items-center", variant !== "default" && "w-full justify-center", className);
  const { escuro, claro, alt } = NA_LOGO;

  if (superficie === "escuro") {
    return <div className={wrapper}><Imagem asset={escuro} alt={alt} className={altura} /></div>;
  }

  return (
    <div className={wrapper}>
      {/* Tema escuro: versão negativa. */}
      <Imagem asset={escuro} alt={alt} className={cn(altura, "hidden dark:block")} />
      {/* Tema claro: selo oficial colorido em fundo claro. Sem placa/fundo artificial. */}
      <Imagem asset={claro} alt={alt} className={cn(altura, "block dark:hidden")} />
    </div>
  );
}
