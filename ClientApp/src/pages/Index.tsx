// Caminho: ClientApp/src/pages/Index.tsx

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

const Index = () => {
  // **LÓGICA DE LOGIN CORRETA**
  const handleLogin = async () => {
    try {
      // 1. Pede ao nosso backend a URL de login segura.
      //    O proxy do Vite irá redirecionar esta chamada para http://localhost:5277.
      const response = await fetch('/Authentication/OAuth/GetLoginUrl');
      
      if (!response.ok) {
        throw new Error(`Falha ao obter a URL de login. Status: ${response.status}`);
      }
      
      const data = await response.json();
      
      // 2. Redireciona o browser do utilizador para a URL de login da Autodesk que o backend forneceu.
      window.location.href = data.loginUrl;

    } catch (error) {
      console.error("Erro no processo de login:", error);
      alert("Não foi possível iniciar o login. Verifique a console (F12) para mais detalhes.");
    }
  };

  return (
    <div className="min-h-screen bg-background flex items-center justify-center relative overflow-hidden">
      {/* Efeitos de fundo */}
      <div className="absolute inset-0 bg-gradient-dark opacity-50"></div>
      <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-primary/50 rounded-full blur-3xl animate-pulse"></div>
      <div className="absolute bottom-1/4 right-1/4 w-80 h-80 bg-accent/50 rounded-full blur-3xl animate-pulse delay-1000"></div>
      
      <div className="relative z-10 w-full max-w-md px-6">
        {/* Logo */}
        <div className="text-center mb-8">
          {/* Garanta que o seu logo está na pasta 'ClientApp/public/' */}
          <img 
            src="/logo-na-consultores.png" 
            alt="N&A Consultores"
            className="mx-auto h-26 w-auto mb-6 filter brightness-110"
          />
        </div>

        {/* Cartão de Login */}
        <Card className="bg-card/60 backdrop-blur-sm border-border shadow-card">
          <CardHeader className="text-center space-y-2">
            <CardTitle className="text-3xl font-bold text-foreground">
              N&A Connect
            </CardTitle>
            <CardDescription className="text-muted-foreground">
              Portal de acesso aos serviços da plataforma Autodesk
            </CardDescription>
          </CardHeader>
          
          <CardContent className="space-y-2">
            <div className="text-center">
              <p className="text-sm text-muted-foreground mb-2">
                Clique no botão abaixo para aceder à plataforma APS da Autodesk
              </p>
              
              <Button 
                onClick={handleLogin}
                className="w-full bg-gradient-primary hover:shadow-glow transition-all duration-300 transform hover:scale-105 text-lg py-6 font-semibold"
                size="lg"
              >
                Fazer Login com Autodesk
              </Button>
            </div>
            
            <div className="pt-4 border-t border-border">
              <p className="text-xs text-center text-muted-foreground">
                Autodesk Platform Services (APS) Portal
              </p>
            </div>
          </CardContent>
        </Card>

        {/* Rodapé */}
        <div className="text-center mt-8">
          <p className="text-xs text-muted-foreground">
            © {new Date().getFullYear()} N&A Consultores. Todos os direitos reservados.
          </p>
        </div>
      </div>
    </div>
  );
};

export default Index;