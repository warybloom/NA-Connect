// Slot do header GLOBAL do Layout: uma página pode substituir o título (lado esquerdo) e inserir ações compactas
// (lado direito, antes de notificação/configuração) sem criar um header próprio dentro da página.
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

interface PageHeaderSlot { title?: ReactNode; actions?: ReactNode }

const Ctx = createContext<{ slot: PageHeaderSlot; setSlot: (s: PageHeaderSlot) => void }>({ slot: {}, setSlot: () => {} });

export function PageHeaderProvider({ children }: { children: ReactNode }) {
  const [slot, setSlot] = useState<PageHeaderSlot>({});
  return <Ctx.Provider value={{ slot, setSlot }}>{children}</Ctx.Provider>;
}

export const usePageHeaderSlot = () => useContext(Ctx).slot;

/** A página registra título/ações enquanto estiver montada; ao sair, o header volta ao padrão. */
export function usePageHeader(title: ReactNode | undefined, actions: ReactNode | undefined, deps: unknown[]) {
  const { setSlot } = useContext(Ctx);
  useEffect(() => {
    setSlot({ title, actions });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  useEffect(() => () => setSlot({}), [setSlot]);
}
