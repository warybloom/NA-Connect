// Proteção de alterações não salvas: confirma antes de trocar de perfil/aba, sair pelo menu (links internos) ou
// fechar/recarregar a página. O app usa <BrowserRouter> (sem useBlocker), por isso os links são interceptados aqui.
import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";

export function useAlteracoesPendentes(pendente: boolean) {
  const navigate = useNavigate();
  const [acao, setAcao] = useState<(() => void) | null>(null);
  const pendenteRef = useRef(pendente);
  pendenteRef.current = pendente;

  /** Executa já, ou pede confirmação se houver alterações não salvas. */
  const confirmarSe = useCallback((executar: () => void) => {
    if (pendenteRef.current) setAcao(() => executar);
    else executar();
  }, []);

  useEffect(() => {
    if (!pendente) return;
    const antesDeSair = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = ""; };
    const clique = (e: MouseEvent) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      const a = (e.target as HTMLElement | null)?.closest?.("a[href]") as HTMLAnchorElement | null;
      if (!a || a.target === "_blank") return;
      const url = new URL(a.href, window.location.href);
      if (url.origin !== window.location.origin) return;
      if (url.pathname === window.location.pathname && url.search === window.location.search) return;
      e.preventDefault();
      e.stopPropagation();
      setAcao(() => () => navigate(url.pathname + url.search + url.hash));
    };
    window.addEventListener("beforeunload", antesDeSair);
    document.addEventListener("click", clique, true);
    return () => {
      window.removeEventListener("beforeunload", antesDeSair);
      document.removeEventListener("click", clique, true);
    };
  }, [pendente, navigate]);

  const dialogo = (
    <AlertDialog open={acao !== null} onOpenChange={(v) => { if (!v) setAcao(null); }}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Descartar alterações?</AlertDialogTitle>
          <AlertDialogDescription>
            Existem alterações não salvas neste perfil. Se continuar, elas serão perdidas.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Continuar editando</AlertDialogCancel>
          <AlertDialogAction
            className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            onClick={() => { const a = acao; setAcao(null); a?.(); }}
          >
            Descartar e sair
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );

  return { confirmarSe, dialogo };
}
