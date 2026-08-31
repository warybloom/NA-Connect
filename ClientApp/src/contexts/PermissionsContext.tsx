// src/contexts/PermissionsContext.tsx
import { createContext, useContext, useEffect, useState, ReactNode } from "react";
import { matchPath } from "react-router-dom";

interface PermissionsContextType {
  rotasPermitidas: string[];
  loading: boolean;
  temAcesso: (rota: string) => boolean;
}

const PermissionsContext = createContext<PermissionsContextType>({
  rotasPermitidas: [],
  loading: true,
  temAcesso: () => false,
});

export function PermissionsProvider({ children }: { children: ReactNode }) {
  const [rotasPermitidas, setRotasPermitidas] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch("/api/AdminMaster/me/permissions", { credentials: "include" })
      .then((res) => {
        if (!res.ok) throw new Error("Não autenticado");
        return res.json();
      })
      .then(setRotasPermitidas)
      .catch(() => setRotasPermitidas([]))
      .finally(() => setLoading(false));
  }, []);

  const rotasSempreLiberadas = ["/projects"]

  const temAcesso = (rota: string) => {
  const rotaDecodificada = decodeURIComponent(rota);
  if (rotasSempreLiberadas.includes(rotaDecodificada)) return true;
  return rotasPermitidas.some((padrao) => matchPath(padrao, rotaDecodificada) !== null);
};

  return (
    <PermissionsContext.Provider value={{ rotasPermitidas, loading, temAcesso }}>
      {children}
    </PermissionsContext.Provider>
  );
}

export const usePermissions = () => useContext(PermissionsContext);