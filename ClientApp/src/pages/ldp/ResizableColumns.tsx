import { useCallback, useRef, useState } from "react";

// Redimensionamento de colunas de grade sem biblioteca nova (nenhuma lib de grid/dnd está instalada
// no projeto — ver AUDITORIA_FORGESYNC_NA/17-*.md §1). Implementado com mousedown/mousemove/mouseup
// puro. `useColumnWidths` guarda a largura de cada coluna por índice; `ResizeHandle` é a alça
// arrastável, encaixada na borda direita de cada <th>.

export function useColumnWidths(initial: number[]) {
  const [widths, setWidths] = useState<number[]>(initial);

  const setWidth = useCallback((index: number, width: number) => {
    setWidths((prev) => {
      const next = [...prev];
      next[index] = Math.max(60, width);
      return next;
    });
  }, []);

  return { widths, setWidth };
}

export const ResizeHandle = ({ index, currentWidth, onResize }: { index: number; currentWidth: number; onResize: (index: number, width: number) => void }) => {
  const startX = useRef(0);
  const startWidth = useRef(0);

  const handleMouseDown = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    startX.current = e.clientX;
    startWidth.current = currentWidth;

    const handleMouseMove = (moveEvent: MouseEvent) => {
      const delta = moveEvent.clientX - startX.current;
      onResize(index, startWidth.current + delta);
    };
    const handleMouseUp = () => {
      window.removeEventListener("mousemove", handleMouseMove);
      window.removeEventListener("mouseup", handleMouseUp);
    };
    window.addEventListener("mousemove", handleMouseMove);
    window.addEventListener("mouseup", handleMouseUp);
  };

  return (
    <div
      onMouseDown={handleMouseDown}
      className="absolute top-0 right-0 h-full w-1.5 cursor-col-resize hover:bg-primary/40 active:bg-primary/60 select-none"
      title="Arrastar para redimensionar"
    />
  );
};
