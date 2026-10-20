// Ficheiro: ClientApp/src/features/login/loginMedia.ts
// Assets exclusivos da tela de login. Para trocar, substitua o arquivo em ClientApp/public mantendo o nome,
// ou altere o caminho aqui. Arquivo ausente não quebra a tela: o hero cai no fundo navy com a malha técnica.

import { NA_LOGO } from "../theme/brandAssets";

export const LOGIN_BRAND = {
  /** Logo N&A | 30 anos, versão negativa (branco + ciano) recortada sem margem transparente. */
  src: NA_LOGO.escuro.src, // fundo do login é sempre navy: versão negativa oficial (features/theme/brandAssets.ts)
  alt: "N&A Consultores — 30 anos",
  /** Proporção largura/altura do arquivo, usada para reservar espaço antes da imagem carregar. */
  aspectRatio: 1345 / 479,
} as const;

export const LOGIN_HERO_MEDIA = {
  /** Vídeo de fundo (MP4/H.264, sem áudio, ~10–20 s em loop, idealmente < 6 MB). */
  video: "/media/login/hero-na.mp4",
  /** Primeiro quadro do vídeo ou foto de projeto; aparece de imediato e substitui o vídeo se ele falhar. */
  poster: "/media/login/hero-poster.webp",
} as const;
