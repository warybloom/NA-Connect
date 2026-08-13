// hooks/useGlobalFileDrop.ts
import { useEffect, useState, useCallback } from "react";

export function useGlobalFileDrop(onFileDropped: (file: File) => void, enabled = true) {
  const [isDraggingOverPage, setIsDraggingOverPage] = useState(false);

  useEffect(() => {
    if (!enabled) return;

    let dragCounter = 0;

    const handleDragEnter = (e: DragEvent) => {
      e.preventDefault();
      // só reage se tiver arquivo sendo arrastado (não texto/link)
      if (e.dataTransfer?.types.includes("Files")) {
        dragCounter++;
        setIsDraggingOverPage(true);
      }
    };

    const handleDragOver = (e: DragEvent) => {
      e.preventDefault(); // necessário pra permitir o drop
    };

    const handleDragLeave = (e: DragEvent) => {
      e.preventDefault();
      dragCounter--;
      if (dragCounter <= 0) {
        dragCounter = 0;
        setIsDraggingOverPage(false);
      }
    };

    const handleDrop = (e: DragEvent) => {
      e.preventDefault();
      dragCounter = 0;
      setIsDraggingOverPage(false);

      const droppedFile = e.dataTransfer?.files?.[0];
      if (droppedFile) {
        onFileDropped(droppedFile);
      }
    };

    window.addEventListener("dragenter", handleDragEnter);
    window.addEventListener("dragover", handleDragOver);
    window.addEventListener("dragleave", handleDragLeave);
    window.addEventListener("drop", handleDrop);

    return () => {
      window.removeEventListener("dragenter", handleDragEnter);
      window.removeEventListener("dragover", handleDragOver);
      window.removeEventListener("dragleave", handleDragLeave);
      window.removeEventListener("drop", handleDrop);
    };
  }, [enabled, onFileDropped]);

  return isDraggingOverPage;
}