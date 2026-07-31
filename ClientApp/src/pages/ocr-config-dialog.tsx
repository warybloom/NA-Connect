// Arquivo: ClientApp/src/components/ocr-config-dialog.tsx

import { useState, useEffect } from "react"
import { useNavigate } from "react-router-dom"
import {createPortal} from "react-dom"
import { X, Settings2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import toast from "react-hot-toast"

interface Props {
  isOpen: boolean
  onClose: () => void
}

export const OCRConfigDialog = ({ isOpen, onClose }: Props) => {
  const navigate = useNavigate()
  const [pacotes, setPacotes] = useState<any[]>([])
  const [pacoteId, setPacoteId] = useState("")
  const [tipoFolha, setTipoFolha] = useState("")

  useEffect(() => {
    if (!isOpen) return
    fetch("/api/admin/ocr/pacotes")
      .then((res) => res.json())
      .then(setPacotes)
      .catch(() => toast.error("Erro ao carregar pacotes OCR"))
  }, [isOpen])

  const handleConfirm = () => {
    if (!pacoteId || !tipoFolha) {
      toast.error("Selecione o pacote e o tipo de folha.")
      return
    }
    navigate(`/folhas/${pacoteId}/${tipoFolha}`)
    onClose()
  }

  if (!isOpen) return null

  return  createPortal (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-background/80 backdrop-blur-sm animate-enter">
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-background/80 backdrop-blur-sm animate-enter">
      <div className="bg-card border border-border rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
        <div className="p-4 border-b border-border flex justify-between items-center bg-muted/30">
          <h2 className="text-lg font-black uppercase text-foreground flex items-center gap-2">
            <Settings2 className="text-primary" /> Configurar Visão OCR
          </h2>
          <button
            onClick={onClose}
            title="Fechar janela"
            aria-label="Fechar janela"
            className="text-muted-foreground hover:text-foreground transition-colors p-1"
          >
            <X size={20} />
          </button>
        </div>

        <div className="p-6 space-y-6">
          <div className="space-y-2">
            <label htmlFor="pacote-select" className="text-xs font-bold text-muted-foreground uppercase">
              Pacote OCR *
            </label>
            <select
              id="pacote-select"
              title="Selecione o pacote OCR"
              aria-label="Selecione o pacote OCR"
              value={pacoteId}
              onChange={(e) => setPacoteId(e.target.value)}
              className="w-full bg-background border border-border rounded-xl p-3 text-sm text-foreground outline-none focus:ring-2 ring-primary/50 transition-colors"
            >
              <option value="">Selecione o pacote...</option>
              {pacotes.map((p) => (
                <option key={p.id} value={p.id}>{p.nome}</option>
              ))}
            </select>
          </div>

          <div className="space-y-2">
            <label htmlFor="tipo-folha-select" className="text-xs font-bold text-muted-foreground uppercase">
              Tipo de Folha *
            </label>
            <select
              id="tipo-folha-select"
              title="Selecione o tipo de folha"
              aria-label="Selecione o tipo de folha"
              value={tipoFolha}
              onChange={(e) => setTipoFolha(e.target.value)}
              className="w-full bg-background border border-border rounded-xl p-3 text-sm text-foreground outline-none focus:ring-2 ring-primary/50 transition-colors"
            >
              <option value="">Selecione o tipo...</option>
              <option value="A0">Folha A0</option>
              <option value="A1">Folha A1</option>
              <option value="A2">Folha A2</option>
              <option value="A3">Folha A3</option>
              <option value="ANSI_A">Folha ANSI A</option>
            </select>
          </div>

          <div className="pt-4 flex gap-3">
            <Button
              onClick={onClose}
              aria-label="Cancelar"
              variant="outline"
              className="flex-1 border-border text-foreground hover:bg-muted font-bold uppercase text-[10px] transition-colors"
            >
              Cancelar
            </Button>
            <Button
              onClick={handleConfirm}
              aria-label="Abrir Calibrador"
              disabled={!pacoteId || !tipoFolha}
              className="flex-1 bg-primary hover:bg-primary/80 text-primary-foreground font-bold uppercase text-[10px] gap-2 transition-colors"
            >
              Abrir Calibrador
            </Button>
          </div>
        </div>
      </div>
    </div>
    </div>,
  document.body
  )
}