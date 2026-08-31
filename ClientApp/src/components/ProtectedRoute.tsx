// Caminho: ClientApp/src/components/ProtectedRoute.tsx
import { useState, useEffect } from 'react';
import { Navigate, Outlet } from 'react-router-dom';

interface UserProfile {
  id?: number;
  nome?: string;
  email?: string;
  papel?: string;
}

const ProtectedRoute = () => {
  const [isAuthenticated, setIsAuthenticated] = useState<boolean | null>(null);
  const [user, setUser] = useState<UserProfile | null>(null);

useEffect(() => {
  const checkAuthStatus = async () => {
    try {
      // 1. Valida se o usuário tem sessão ativa enviando os cookies de sessão
      const statusResponse = await fetch('/Authentication/OAuth/CheckStatus', {
        credentials: 'include'
      });

      if (!statusResponse.ok) {
        setIsAuthenticated(false);
        return;
      }

      const statusData = await statusResponse.json();
      setIsAuthenticated(statusData.isAuthenticated);

      // 2. Busca o perfil e a role atribuída
      if (statusData.isAuthenticated) {
        try {
          const userResponse = await fetch('/Authentication/OAuth/GetCurrentUser', {
            credentials: 'include'
          });

          if (userResponse.ok) {
            const userData = await userResponse.json();

            // Garante que o retorno é válido e que o objeto de usuário existe
            if (userData.isAuthenticated && userData.user) {
              setUser({
                id: userData.user.UserID || userData.user.Id,
                nome: userData.user.Nome,
                email: userData.user.Email,
                papel: userData.role || userData.user.RoleSistema || userData.user.Papel
              });
            }
          }
        } catch (e) {
          console.warn("Não foi possível carregar os detalhes do usuário, mas a sessão está ativa.", e);
        }
      }
    } catch (error) {
      console.error("Erro ao verificar status de autenticação", error);
      setIsAuthenticated(false);
    }
  };

  checkAuthStatus();
}, []);

  // Enquanto está verificando a sessão
  if (isAuthenticated === null) {
    return (
      <div className="min-h-screen w-full flex items-center justify-center bg-background text-foreground">
        A verificar autenticação...
      </div>
    );
  }

  // Se não estiver autenticado, manda para a tela de login
  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return (
    <div className="min-h-screen flex flex-col bg-background text-foreground">
      {/* Exibe o aviso apenas se o papel for expressamente VIEWER */}
      {user?.papel === 'VIEWER' && (
        <div className="bg-amber-500/10 border-b border-amber-500/20 text-amber-600 dark:text-amber-400 text-center py-2 px-4 text-sm font-medium">
          ⚠️ Sua conta está em modo de <strong>Visualizador</strong>. Aguarde a liberação de permissões por um Administrador.
        </div>
      )}

      <div className="flex-1">
        <Outlet context={{ user }} />
      </div>
    </div>
  );
};

export default ProtectedRoute;