import { useState, useEffect } from "react";
import { Outlet, Navigate } from "react-router-dom";
import { authenticatedDestination } from "@/features/auth/authenticatedDestination";

/**
 * Interface rigorosa para a resposta de autenticação.
 * Sincronizada com o retorno do AuthController no C#.
 */
interface AuthStatusResponse {
  isAuthenticated: boolean;
}

const ProtectedRoute = () => {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean | null>(null);

  useEffect(() => {
    const abort = new AbortController();
    const checkAuth = async () => {
      try {
        /**
         * ✅ LÓGICA PRESERVADA: Consulta ao backend para validar AccessToken.
         * credentials: 'include' garante a leitura do cookie de sessão.
         *
         */
        const response = await fetch("/Authentication/OAuth/CheckStatus", {
          credentials: 'include',
          signal: abort.signal,
        });

        // Se o servidor negar o acesso (Sessão expirada no ACC/C#)
        if (response.status === 401) {
          setIsAuthenticated(false);
          return;
        }

        const data: AuthStatusResponse = await response.json();
        if (!response.ok || data.isAuthenticated !== true) {
          if (!abort.signal.aborted) setIsAuthenticated(false);
          return;
        }
        const destination = await authenticatedDestination(fetch, abort.signal);
        if (abort.signal.aborted) return;
        if (destination) {
          // Full navigation mounts the dedicated TV entry before any admin providers.
          window.location.replace(destination);
          return;
        }
        setIsAuthenticated(true);
      } catch (error: unknown) {
        if (abort.signal.aborted) return;
        // ✅ CORREÇÃO: Tipagem 'unknown' para satisfazer o TypeScript
        console.error("Erro crítico na validação de segurança Forges Sync Connect:", error);
        setIsAuthenticated(false);
      }
    };
    checkAuth();
    return () => abort.abort();
  }, []);

  /**
   * ✅ LAYOUT PREMIUM: Estrutura de loading com identidade N&A.
   *
   */
  if (isAuthenticated === null) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background">
        <div className="flex items-center gap-3 text-muted-foreground/60 animate-pulse">
          <div className="h-5 w-5 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <span className="text-[10px] font-black uppercase tracking-[0.3em]">
            Validando credenciais N&A...
          </span>
        </div>
      </div>
    );
  }

  // ✅ REDIRECIONAMENTO: Lógica original preservada
  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />;
};

export default ProtectedRoute;
