// Caminho: ClientApp/src/components/ProtectedRoute.tsx
import { useState, useEffect } from 'react';
import { Navigate, Outlet } from 'react-router-dom';

const ProtectedRoute = () => {
    const [isAuthenticated, setIsAuthenticated] = useState<boolean | null>(null);

    useEffect(() => {
        const checkAuthStatus = async () => {
            try {
                // Pergunta ao nosso backend se o utilizador tem uma sessão ativa
                const response = await fetch('/Authentication/OAuth/CheckStatus');
                const data = await response.json();
                setIsAuthenticated(data.isAuthenticated);
            } catch (error) {
                console.error("Erro ao verificar status de autenticação", error);
                setIsAuthenticated(false);
            }
        };

        checkAuthStatus();
    }, []);

    // Enquanto estamos a verificar, não mostramos nada (ou um spinner de carregamento)
    if (isAuthenticated === null) {
        return <div className="min-h-screen w-full flex items-center justify-center bg-background text-foreground">A verificar autenticação...</div>;
    }

    // Se estiver autenticado, renderiza a página pedida (Hubs, Projetos, etc.)
    // O <Outlet /> é o espaço onde a página filha será inserida.
    return isAuthenticated ? <Outlet /> : <Navigate to="/login" />;
};

export default ProtectedRoute;