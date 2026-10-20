import { useState } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Upload, CheckSquare, Square } from "lucide-react";
import toast from "react-hot-toast";
import { parseWorkbookFile, WorkbookPreview } from "./excelPreview";
import { ExcelPreviewTable } from "./ExcelPreviewTable";

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  /** true = mostra checkbox por coluna e exige seleção; false = só detecta os cabeçalhos (todos). */
  permitirSelecaoDeColunas?: boolean;
  onConfirm: (headers: string[], fileName: string) => void;
}

// Passo 1: selecionar Excel. Passo 2: aba + linha de cabeçalho (lado a lado, evidentes) → preview
// visual grande → colunas detectadas com seleção em massa. Reutilizado por: import de estrutura
// (Template), upload de Cronograma (Mapeamento), upload de XLSX base (Geração LDP Espelho). Nunca
// envia o arquivo para nenhum endpoint — leitura 100% client-side (FileReader + xlsx.js).
export const ExcelImportWizard = ({ open, onOpenChange, title, description, permitirSelecaoDeColunas = true, onConfirm }: Props) => {
  const [fileName, setFileName] = useState<string | null>(null);
  const [workbook, setWorkbook] = useState<WorkbookPreview | null>(null);
  const [sheetName, setSheetName] = useState<string>("");
  const [headerRow, setHeaderRow] = useState<number>(1);
  const [selectedCols, setSelectedCols] = useState<Set<number>>(new Set());

  const reset = () => {
    setFileName(null);
    setWorkbook(null);
    setSheetName("");
    setHeaderRow(1);
    setSelectedCols(new Set());
  };

  const handleFile = async (file: File) => {
    try {
      const parsed = await parseWorkbookFile(file);
      setWorkbook(parsed);
      setFileName(file.name);
      setSheetName(parsed.sheetNames[0] ?? "");
      setHeaderRow(1);
      const primeiraLinha = parsed.getRows(parsed.sheetNames[0] ?? "")[0] ?? [];
      setSelectedCols(new Set(primeiraLinha.map((_, i) => i)));
    } catch (err) {
      toast.error("Não foi possível ler este arquivo. Confirme que é um .xlsx/.xls válido.");
    }
  };

  const rows = workbook && sheetName ? workbook.getRows(sheetName) : [];
  const headerIdx = Math.max(0, headerRow - 1);
  const headersDaLinha = (rows[headerIdx] ?? []).map((h, i) => String(h ?? "").trim() || `Coluna ${i + 1}`);

  const toggleCol = (i: number) => {
    setSelectedCols((prev) => {
      const next = new Set(prev);
      if (next.has(i)) next.delete(i);
      else next.add(i);
      return next;
    });
  };

  const selecionarTodas = () => setSelectedCols(new Set(headersDaLinha.map((_, i) => i)));
  const limparSelecao = () => setSelectedCols(new Set());

  const handleConfirmar = () => {
    if (!fileName || headersDaLinha.length === 0) {
      toast.error("Selecione um arquivo e uma linha de cabeçalho válida.");
      return;
    }
    const headers = permitirSelecaoDeColunas
      ? headersDaLinha.filter((_, i) => selectedCols.has(i))
      : headersDaLinha;
    if (headers.length === 0) {
      toast.error("Selecione ao menos uma coluna.");
      return;
    }
    onConfirm(headers, fileName);
    reset();
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) reset(); onOpenChange(o); }}>
      <DialogContent className="max-w-[90vw] w-[90vw] max-h-[90vh] h-[90vh] flex flex-col overflow-hidden">
        <DialogHeader className="shrink-0">
          <DialogTitle className="uppercase italic font-black tracking-tighter">{title}</DialogTitle>
          <p className="text-xs text-muted-foreground">{description}</p>
        </DialogHeader>

        {!workbook ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="glass-card p-16 border-2 border-dashed border-primary/30 text-center relative hover:bg-primary/5 transition-colors cursor-pointer rounded-xl w-full max-w-xl">
              <input
                type="file"
                accept=".xlsx,.xls"
                onChange={(e) => { const f = e.target.files?.[0]; if (f) handleFile(f); }}
                className="absolute inset-0 opacity-0 cursor-pointer"
                title="Selecionar arquivo Excel"
              />
              <div className="flex flex-col items-center gap-2 pointer-events-none">
                <Upload className="w-10 h-10 text-primary" />
                <span className="font-black text-xs uppercase italic text-primary">Selecionar arquivo .xlsx</span>
              </div>
            </div>
          </div>
        ) : (
          <div className="flex-1 flex flex-col gap-4 min-h-0">
            {/* Fluxo visual: Arquivo → Aba → Linha do cabeçalho → Preview → Colunas detectadas */}
            <div className="flex items-end gap-4 shrink-0">
              <div className="flex-1 max-w-xs">
                <label className="text-[10px] font-black uppercase text-muted-foreground">1. Aba</label>
                <Select value={sheetName} onValueChange={setSheetName}>
                  <SelectTrigger className="bg-card/30 border-border/50 text-xs h-10">
                    <SelectValue placeholder="Aba" />
                  </SelectTrigger>
                  <SelectContent>
                    {workbook.sheetNames.map((s) => (
                      <SelectItem key={s} value={s} className="text-xs">{s}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="w-56">
                <label className="text-[10px] font-black uppercase text-primary">2. Linha do cabeçalho</label>
                <Input
                  type="number"
                  min={1}
                  value={headerRow}
                  onChange={(e) => setHeaderRow(Math.max(1, Number(e.target.value) || 1))}
                  className="bg-primary/5 border-primary/40 h-10 text-sm font-bold focus-visible:ring-primary"
                />
              </div>
              <p className="text-[10px] text-muted-foreground uppercase font-bold pb-2">
                Arquivo: {fileName}
              </p>
              <Button variant="outline" onClick={reset} className="text-[10px] font-black uppercase ml-auto shrink-0">
                Trocar arquivo
              </Button>
            </div>

            <div className="flex items-center justify-between shrink-0">
              <label className="text-[10px] font-black uppercase text-muted-foreground">3. Preview / 4. Colunas detectadas</label>
              {permitirSelecaoDeColunas && (
                <div className="flex gap-2">
                  <Button variant="outline" size="sm" onClick={selecionarTodas} className="h-7 text-[9px] font-black uppercase gap-1.5">
                    <CheckSquare className="w-3 h-3" /> Selecionar todas
                  </Button>
                  <Button variant="outline" size="sm" onClick={limparSelecao} className="h-7 text-[9px] font-black uppercase gap-1.5">
                    <Square className="w-3 h-3" /> Limpar seleção
                  </Button>
                </div>
              )}
            </div>

            <div className="flex-1 min-h-0 overflow-auto">
              <ExcelPreviewTable
                rows={rows}
                headerRowIndex={headerIdx}
                maxPreviewRows={30}
                selectedColumns={permitirSelecaoDeColunas ? selectedCols : undefined}
                onToggleColumn={permitirSelecaoDeColunas ? toggleCol : undefined}
              />
            </div>

            {permitirSelecaoDeColunas && (
              <p className="text-[10px] text-muted-foreground shrink-0">
                {selectedCols.size} de {headersDaLinha.length} coluna(s) selecionada(s).
              </p>
            )}
          </div>
        )}

        <DialogFooter className="shrink-0">
          <Button
            onClick={handleConfirmar}
            disabled={!workbook}
            className="text-[10px] font-black uppercase italic tracking-widest bg-primary hover:bg-primary/90"
          >
            Confirmar
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
