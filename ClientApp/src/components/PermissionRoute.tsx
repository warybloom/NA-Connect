// src/components/PermissionRoute.tsx
import { Navigate, useLocation, Outlet } from "react-router-dom";
import { usePermissions } from "@/contexts/PermissionsContext";

export default function PermissionRoute() {
  const { temAcesso, loading } = usePermissions();
  const location = useLocation();

  if (loading) return null;

  if (!temAcesso(location.pathname)) {
    

    if (location.pathname=="/projects"){
             return (
        <div className="min-h-screen flex items-center justify-center text-center p-6">
          <div>
            <p className="text-lg font-medium">Você não tem permissão para acessar o sistema.</p>
            <p className="text-sm text-muted-foreground mt-2">Contate um administrador.</p>
          </div>
        </div>
      );
    }
    return <Navigate to = "/projects" replace />;
  }

  return <Outlet />;
}