import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Building2, Globe, ArrowRight, LayoutGrid } from "lucide-react";
import toast from 'react-hot-toast';

import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

// ✅ Interface rigorosa para evitar 'any'
interface Hub {
  id: string;
  attributes: {
    name: string;
    region: string;
  };
}

export default function Hubs() {
  const navigate = useNavigate();
  const [hubs, setHubs] = useState<Hub[]>([]);
  const [userName, setUserName] = useState<string>("...");
  const [loading, setLoading] = useState(true);

  /**
   * ✅ PENTE FINO - BUSCA DE DADOS:
   * Mantém a lógica de buscar Hubs e Usuário em paralelo para performance.
   *
   */
  useEffect(() => {
    const fetchInitialData = async () => {
      setLoading(true);
      try {
        const [hRes, uRes] = await Promise.all([
          fetch('/DataManagement/Data/Hubs', { credentials: 'include' }),
          fetch('/Authentication/OAuth/GetUserName', { credentials: 'include' })
        ]);

        if (hRes.status === 401) { navigate('/login'); return; }

        if (hRes.ok) {
          const data = await hRes.json();
          setHubs(data.data || []);
        }

        if (uRes.ok) {
          const userData = await uRes.json();
          setUserName(userData.userName);
        }
      } catch (error: unknown) {
        console.error("Erro ao carregar contas:", error);
        toast.error("Falha ao conectar com o servidor N&A.");
      } finally {
        setLoading(false);
      }
    };

    fetchInitialData();
  }, [navigate]);

  return (
    <div className="min-h-[80vh] flex flex-col animate-enter">

      {/* Header de Boas-vindas com Identidade N&A */}
      <div className="mb-12 space-y-2">
        <div className="flex items-center gap-3 text-primary mb-2">
          <LayoutGrid className="w-5 h-5" />
          <span className="text-[10px] font-black uppercase tracking-[0.3em]">Seletor de Organização</span>
        </div>
        <h1 className="text-4xl font-black text-foreground tracking-tighter uppercase italic">
          Olá, {userName.split(' ')[0]}
        </h1>
        <p className="text-muted-foreground font-medium max-w-2xl">
          Selecione a empresa ou conta corporativa que deseja gerenciar no ambiente Autodesk Construction Cloud.
        </p>
      </div>

      {/* Grid de Hubs (Contas) com Visual Premium */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        {loading ? (
          // Skeleton loaders para feedback visual
          [1, 2, 3].map(i => (
            <div key={i} className="h-48 rounded-2xl bg-card/20 border border-border/50 animate-pulse" />
          ))
        ) : hubs.length > 0 ? (
          hubs.map((hub) => (
            <div
              key={hub.id}
              className="group relative flex flex-col justify-between p-8 rounded-2xl border border-border/50 glass-card transition-all duration-500 hover:border-primary/50 hover:shadow-2xl hover:shadow-primary/5 overflow-hidden"
            >
              {/* Efeito visual de fundo */}
              <div className="absolute top-0 right-0 p-4 opacity-5 group-hover:opacity-10 transition-opacity">
                <Building2 className="w-24 h-24 rotate-12" />
              </div>

              <div className="relative z-10">
                <div className="w-12 h-12 rounded-xl bg-primary/10 flex items-center justify-center mb-6 border border-primary/20 group-hover:scale-110 transition-transform duration-500">
                  <Building2 className="w-6 h-6 text-primary" />
                </div>

                <h3 className="text-xl font-black text-foreground uppercase italic tracking-tight mb-2 group-hover:text-primary transition-colors">
                  {hub.attributes.name}
                </h3>

                <div className="flex items-center gap-2 text-[10px] font-bold text-muted-foreground uppercase tracking-widest">
                  <Globe className="w-3 h-3" />
                  Região: {hub.attributes.region.toUpperCase()}
                </div>
              </div>

              <Button
                onClick={() => navigate(`/projects/${hub.id}`)}
                className="mt-8 w-full font-black text-[10px] tracking-[0.2em] uppercase gap-2 shadow-lg shadow-primary/20"
              >
                ACESSAR CONTA
                <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
              </Button>
            </div>
          ))
        ) : (
          <div className="col-span-full py-20 text-center border-2 border-dashed border-border/40 rounded-2xl">
            <p className="text-muted-foreground font-bold uppercase tracking-widest italic">Nenhuma conta vinculada ao seu perfil Autodesk.</p>
          </div>
        )}
      </div>

      <div className="mt-auto pt-12 flex justify-center">
        <p className="text-[9px] text-muted-foreground/40 font-black uppercase tracking-[0.5em]">
          Forges Sync Connect • Enterprise Selection Gateway
        </p>
      </div>
    </div>
  );
}
