import type { ReactNode } from "react";
import { Navigate, Outlet } from "react-router-dom";
import { usePermissions } from "@/features/permissions/PermissionProvider";
import { podeAcessarBiBml011016 } from "./acessoBiBml011016";

export function MasterAdminOnly({ children }: { children?: ReactNode }) {
  const { loading, error, effective } = usePermissions();
  if (loading) return null;
  if (error || effective?.isMasterAdmin !== true) return <Navigate to="/" replace />;
  return children ?? <Outlet />;
}
