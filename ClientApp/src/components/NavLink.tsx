import { NavLink as RouterNavLink, NavLinkProps } from "react-router-dom";
import { forwardRef } from "react";
/**
 * ✅ CORREÇÃO DEFINITIVA: 
 * Usamos o alias '@' que aponta para a pasta 'src'. 
 * Isso elimina a confusão de '../../' e funciona em qualquer nível de pasta.
 * Baseado no alias configurado no seu vite.config.ts.
 */
import { cn } from "@/lib/utils"; 

interface NavLinkCompatProps extends Omit<NavLinkProps, "className"> {
  className?: string;
  activeClassName?: string;
  pendingClassName?: string;
}

/**
 * Componente NavLink customizado Forges Sync Connect.
 * Ideal para a Sidebar do Dashboard, permitindo estilos diferentes para links ativos
 * enquanto navega entre os projetos do seu DataController.cs.
 */
const NavLink = forwardRef<HTMLAnchorElement, NavLinkCompatProps>(
  ({ className, activeClassName, pendingClassName, to, ...props }, ref) => {
    return (
      <RouterNavLink
        ref={ref}
        to={to}
        // Lógica de classes unificada com 'cn' para suportar Tailwind dinâmico
        className={({ isActive, isPending }) =>
          cn(
            className, 
            isActive && activeClassName, 
            isPending && pendingClassName
          )
        }
        {...props}
      />
    );
  },
);

NavLink.displayName = "NavLink";

export { NavLink };