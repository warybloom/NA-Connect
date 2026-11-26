// Custos do Projeto → Impostos / Softwares: itens de costs.ProfessionalCostItem de UMA categoria (IMPOSTO | SOFTWARE),
// por profissional. Valor mensal monetário (sem cálculo percentual). Os itens ativos entram no CustoMensal/CustoHora
// calculados pelo backend — nenhum cálculo financeiro novo no frontend.
import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import toast from "react-hot-toast";
import { Loader2, Pencil, Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { CATEGORIA, custosApi, dinheiro, juntarSoftware, lerNumero, separarSoftware, type ItemCusto, type Profissional } from "./costsApi";
import {
  CabecalhoCustos, Campo, CampoTexto, Carregando, ConfirmarDesativacao, ErroCustos, SeloAtivo, SemProjeto, mensagemDe, tdC, tdCNum, thC, thCNum,
  useCarga, useProjetoCustos,
} from "./CustosShared";
import { ROTAS_CUSTOS } from "./custosRoutes";

interface Config {
  categoria: string;
  titulo: string;
  descricao: string;
  /** Software: nome + licença (gravados juntos em Descricao). Imposto: só descrição. */
  software: boolean;
  rotuloItem: string;
  exemplo: string;
}

const IMPOSTOS: Config = {
  categoria: CATEGORIA.imposto, titulo: "Impostos", software: false, rotuloItem: "Imposto / encargo", exemplo: "Ex.: INSS patronal",
  descricao: "Impostos e encargos mensais (valor em R$) por profissional. Entram no custo mensal e no custo/hora.",
};
const SOFTWARES: Config = {
  categoria: CATEGORIA.software, titulo: "Softwares", software: true, rotuloItem: "Software", exemplo: "Ex.: AutoCAD",
  descricao: "Licenças de software por profissional (valor mensal em R$). Entram no custo mensal e no custo/hora.",
};

export const CustosImpostosPage = () => <ItensCategoriaPage config={IMPOSTOS} />;
export const CustosSoftwaresPage = () => <ItensCategoriaPage config={SOFTWARES} />;

function ItensCategoriaPage({ config }: { config: Config }) {
  const { projectId } = useProjetoCustos();
  const profissionais = useCarga<Profissional[]>(projectId ? () => custosApi.profissionais(projectId) : null, [projectId]);
  const ativos = useMemo(() => (profissionais.dados ?? []).filter((p) => p.ativo), [profissionais.dados]);
  const [profId, setProfId] = useState<number | null>(null);
  useEffect(() => { if (profId === null && ativos.length > 0) setProfId(ativos[0].id); }, [ativos, profId]);
  const prof = ativos.find((p) => p.id === profId);

  if (!projectId) return <SemProjeto />;
  return (
    <div className="space-y-4">
      <CabecalhoCustos titulo={config.titulo} descricao={config.descricao} />
      {profissionais.erro ? <ErroCustos erro={profissionais.erro} /> : !profissionais.dados ? <Carregando texto="Carregando profissionais…" /> : ativos.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border p-10 text-center">
          <p className="font-semibold">Nenhum profissional ativo</p>
          <p className="text-sm text-muted-foreground">Cadastre profissionais em <Link className="text-primary hover:underline" to={ROTAS_CUSTOS.profissionais}>Profissionais</Link> antes de lançar {config.titulo.toLowerCase()}.</p>
        </div>
      ) : (
        <>
          <label className="flex max-w-md flex-col gap-1.5">
            <span className="text-xs font-semibold">Profissional</span>
            <select value={profId ?? ""} onChange={(e) => setProfId(Number(e.target.value))} className="h-10 rounded-md border border-border bg-background px-3 text-sm">
              {ativos.map((p) => <option key={p.id} value={p.id}>{p.nome}{p.empresa ? ` — ${p.empresa}` : ""}</option>)}
            </select>
          </label>
          {prof && <ItensDoProfissional key={`${prof.id}-${config.categoria}`} projectId={projectId} prof={prof} config={config} onAlterado={profissionais.recarregar} />}
        </>
      )}
    </div>
  );
}

