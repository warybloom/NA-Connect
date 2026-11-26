import {useState} from "react";
import {Link} from "react-router-dom";
import {Check, ClipboardCopy, ExternalLink} from "lucide-react";
import {Dialog,DialogContent,DialogDescription,DialogHeader,DialogTitle} from "@/components/ui/dialog";
import {Button} from "@/components/ui/button";
import {formatHH} from "@/components/pmo/pmoData";
import type {SiglaPendenteResumo} from "./resumoPhaseData";

type Props={
  open:boolean;onOpenChange:(open:boolean)=>void;nomeProjeto:string;
  siglas:SiglaPendenteResumo[];diagnostico:string|null;
};
export default function ResumoSiglasPendentesDialog({open,onOpenChange,nomeProjeto,siglas,diagnostico}:Props){
  const [copiado,setCopiado]=useState(false);
  const [erro,setErro]=useState("");
  const codigos = siglas.reduce((total,item)=>total+item.documentos.length,0);
  async function copiar(){
    try{
      await navigator.clipboard.writeText(siglas.map(s=>s.sigla).join("\n"));
      setCopiado(true);setErro("");
    }catch{
      setErro("Cópia indisponível neste navegador. Selecione as siglas na lista.");
    }
  }
  const fmt=(value:number|null)=>value===null?"—":formatHH(value)+" HH";
  return <Dialog open={open} onOpenChange={next=>{onOpenChange(next);if(!next){setCopiado(false);setErro("");}}}>
    <DialogContent className="max-w-4xl" data-testid="siglas-sem-mapeamento-dialog">
      <DialogHeader>
        <DialogTitle>Conferir siglas · Sem mapeamento</DialogTitle>
        <DialogDescription>{nomeProjeto} · {siglas.length} sigla(s) · {codigos} código(s) documental(is)</DialogDescription>
      </DialogHeader>
      <p className="text-sm text-muted-foreground">
        Siglas Doc3 encontradas nos documentos deste card. Verifique as regras do Normalizador de Siglas ou a Norma de Coordenação do projeto.
        Nenhum cadastro ou lançamento é alterado aqui.
      </p>
      {diagnostico&&<p className="rounded-md border border-amber-500/30 p-2 text-xs text-amber-700 dark:text-amber-300">{diagnostico}</p>}
      <div className="flex flex-wrap gap-2">
        <Button variant="outline" size="sm" onClick={()=>void copiar()} disabled={siglas.length===0}>
          {copiado?<Check className="mr-2 h-4 w-4"/>:<ClipboardCopy className="mr-2 h-4 w-4"/>}
          {copiado?"Siglas copiadas":"Copiar siglas"}
        </Button>
        <Button variant="outline" size="sm" asChild>
          <Link to="/configuracoes/normalizador-siglas" onClick={()=>onOpenChange(false)}>
            <ExternalLink className="mr-2 h-4 w-4"/>Abrir Normalizador
          </Link>
        </Button>
      </div>
      {erro&&<p className="text-xs text-destructive" role="alert">{erro}</p>}
      {siglas.length===0?<p className="rounded-lg border border-border p-4 text-sm text-muted-foreground">
        Este agrupamento não possui documentos com código associado na resposta atual. Veja o diagnóstico do card e confira o vínculo do projeto.
      </p>:
        <div className="max-h-[52dvh] space-y-2 overflow-y-auto pr-1">
          {siglas.map(item=>{
            const jaIdentificados=item.documentos.filter(d=>d.disciplinaIdentificada!==null).length;
            return <details key={item.sigla} className="rounded-lg border border-border/70 bg-card/60 p-3" open={siglas.length===1?true:undefined}>
              <summary className="cursor-pointer text-sm font-bold">
                <code className="mr-2 rounded bg-muted px-2 py-1 text-primary">{item.sigla}</code>
                {item.documentos.length} documento(s)
                {jaIdentificados>0&&<span className="ml-2 text-xs font-normal text-amber-600 dark:text-amber-400">
                  ({jaIdentificados} já têm disciplina identificada; conferir agrupamento)
                </span>}
              </summary>
              <div className="mt-3 overflow-x-auto">
                <table className="w-full min-w-[560px] text-left text-xs">
                  <thead><tr className="border-b border-border"><th className="p-2">Código do documento</th><th className="p-2 text-right">HH realizadas</th><th className="p-2">Diagnóstico</th></tr></thead>
                  <tbody>{item.documentos.map(d=><tr key={d.codigo} className="border-b border-border/40">
                    <td className="p-2 font-mono break-all">{d.codigo}</td>
                    <td className="p-2 text-right whitespace-nowrap">{fmt(d.hhRealizado)}</td>
                    <td className="p-2">{d.disciplinaIdentificada
                      ? "Disciplina identificada: "+d.disciplinaIdentificada
                      : d.diagnostico??"Sem correspondência comprovada no projeto"}</td>
                  </tr>)}</tbody>
                </table>
              </div>
            </details>;
          })}
        </div>}
    </DialogContent>
  </Dialog>;
}
