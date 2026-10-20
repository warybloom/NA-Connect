// Utilitário compartilhado pelas 3 páginas de Configuração da LDP para ler um Excel client-side
// (preview visual, nunca envia o arquivo a um endpoint). Mesma biblioteca já usada em ControlPanel.tsx.
import * as XLSX from "xlsx";

export interface WorkbookPreview {
  sheetNames: string[];
  getRows: (sheetName: string) => string[][];
}

export function parseWorkbookFile(file: File): Promise<WorkbookPreview> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = (evt) => {
      try {
        const data = new Uint8Array(evt.target?.result as ArrayBuffer);
        const workbook = XLSX.read(data, { type: "array" });
        resolve({
          sheetNames: workbook.SheetNames,
          getRows: (sheetName: string) =>
            XLSX.utils.sheet_to_json<string[]>(workbook.Sheets[sheetName], {
              header: 1,
              defval: "",
              blankrows: false,
            }) as unknown as string[][],
        });
      } catch (err) {
        reject(err);
      }
    };
    reader.onerror = () => reject(reader.error ?? new Error("Falha ao ler o arquivo."));
    reader.readAsArrayBuffer(file);
  });
}
