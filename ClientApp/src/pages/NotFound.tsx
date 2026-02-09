// Caminho: ClientApp/src/pages/NotFound.tsx

import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";

export default function NotFound() {
  return (
    <div className="min-h-screen flex flex-col items-center justify-center bg-background text-foreground text-center p-4">
      <h1 className="text-6xl font-bold text-primary mb-4">404</h1>
      <h2 className="text-2xl font-semibold mb-2">Página Não Encontrada</h2>
      <p className="text-muted-foreground mb-6">
        Lamentamos, mas a página que você procura não existe ou foi movida.
      </p>
      <Button asChild>
        <Link to="/dashboard">Voltar para o Início</Link>
      </Button>
    </div>
  );
}