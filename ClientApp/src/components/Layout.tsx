// Caminho: ClientApp/src/components/Layout.tsx
import { Outlet, NavLink } from "react-router-dom";

// Componente para um item do menu com estilo ativo
const SidebarNavLink = ({ to, children }: { to: string, children: React.ReactNode }) => {
    return (
        <NavLink
            to={to}
            className={({ isActive }) =>
                `flex items-center p-3 my-1 rounded-lg transition-colors duration-200 ${
                    isActive
                        ? 'bg-sidebar-primary text-sidebar-primary-foreground shadow-sm'
                        : 'text-sidebar-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground'
                }`
            }
        >
            {children}
        </NavLink>
    );
};

const Layout = () => {
    return (
        <div className="min-h-screen w-full flex bg-background text-foreground">
            {/* Barra Lateral de Navegação */}
            <aside className="w-64 bg-sidebar-background border-r border-sidebar-border flex-shrink-0 p-4 flex flex-col">
                <div className="mb-8 text-center">
                    <h1 className="text-2xl font-bold text-sidebar-primary">N&A Connect</h1>
                </div>
                <nav className="flex-grow">
                    <SidebarNavLink to="/contas">
                        <i className="fas fa-cloud w-6 h-6 mr-3"></i>
                        Contas
                    </SidebarNavLink>
                    {/* Adicione outros links aqui no futuro, como Projetos */}
                </nav>
                <div className="mt-auto">
                     <a href="/Authentication/OAuth/Logout" className="flex items-center p-3 rounded-lg text-sidebar-foreground hover:bg-red-500/10 hover:text-red-500">
                        <i className="fas fa-sign-out-alt w-6 h-6 mr-3"></i>
                        Sair
                    </a>
                </div>
            </aside>

            {/* Conteúdo Principal da Página */}
            <main className="flex-1 p-8 overflow-auto">
                {/* O Outlet é onde as páginas (Contas, Projetos, etc.) serão renderizadas */}
                <Outlet />
            </main>
        </div>
    );
};

export default Layout;