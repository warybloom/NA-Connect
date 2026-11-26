import type { ReactNode } from "react";
import { ThemeProvider as NextThemesProvider, useTheme } from "next-themes";
import { normalizarPreferencia, PREFERENCIA_PADRAO, THEME_STORAGE_KEY, type PreferenciaTema, type TemaResolvido } from "./themeLogic";

/**
 * Provedor ÚNICO de tema da aplicação (next-themes, já usado pelo sonner). Aplica a classe "dark"/"light" no <html>,
 * persiste no localStorage, acompanha o sistema operacional no modo Automático (inclusive mudanças ao vivo) e
 * sincroniza abas abertas. Os tokens de cor estão em src/index.css (:root = claro, .dark = escuro).
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  return (
    <NextThemesProvider
      attribute="class"
      defaultTheme={PREFERENCIA_PADRAO}
      enableSystem
      storageKey={THEME_STORAGE_KEY}
      themes={["light", "dark"]}
      disableTransitionOnChange
    >
      {children}
    </NextThemesProvider>
  );
}

/** Preferência escolhida (light/dark/system) e o tema efetivamente aplicado. */
export function useTemaApp(): { preferencia: PreferenciaTema; tema: TemaResolvido; definir: (p: PreferenciaTema) => void } {
  const { theme, resolvedTheme, setTheme } = useTheme();
  const preferencia = normalizarPreferencia(theme);
  const tema: TemaResolvido = resolvedTheme === "light" ? "light" : "dark";
  return { preferencia, tema, definir: setTheme };
}