function ItensDoProfissional({ projectId, prof, config, onAlterado }: { projectId: string; prof: Profissional; config: Config; onAlterado: () => void }) {
  const itens = useCarga<ItemCusto[]>(() => custosApi.itens(projectId, prof.id), [projectId, prof.id]);
  const daCategoria = (itens.dados ?? []).filter((i) => i.categoria === config.categoria);
  const total = daCategoria.filter((i) => i.ativo).reduce((s, i) => s + i.valor, 0);
  const [novo, setNovo] = useState({ nome: "", licenca: "", valor: "" });
  const [salvando, setSalvando] = useState(false);
  const [editando, setEditando] = useState<ItemCusto | null>(null);
  const [desativar, setDesativar] = useState<ItemCusto | null>(null);
  const [ocupado, setOcupado] = useState(false);

  const depois = () => { itens.recarregar(); onAlterado(); };
  const adicionar = async (e: FormEvent) => {
    e.preventDefault();
    const valor = lerNumero(novo.valor);
    if (!novo.nome.trim()) { toast.error(`Informe o ${config.software ? "software" : "imposto/encargo"}.`); return; }
    if (valor === null || valor < 0) { toast.error("Informe o valor mensal (não negativo)."); return; }
    setSalvando(true);
    try {
      const descricao = config.software ? juntarSoftware(novo.nome, novo.licenca) : novo.nome.trim();
      await custosApi.criarItem(projectId, prof.id, { categoria: config.categoria, descricao, valor });
      setNovo({ nome: "", licenca: "", valor: "" }); depois(); toast.success(`${config.rotuloItem} cadastrado.`);
    } catch (x) { toast.error(mensagemDe(x)); } finally { setSalvando(false); }
  };
  const confirmar = async () => {
    if (!desativar) return;
    setOcupado(true);
    try { await custosApi.desativar(projectId, "item", desativar.id); setDesativar(null); depois(); toast.success("Desativado."); }
    catch (x) { toast.error(mensagemDe(x)); } finally { setOcupado(false); }
  };

  return (
    <section className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-3">
        <Resumo rotulo={`Total de ${config.titulo.toLowerCase()} (mês)`} valor={dinheiro(total)} destaque />
        <Resumo rotulo="Custo mensal do profissional" valor={dinheiro(prof.custoMensal)} />
        <Resumo rotulo="Custo/hora do profissional" valor={dinheiro(prof.custoHora)} />
      </div>
      <div className="overflow-hidden rounded-xl border border-border bg-card/60">
        {itens.erro ? <div className="p-4"><ErroCustos erro={itens.erro} /></div> : !itens.dados ? <div className="px-4"><Carregando texto="Carregando…" /></div> : (
          <table className="w-full text-sm">
            <thead><tr className="border-b border-border">
              <th className={thC}>{config.software ? "Software" : "Imposto / encargo"}</th>
              {config.software && <th className={thC}>Licença / descrição</th>}
              <th className={thCNum}>Valor mensal</th><th className={thC}>Situação</th><th className={thC} />
            </tr></thead>
            <tbody>
              {daCategoria.length === 0 && <tr><td colSpan={config.software ? 5 : 4} className="px-3 py-6 text-center text-muted-foreground">Nenhum registro para {prof.nome}.</td></tr>}
              {daCategoria.map((i) => {
                const sw = separarSoftware(i.descricao);
                return (
                  <tr key={i.id} className={cn("border-b border-border/50", !i.ativo && "text-muted-foreground")}>
                    <td className={`${tdC} font-medium`}>{config.software ? sw.nome : i.descricao}</td>
                    {config.software && <td className={tdC}>{sw.licenca || "—"}</td>}
                    <td className={tdCNum}>{dinheiro(i.valor)}</td>
                    <td className={tdC}><SeloAtivo ativo={i.ativo} /></td>
                    <td className={`${tdC} whitespace-nowrap text-right`}>
                      {!config.software && <>
                        <Button variant="ghost" size="sm" className="h-7 gap-1" onClick={() => setEditando(i)}><Pencil className="h-3.5 w-3.5" />Editar</Button>
                        {i.ativo && <Button variant="ghost" size="sm" className="h-7 text-rose-500" onClick={() => setDesativar(i)}>Desativar</Button>}
                      </>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
        {!config.software && <form onSubmit={adicionar} className={cn("grid gap-2 border-t border-border bg-background/40 p-3", config.software ? "sm:grid-cols-[1fr_1fr_160px_auto]" : "sm:grid-cols-[1fr_160px_auto]")}>
          <CampoTexto aria-label={config.rotuloItem} placeholder={config.exemplo} value={novo.nome} onChange={(e) => setNovo({ ...novo, nome: e.target.value })} />
          {config.software && <CampoTexto aria-label="Licença ou descrição" placeholder="Licença / descrição (opcional)" value={novo.licenca} onChange={(e) => setNovo({ ...novo, licenca: e.target.value })} />}
          <CampoTexto aria-label="Valor mensal" inputMode="decimal" placeholder="Valor mensal (R$)" value={novo.valor} onChange={(e) => setNovo({ ...novo, valor: e.target.value })} />
          <Button type="submit" disabled={salvando} className="h-10 gap-1.5">{salvando ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}Adicionar</Button>
        </form>}
      </div>
      {config.software && <p className="text-sm text-muted-foreground">Softwares e equipamentos são itens vinculados a uma vigência. Para incluí-los sem alterar o histórico, abra o profissional em <Link className="text-primary hover:underline" to={ROTAS_CUSTOS.profissionais}>Profissionais</Link> e registre uma nova vigência.</p>}
      {editando && <EditarItem projectId={projectId} prof={prof} item={editando} config={config} onFechar={() => setEditando(null)} onSalvo={() => { setEditando(null); depois(); }} />}
      <ConfirmarDesativacao aberto={desativar !== null} ocupado={ocupado} onCancelar={() => setDesativar(null)} onConfirmar={confirmar}
        titulo={`Desativar ${config.rotuloItem.toLowerCase()}?`} descricao="O valor deixa de compor o custo mensal deste profissional. O histórico é mantido." />
    </section>
  );
}

function EditarItem({ projectId, prof, item, config, onFechar, onSalvo }: {
  projectId: string; prof: Profissional; item: ItemCusto; config: Config; onFechar: () => void; onSalvo: () => void;
}) {
  const sw = separarSoftware(item.descricao);
  const [nome, setNome] = useState(config.software ? sw.nome : item.descricao);
  const [licenca, setLicenca] = useState(sw.licenca);
  const [valor, setValor] = useState(String(item.valor).replace(".", ","));
  const [ativo, setAtivo] = useState(item.ativo);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const salvar = async (e: FormEvent) => {
    e.preventDefault();
    const v = lerNumero(valor);
    if (!nome.trim() || v === null || v < 0) { setErro("Informe a descrição e um valor mensal não negativo."); return; }
    setSalvando(true); setErro(null);
    try {
      await custosApi.atualizarItem(projectId, prof.id, item.id, { descricao: config.software ? juntarSoftware(nome, licenca) : nome.trim(), valor: v, ativo });
      toast.success("Atualizado."); onSalvo();
    } catch (x) { setErro(mensagemDe(x)); } finally { setSalvando(false); }
  };

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !salvando) onFechar(); }}>
      <DialogContent className="max-w-md">
        <form onSubmit={salvar} className="space-y-4">
          <DialogHeader>
            <DialogTitle>Editar {config.rotuloItem.toLowerCase()}</DialogTitle>
            <DialogDescription>{prof.nome}</DialogDescription>
          </DialogHeader>
          <Campo rotulo={config.rotuloItem}><CampoTexto value={nome} onChange={(e) => setNome(e.target.value)} autoFocus /></Campo>
          {config.software && <Campo rotulo="Licença / descrição"><CampoTexto value={licenca} onChange={(e) => setLicenca(e.target.value)} /></Campo>}
          <Campo rotulo="Valor mensal (R$)"><CampoTexto inputMode="decimal" value={valor} onChange={(e) => setValor(e.target.value)} /></Campo>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={ativo} onChange={(e) => setAtivo(e.target.checked)} />Ativo</label>
          {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{erro}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onFechar} disabled={salvando}>Cancelar</Button>
            <Button type="submit" disabled={salvando} className="gap-1.5">{salvando && <Loader2 className="h-4 w-4 animate-spin" />}Salvar</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

const Resumo = ({ rotulo, valor, destaque }: { rotulo: string; valor: string; destaque?: boolean }) => (
  <div className={cn("rounded-xl border bg-card/60 p-4", destaque ? "border-primary/40" : "border-border")}>
    <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{rotulo}</p>
    <p className={cn("text-xl font-semibold tabular-nums", destaque && "text-primary")}>{valor}</p>
  </div>
);
