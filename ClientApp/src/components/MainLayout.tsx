// ClientApp/src/components/MainLayout.tsx
import { Outlet, useLocation } from "react-router-dom";
import { AnimatePresence, motion } from "framer-motion";
import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar";
import { AppSidebar } from "@/components/app-sidebar"; // Ajuste o caminho da sua Sidebar

export function MainLayout() {
  const location = useLocation();

  return (
    <SidebarProvider>
      {/* 1. Sidebar FIXA — NUNCA desmonta nem recarrega ao trocar de tela */}
      <AppSidebar />

      {/* 2. SidebarInset garante que o conteúdo ocupe o espaço correto ao lado da Sidebar */}
      <SidebarInset className="flex min-h-screen flex-1 flex-col overflow-x-hidden">
        
        {/* 3. AnimatePresence anima APENAS o conteúdo das rotas filhas */}
        <AnimatePresence mode="wait">
          <motion.main
            key={location.pathname}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            transition={{ duration: 0.2, ease: "easeInOut" }}
            className="flex-1 p-6"
          >
            <Outlet />
          </motion.main>
        </AnimatePresence>

      </SidebarInset>
    </SidebarProvider>
  );
}