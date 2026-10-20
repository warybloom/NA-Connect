// Logos oficiais do selo comemorativo N&A 30 anos, por tema.
// Os PNGs em /brands/na foram recortados APENAS nas margens transparentes dos originais em /logos.
// A paleta, tipografia e forma das artes oficiais foram preservadas.
export interface LogoAsset {
  src: string;
  aspectRatio: number;
}

export const NA_LOGO: { escuro: LogoAsset; claro: LogoAsset; alt: string } = {
  escuro: { src: "/brands/na/na-30-anos-negativo.png", aspectRatio: 1345 / 479 },
  claro: { src: "/brands/na/na-30-anos-colorido.png", aspectRatio: 1345 / 479 },
  alt: "N&A Consultores — 30 anos",
};
