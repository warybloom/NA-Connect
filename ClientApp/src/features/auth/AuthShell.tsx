// Ficheiro: ClientApp/src/features/auth/AuthShell.tsx
// Auth Shell N&A: identidade à esquerda (navy, marca, clientes), formulário protagonista à direita.
import { useState } from "react";
import { ArrowLeft } from "lucide-react";
import { ClientMarquee } from "../login/ClientMarquee";
import { CLIENTS } from "../login/clients";
import { LOGIN_BRAND } from "../login/loginMedia";
import { NaLoginForm } from "./NaLoginForm";
import { ForgotPasswordInfo, RequestAccessInfo } from "./AuthInfoPanels";
import type { NaLoginUser } from "./naLoginApi";
import { ThemeSelector } from "../theme/ThemeSelector";

export type AuthShellView = "login" | "forgot" | "request";

interface AuthShellProps {
  onBackToHero: () => void;
  onAuthenticated: (user: NaLoginUser) => void;
}

export function AuthShell({ onBackToHero, onAuthenticated }: AuthShellProps) {
  const [view, setView] = useState<AuthShellView>("login");
  const voltarParaLogin = () => setView("login");

  return (
    <div className="na-auth">
      <aside className="na-auth__identity">
        <div className="na-auth__identity-grid" aria-hidden="true" />
        <header className="na-auth__identity-head">
          <img
            className="na-auth__brand"
            src={LOGIN_BRAND.src}
            alt={LOGIN_BRAND.alt}
            style={{ aspectRatio: LOGIN_BRAND.aspectRatio }}
            decoding="async"
          />
        </header>
        <div className="na-auth__identity-body">
          <h1 className="na-auth__title">N&amp;A Connect</h1>
          <p className="na-auth__tagline">
            <span>Engenharia conectada.</span>
            <span>Informação confiável.</span>
            <span>Projetos sob controle.</span>
          </p>
        </div>
        <div className="na-auth__identity-foot">
          <ClientMarquee clients={CLIENTS} label="Clientes N&A" />
        </div>
      </aside>

      <main className="na-auth__panel">
        <div className="na-auth__panel-top">
          <button type="button" className="na-auth-back na-auth-back--quiet" onClick={onBackToHero}>
            <ArrowLeft aria-hidden="true" />
            Outras formas de entrar
          </button>
          <ThemeSelector />
        </div>
        <div className="na-auth__panel-body">
          {view === "login" && (
            <NaLoginForm
              autoFocus
              onAuthenticated={onAuthenticated}
              onForgotPassword={() => setView("forgot")}
              onRequestAccess={() => setView("request")}
            />
          )}
          {view === "forgot" && <ForgotPasswordInfo onBack={voltarParaLogin} />}
          {view === "request" && <RequestAccessInfo onBack={voltarParaLogin} />}
        </div>
        <p className="na-auth__panel-foot">N&amp;A Consultores · Ambiente seguro</p>
      </main>
    </div>
  );
}
