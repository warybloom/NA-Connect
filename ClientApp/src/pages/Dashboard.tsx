// Caminho: ClientApp/src/pages/Dashboard.tsx (Conectado à API)
import { useState, useEffect } from "react";
import { AnimatedBackground } from "@/components/ui/animated-background";
import { Logo } from "@/components/ui/logo";
import { ElegantCard } from "@/components/ui/elegant-card";
import { Button } from "@/components/ui/button";
import { useNavigate } from "react-router-dom";
import { ArrowLeftIcon } from "@heroicons/react/24/solid";

interface Hub {
  id: string;
  attributes: { name: string; region: string; };
}

export default function Dashboard() {
  const navigate = useNavigate();
  const [hubs, setHubs] = useState<Hub[]>([]);
  const [userName, setUserName] = useState("...");

  useEffect(() => {
    const fetchData = async () => {
      // Busca os hubs e o nome do utilizador em paralelo
      const [hubsResponse, userResponse] = await Promise.all([
        fetch('/DataManagement/Data/Hubs'),
        fetch('/Authentication/OAuth/GetUserName')
      ]);

      if (hubsResponse.ok) {
        const hubsData = await hubsResponse.json();
        setHubs(hubsData.data);
      }
      if (userResponse.ok) {
        const userData = await userResponse.json();
        setUserName(userData.userName);
      }
    };
    fetchData();
  }, []);

  return (
    
    <AnimatedBackground>
      <div className="min-h-screen p-6">
        <header className="flex items-center justify-between mb-8">
          <Logo />
          <nav className="flex items-center gap-4">
             <a href="/Authentication/OAuth/Logout" className="text-sm text-muted-foreground hover:text-primary">Sair</a>
          </nav>
        </header>
        

        <div className="max-w-4xl mx-auto">
          <div className="mb-8">
            <h1 className="text-4xl font-bold text-foreground mb-2">
              Bem-vindo, {userName}!
            </h1>
            <p className="text-muted-foreground">
              Selecione uma conta (Hub) para visualizar os projetos.
            </p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
            {hubs.length > 0 ? hubs.map(hub => (
              <ElegantCard
                key={hub.id}
                title={hub.attributes.name}
                description={`Região: ${hub.attributes.region.toUpperCase()}`}
                buttonText="Ver Projetos"
                onButtonClick={() => navigate(`/projects/${hub.id}`)}
              />
            )) : (
                <p className="text-muted-foreground">A carregar contas...</p>
            )}
          </div>
        </div>
      </div>
    </AnimatedBackground>
  );
}