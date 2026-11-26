// Configurações → Configuração de Templates → <tipo>. UM editor parametrizado pelo tipo (templateTypes.ts):
// tipos já suportados pelo motor existente (components/AdminTemplates.tsx) abrem o motor fixo no módulo; os demais
// mostram o estado real (tipo ainda sem campos definidos no motor) — sem inventar funcionalidade.
import { useParams } from "react-router-dom";
import { FileSpreadsheet } from "lucide-react";
import AdminTemplates from "@/components/AdminTemplates";
import { encontrarTemplate } from "./templateTypes";

export default function TemplateConfigPage() {
  const { tipo } = useParams<{ tipo: string }>();
  const t = encontrarTemplate(tipo);
  if (!t) {
    return <p className="text-sm text-rose-400">Tipo de template desconhecido: “{tipo}”.</p>;
  }
  return (
    <div className="flex h-full flex-col gap-4 animate-enter">
      <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-widest text-muted-foreground">
        <span>Configuração de Templates</span><span>›</span><span className="text-foreground">{t.label}</span>
      </div>
      {t.moduloMotor ? (
        <AdminTemplates key={t.tipo} moduloFixo={t.moduloMotor} titulo={`Template Excel — ${t.label}`} />
      ) : (
        <div className="flex min-h-[40vh] items-center justify-center">
          <div className="w-full max-w-lg rounded-xl border border-border bg-card/60 p-8 text-center">
            <FileSpreadsheet className="mx-auto mb-3 h-8 w-8 text-amber-500" />
            <h2 className="text-lg font-bold">Template Excel — {t.label}</h2>
            <span className="mt-2 inline-block rounded-full border border-amber-500/40 bg-amber-500/10 px-3 py-1 text-[10px] font-black uppercase tracking-widest text-amber-500">
              Em desenvolvimento
            </span>
            <p className="mt-3 text-sm text-muted-foreground">
              O motor de templates ainda não tem os campos deste tipo. Quando forem definidos, este tipo usará o mesmo
              editor (upload do Excel padrão + mapeamento das tags) dos demais.
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
