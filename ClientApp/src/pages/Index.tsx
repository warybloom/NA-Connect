import { useEffect, useRef, useState } from "react";

import toast from 'react-hot-toast';
import { LoginHero } from "../features/login/LoginHero";
import { LoginActions } from "../features/login/LoginActions";
import { ClientMarquee } from "../features/login/ClientMarquee";
import { CLIENTS } from "../features/login/clients";
import { LOGIN_BRAND } from "../features/login/loginMedia";
import { AuthShell } from "../features/auth/AuthShell";
import { PixelTransitionOverlay, usePixelTransition } from "../features/auth/AuthTransition";
import "../features/login/login.css";
import "../features/auth/auth.css";

/**
 * Interface rigorosa para a resposta de login.
 * Sincronizada com o retorno do AuthController no C#.
 */
interface LoginResponse {
  loginUrl: string;
}

const Index = () => {
  const [view, setView] = useState<"hero" | "auth">("hero");
  const { phase, reduced, run } = usePixelTransition();
  const naButtonRef = useRef<HTMLButtonElement>(null);
  const voltouDoAuth = useRef(false);

  // Ao voltar do Auth Shell, o foco retorna ao CTA que o abriu (teclado/leitor de tela não perdem o contexto).
  useEffect(() => {
    if (view === "hero" && voltouDoAuth.current) {
      voltouDoAuth.current = false;
      naButtonRef.current?.focus({ preventScroll: true });
    }
  }, [view]);

  /**
   * ✅ PENTE FINO: Lógica de Login Preservada.
   *
   */
  const handleLogin = async () => {
    const toastId = toast.loading("Preparando acesso à Autodesk...");

    try {
      /**
       * Chamada ao backend para obter a URL de autorização APS.
       * credentials: 'include' adicionado para suporte ao Proxy do Vite.
       *
       */
      const response = await fetch('/Authentication/OAuth/GetLoginUrl', {
        credentials: 'include'
      });

      if (!response.ok) {
        throw new Error("Servidor Forges Sync Connect não respondeu adequadamente.");
      }

      const data: LoginResponse = await response.json();

      // ✅ REDIRECIONAMENTO: Mesma lógica da versão funcional original
      if (data.loginUrl) {
        window.location.href = data.loginUrl;
      } else {
        throw new Error("URL de login ausente na resposta.");
      }

    } catch (error: unknown) {
      const message = error instanceof Error ? error.message : "Falha na conexão.";
      console.error("Erro no fluxo OAuth:", error);
      toast.error(message, { id: toastId });
    }
  };

  // Login N&A local: cinematográfico → pixels → Auth Shell (sem trocar de rota).
  const abrirLoginNa = () => run(() => setView("auth"));
  const voltarParaHero = () =>
    run(() => {
      voltouDoAuth.current = true;
      setView("hero");
    });

  // Sessão N&A criada (Auth:UserId): segue o mesmo destino do callback Autodesk ("/"), onde ProtectedRoute
  // valida a sessão por CheckStatus e RotaInicialPermitida escolhe a tela inicial.
  const entrar = () => {
    // Troca de identidade: remover o projeto e os filtros da conta anterior.
    for (const key of ["na.projetoAtual", "na.projetosAnalise", "na.controleProducao.escopo"]) {
      try { sessionStorage.removeItem(key); } catch { /* navegação continua */ }
    }
    // Recriar todos os Providers para impedir cache de permissões da conta antiga.
    window.location.replace("/");
  };

  return (
    <div className="na-login">
      {view === "hero" ? (
        <LoginHero>
          <header className="na-login__header">
            <img
              className="na-login__brand"
              src={LOGIN_BRAND.src}
              alt={LOGIN_BRAND.alt}
              style={{ aspectRatio: LOGIN_BRAND.aspectRatio }}
              decoding="async"
            />
          </header>

          <main className="na-login__main">
            <h1 className="na-login__title">N&amp;A Connect</h1>
            <p className="na-login__tagline">
              <span>Engenharia conectada.</span>
              <span>Informação confiável.</span>
              <span>Projetos sob controle.</span>
            </p>
            <LoginActions ref={naButtonRef} onAutodeskLogin={handleLogin} onNaLogin={abrirLoginNa} />
          </main>

          <footer>
            <ClientMarquee clients={CLIENTS} label="Clientes N&A" />
          </footer>
        </LoginHero>
      ) : (
        <AuthShell onBackToHero={voltarParaHero} onAuthenticated={entrar} />
      )}
      <PixelTransitionOverlay phase={phase} reduced={reduced} />
    </div>
  );
};

export default Index;
