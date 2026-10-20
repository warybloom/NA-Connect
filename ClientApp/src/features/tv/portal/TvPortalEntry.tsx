import type { ReactNode } from "react";
import { BrowserRouter } from "react-router-dom";
import { TvPortalGate } from "./TvPortalGate";

// Dedicated entry before the main router/providers: no robot polling, sidebar or admin modules mount here.
export function TvPortalEntry({ children }: { children: ReactNode }) {
  return window.location.pathname === "/tv/iniciar" ? <BrowserRouter>
    <TvPortalGate><p className="p-8">Acesso administrativo preservado. <a href="/dashboard">Abrir N&A Connect</a></p></TvPortalGate>
  </BrowserRouter> : <>{children}</>;
}
