// Caminho: ClientApp/src/pages/Hubs.tsx

import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";

// Define a estrutura (o "tipo") de um Hub para o TypeScript
interface Hub {
    id: string;
    attributes: {
        name: string;
        region: string;
    };
}

// --- Componente para o "Esqueleto de Carregamento" ---
const HubCardSkeleton = () => (
    <div className="bg-card/80 border-border rounded-lg p-6 animate-pulse">
        <div className="h-6 bg-muted rounded w-3/4 mb-2"></div>
        <div className="h-4 bg-muted rounded w-1/2"></div>
        <div className="mt-8 h-10 bg-secondary rounded"></div>
    </div>
);

// --- Componente Principal da Página de Hubs ---
const Hubs = () => {
    const [hubs, setHubs] = useState<Hub[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [userName, setUserName] = useState<string>(''); // Para guardar o nome do utilizador

    useEffect(() => {
        // Função para buscar os dados do backend
        const fetchData = async () => {
            try {
                // Em paralelo, busca os hubs e o nome do utilizador
                const [hubsResponse, userResponse] = await Promise.all([
                    fetch('/DataManagement/Data/Hubs'),
                    fetch('/Authentication/OAuth/GetUserName') // Assumindo que esta rota existe
                ]);

                if (!hubsResponse.ok) throw new Error(`Erro ao buscar hubs: ${hubsResponse.statusText}`);
                if (!userResponse.ok) throw new Error(`Erro ao buscar nome do utilizador: ${userResponse.statusText}`);
                
                const hubsData = await hubsResponse.json();
                const userData = await userResponse.json();

                setHubs(hubsData.data);
                setUserName(userData.userName);

            } catch (e: any) {
                setError(e.message);
            } finally {
                setLoading(false);
            }
        };

        fetchData();
    }, []); // O array vazio [] significa que este efeito corre apenas uma vez

    return (
        <div>
            {/* Cabeçalho da Página */}
            <div className="mb-8">
                <h1 className="text-3xl font-bold text-foreground">
                    Bem-vindo, {loading ? '...' : userName}!
                </h1>
                <p className="text-muted-foreground mt-2">Selecione uma conta (Hub) para visualizar os projetos.</p>
            </div>

            {/* Grelha de Conteúdo */}
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
                {loading ? (
                    // Se estiver a carregar, mostra 8 "esqueletos"
                    Array.from({ length: 8 }).map((_, index) => <HubCardSkeleton key={index} />)
                ) : error ? (
                    // Se houver um erro, mostra uma mensagem
                    <div className="col-span-full text-center text-destructive bg-destructive/10 p-4 rounded-lg">
                        <p className="font-bold">Ocorreu um Erro</p>
                        <p>{error}</p>
                    </div>
                ) : hubs.length === 0 ? (
                    // Se não houver contas, mostra uma mensagem
                     <div className="col-span-full text-center text-muted-foreground bg-card/80 p-8 rounded-lg">
                        <p>Nenhuma conta (Hub) encontrada.</p>
                    </div>
                ) : (
                    // Se tudo estiver OK, mostra os cartões das contas
                    hubs.map((hub) => (
                        <Card key={hub.id} className="bg-card/90 border-border hover:border-primary transition-all duration-300 flex flex-col group transform hover:-translate-y-1">
                            <CardHeader className="flex-row items-center gap-4">
                                <div className="bg-primary/10 text-primary p-3 rounded-lg">
                                    <i className="fas fa-cloud fa-lg"></i>
                                </div>
                                <div>
                                    <CardTitle className="text-foreground group-hover:text-primary transition-colors">{hub.attributes.name}</CardTitle>
                                    <CardDescription>Região: {hub.attributes.region.toUpperCase()}</CardDescription>
                                </div>
                            </CardHeader>
                            <CardContent className="flex-grow"></CardContent>
                            <div className="p-4 pt-0">
                                <Link 
                                    to={`/contas/${hub.id}/projetos`} 
                                    className="w-full text-center bg-secondary text-secondary-foreground hover:bg-secondary/80 font-bold py-2 px-4 rounded transition-all"
                                >
                                    Ver Projetos
                                </Link>
                            </div>
                        </Card>
                    ))
                )}
            </div>
        </div>
    );
};

export default Hubs;