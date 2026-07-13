// Caminho: ClientApp/src/components/ui/breadcrumbs.tsx

import { Fragment } from 'react';
import { ChevronRight } from 'lucide-react';
import { cn } from '@/lib/utils';

// Define a estrutura de cada "migalha" do caminho
export interface BreadcrumbItem {
  id: string;
  name: string;
}

interface BreadcrumbsProps {
  // O array com o caminho a ser exibido
  path: BreadcrumbItem[];
  // Função a ser chamada quando um item do caminho é clicado
  onPathClick: (itemId: string, index: number) => void;
  className?: string;
}

export function Breadcrumbs({ path, onPathClick, className }: BreadcrumbsProps) {
  return (
    <nav className={cn("flex items-center gap-2 text-sm text-muted-foreground", className)}>
      {path.map((item, index) => {
        // Verifica se é o último item da lista
        const isLast = index === path.length - 1;

        return (
          <Fragment key={item.id}>
            {/* Adiciona o separador "/" antes de cada item, exceto o primeiro */}
            {index > 0 && <ChevronRight className="h-4 w-4" />}

            <span
              onClick={() => !isLast && onPathClick(item.id, index)}
              className={cn(
                "hover:text-primary",
                {
                  "text-foreground font-medium": isLast, // Estilo diferente para o item atual
                  "cursor-pointer": !isLast,           // Cursor de link para itens clicáveis
                }
              )}
            >
              {item.name}
            </span>
          </Fragment>
        );
      })}
    </nav>
  );
}