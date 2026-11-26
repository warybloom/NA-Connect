import { useEffect, useMemo, useState, type FormEvent } from "react";
import toast from "react-hot-toast";
import { Download, FileSpreadsheet, Loader2, Pencil, Plus, Search, Trash2, Upload, Users } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import {
  custosApi, dataFmt, dinheiro, lerNumero, type PreviaAtribuicaoProfissionais, type PreviaImportacaoProfissionais,
  type Profissional, type ProjetoElegivelCusto,
} from "./costsApi";
import {
  CabecalhoCustos, Campo, CampoTexto, Carregando, ConfirmarDesativacao, ErroCustos, SeloAtivo, SelosProjeto, SemProjeto,
  mensagemDe, tdC, tdCNum, thC, thCNum, useCarga, useProjetoCustos,
} from "./CustosShared";
import CustosVigenciasProfissional from "./CustosVigenciasProfissional";
import ProjectCostCopyPanel from "./ProjectCostCopyPanel";

type FormProf = {
  funcao: string;
  disciplina: string;
  nome: string;
  empresa: string;
  aliasTimesheet: string;
  hhBaseMensal: string;
  ativo: boolean;
};

const VAZIO: FormProf = {
  funcao: "", disciplina: "", nome: "", empresa: "", aliasTimesheet: "", hhBaseMensal: "160", ativo: true,
};

export default function CustosProfissionaisPage() {
  const { projectId } = useProjetoCustos();
  const lista = useCarga<Profissional[]>(projectId ? () => custosApi.profissionais(projectId) : null, [projectId]);
  const [busca, setBusca] = useState("");
  const [mostrarInativos, setMostrarInativos] = useState(false);
  const [selecionadoId, setSelecionadoId] = useState<number | null>(null);
  const [selecionados, setSelecionados] = useState<Set<number>>(new Set());
  const [form, setForm] = useState<{ modo: "novo" | "editar"; valores: FormProf; id?: number } | null>(null);
  const [importar, setImportar] = useState(false);
  const [atribuir, setAtribuir] = useState(false);
  const [excluir, setExcluir] = useState<Profissional | null>(null);
  const [ocupado, setOcupado] = useState(false);

  // Trocar de projeto na Home não pode levar seleção/detalhe de outro projeto para a atribuição.
  useEffect(() => {
    setSelecionados(new Set());
    setSelecionadoId(null);
    setForm(null);
    setImportar(false);
    setAtribuir(false);
    setExcluir(null);
  }, [projectId]);

  const filtrados = useMemo(() => {
    const t = busca.trim().toLocaleLowerCase("pt-BR");
    return (lista.dados ?? []).filter((p) => {
      if (!mostrarInativos && !p.ativo) return false;
      if (!t) return true;
      return [p.funcao, p.disciplina, p.nome, p.empresa, p.aliasTimesheet]
        .some((x) => x?.toLocaleLowerCase("pt-BR").includes(t));
    });
  }, [lista.dados, busca, mostrarInativos]);

  const selecionado = lista.dados?.find((p) => p.id === selecionadoId) ?? null;
  const selecionadosValidos = (lista.dados ?? []).filter((p) => selecionados.has(p.id) && p.ativo);
  const todosVisiveis = filtrados.filter((p) => p.ativo).length > 0 &&
    filtrados.filter((p) => p.ativo).every((p) => selecionados.has(p.id));

  const toggleVisiveis = (checked: boolean) => {
    setSelecionados((atual) => {
      const next = new Set(atual);
      for (const p of filtrados) {
        if (!p.ativo) continue;
        if (checked) next.add(p.id); else next.delete(p.id);
      }
      return next;
    });
  };

  const abrirEdicao = (p: Profissional) => setForm({
    modo: "editar",
    id: p.id,
    valores: {
      funcao: p.funcao ?? "",
      disciplina: p.disciplina ?? "",
      nome: p.nome,
      empresa: p.empresa ?? "",
      aliasTimesheet: p.aliasTimesheet ?? "",
      hhBaseMensal: String(p.hhBaseMensal).replace(".", ","),
      ativo: p.ativo,
    },
  });

  const confirmarExclusao = async () => {
    if (!projectId || !excluir) return;
    setOcupado(true);
    try {
      await custosApi.desativar(projectId, "professional", excluir.id);
      toast.success("Profissional removido do projeto. O histórico de custos foi preservado.");
      setSelecionados((atual) => {
        const next = new Set(atual);
        next.delete(excluir.id);
        return next;
      });
      if (selecionadoId === excluir.id) setSelecionadoId(null);
      setExcluir(null);
      lista.recarregar();
    } catch (e) {
      toast.error(mensagemDe(e));
    } finally {
      setOcupado(false);
    }
  };

  if (!projectId) return <SemProjeto />;

  return (
    <div className="space-y-4">
      <CabecalhoCustos
        titulo="Profissionais"
        descricao="Cadastro, composição do custo e histórico de vigências dos profissionais do projeto."
        acoes={
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" className="gap-1.5" onClick={() => setImportar(true)}>
              <FileSpreadsheet className="h-4 w-4" />Importar Excel
            </Button>
            <Button
              variant="outline"
              className="gap-1.5"
              disabled={selecionadosValidos.length === 0}
              onClick={() => setAtribuir(true)}
            >
              <Users className="h-4 w-4" />Atribuir a projetos ({selecionadosValidos.length})
            </Button>
            <Button className="gap-1.5" onClick={() => setForm({ modo: "novo", valores: VAZIO })}>
              <Plus className="h-4 w-4" />Novo profissional
            </Button>
          </div>
        }
      />

      <section className="overflow-hidden rounded-xl border border-border bg-card/60">
        <div className="flex flex-wrap items-center gap-3 border-b border-border p-3">
          <div className="relative min-w-[280px] flex-1">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <CampoTexto
              className="pl-8"
              placeholder="Buscar função, disciplina, nome, empresa ou alias…"
              value={busca}
              onChange={(e) => setBusca(e.target.value)}
            />
          </div>
          <label className="flex items-center gap-2 text-xs text-muted-foreground">
            <input type="checkbox" checked={mostrarInativos} onChange={(e) => setMostrarInativos(e.target.checked)} />
            Mostrar excluídos/inativos
          </label>
          <span className="text-xs text-muted-foreground">{selecionadosValidos.length} selecionado(s)</span>
        </div>

        {lista.erro ? (
          <div className="p-4"><ErroCustos erro={lista.erro} /></div>
        ) : !lista.dados ? (
          <div className="p-4"><Carregando texto="Carregando profissionais…" /></div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[1420px] text-sm">
              <thead>
                <tr className="border-b border-border">
                  <th className="w-10 px-3 py-2 text-center">
                    <input type="checkbox" checked={todosVisiveis} onChange={(e) => toggleVisiveis(e.target.checked)} aria-label="Selecionar todos os profissionais ativos visíveis" />
                  </th>
                  <th scope="col" className={thC}>Função</th>
                  <th scope="col" className={thC}>Disciplina</th>
                  <th scope="col" className={thC}>Nome</th>
                  <th scope="col" className={thCNum}>Remuneração</th>
                  <th scope="col" className={thCNum}>Encargos</th>
                  <th scope="col" className={thCNum}>Softwares</th>
                  <th scope="col" className={thCNum}>Computadores</th>
                  <th scope="col" className={thCNum}>Total</th>
                  <th scope="col" className={thCNum}>Custo/hora</th>
                  <th scope="col" className={thC}>Situação</th>
                  <th scope="col" className={thC}>Ações</th>
                </tr>
              </thead>
              <tbody>
                {filtrados.length === 0 && (
                  <tr><td colSpan={12} className="px-3 py-10 text-center text-muted-foreground">Nenhum profissional para os filtros atuais.</td></tr>
                )}
                {filtrados.map((p) => (
                  <tr
                    key={p.id}
                    className={cn(
                      "border-b border-border/50 hover:bg-accent/20",
                      selecionadoId === p.id && "bg-primary/5",
                      !p.ativo && "opacity-60",
                    )}
                  >
                    <td className="px-3 py-3 text-center">
                      <input
                        type="checkbox"
                        disabled={!p.ativo}
                        checked={selecionados.has(p.id)}
                        onChange={(e) => setSelecionados((atual) => {
                          const next = new Set(atual);
                          if (e.target.checked) next.add(p.id); else next.delete(p.id);
                          return next;
                        })}
                        aria-label={`Selecionar ${p.nome}`}
                      />
                    </td>
                    <td className={tdC}>{p.funcao ?? "—"}</td>
                    <td className={tdC}>{p.disciplina ?? "—"}</td>
                    <td className={tdC}>
                      <button type="button" className="font-semibold hover:text-primary hover:underline" onClick={() => setSelecionadoId(p.id)}
                        aria-pressed={selecionadoId === p.id} title="Ver detalhe e histórico salarial">
                        {p.nome}
                      </button>
                    </td>
                    <td className={tdCNum}>{dinheiro(p.remuneracao)}</td>
                    <td className={tdCNum}>{dinheiro(p.encargos)}</td>
                    <td className={tdCNum}>{dinheiro(p.softwares)}</td>
                    <td className={tdCNum}>{dinheiro(p.computadores)}</td>
                    <td className={`${tdCNum} font-bold`}>{dinheiro(p.custoMensal)}</td>
                    <td className={`${tdCNum} font-bold`}>{dinheiro(p.custoHora)}</td>
                    <td className={tdC}><SeloAtivo ativo={p.ativo} /></td>
                    <td className={tdC}>
                      <div className="flex gap-1">
                        <Button size="sm" variant="ghost" className="h-8 gap-1" onClick={() => abrirEdicao(p)} aria-label={`Editar ${p.nome}`}>
                          <Pencil className="h-3.5 w-3.5" />Editar
                        </Button>
                        {p.ativo && (
                          <Button size="sm" variant="ghost" className="h-8 gap-1 text-rose-500" onClick={() => setExcluir(p)} aria-label={`Excluir ${p.nome} deste projeto`}>
                            <Trash2 className="h-3.5 w-3.5" />Excluir
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <p className="text-xs text-muted-foreground">
        As colunas financeiras mostram a vigência válida na data atual. Alterações salariais são registradas em novas vigências e não recalculam o passado.
      </p>

      {selecionado && (
        <DetalheProfissional projectId={projectId} profissional={selecionado} onAlterado={lista.recarregar} onEditar={() => abrirEdicao(selecionado)} />
      )}

      <ProjectCostCopyPanel sourceProjectId={projectId} />

      {form && (
        <FormularioProfissional
          projectId={projectId}
          estado={form}
          onFechar={() => setForm(null)}
          onSalvo={(id) => {
            setForm(null);
            if (id) setSelecionadoId(id);
            lista.recarregar();
          }}
        />
      )}

      {importar && (
        <ImportarProfissionaisDialog
          projectId={projectId}
          onClose={() => setImportar(false)}
          onImported={() => { setImportar(false); lista.recarregar(); }}
        />
      )}

      {atribuir && (
        <AtribuirProfissionaisDialog
          projectId={projectId}
          professionals={selecionadosValidos}
          onClose={() => setAtribuir(false)}
          onDone={() => { setAtribuir(false); setSelecionados(new Set()); }}
        />
      )}

      <ConfirmarDesativacao
        aberto={excluir !== null}
        ocupado={ocupado}
        onCancelar={() => setExcluir(null)}
        onConfirmar={confirmarExclusao}
        titulo={excluir ? `Excluir ${excluir.nome} deste projeto?` : "Excluir profissional?"}
        descricao="O cadastro deixa de ficar ativo neste projeto, mas o histórico de remuneração e custos permanece preservado."
      />
    </div>
  );
}

function FormularioProfissional({ projectId, estado, onFechar, onSalvo }: {
  projectId: string;
  estado: { modo: "novo" | "editar"; valores: FormProf; id?: number };
  onFechar: () => void;
  onSalvo: (id?: number) => void;
}) {
  const [v, setV] = useState(estado.valores);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const set = <K extends keyof FormProf>(key: K, value: FormProf[K]) => setV((a) => ({ ...a, [key]: value }));

  const salvar = async (e: FormEvent) => {
    e.preventDefault();
    const hh = lerNumero(v.hhBaseMensal);
    if (!v.nome.trim()) { setErro("Informe o nome."); return; }
    if (hh === null || hh <= 0) { setErro("HH base mensal deve ser maior que zero."); return; }

    setSalvando(true);
    setErro(null);
    try {
      const corpo = {
        nome: v.nome.trim(),
        empresa: v.empresa.trim() || null,
        aliasTimesheet: v.aliasTimesheet.trim() || v.nome.trim(),
        hhBaseMensal: hh,
        funcao: v.funcao.trim() || null,
        disciplina: v.disciplina.trim() || null,
      };
      if (estado.modo === "novo") {
        const result = await custosApi.criarProfissional(projectId, corpo);
        toast.success("Profissional cadastrado.");
        onSalvo(result.id);
      } else {
        await custosApi.atualizarProfissional(projectId, estado.id!, { ...corpo, ativo: v.ativo });
        toast.success("Profissional atualizado.");
        onSalvo(estado.id);
      }
    } catch (e) {
      setErro(mensagemDe(e));
    } finally {
      setSalvando(false);
    }
  };

  return (
    <Dialog open onOpenChange={(open) => { if (!open && !salvando) onFechar(); }}>
      <DialogContent className="max-w-2xl">
        <form onSubmit={salvar} className="space-y-4">
          <DialogHeader>
            <DialogTitle>{estado.modo === "novo" ? "Novo profissional" : "Editar profissional"}</DialogTitle>
            <DialogDescription>Função e disciplina pertencem ao cadastro deste profissional no projeto atual.</DialogDescription>
          </DialogHeader>

          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Função"><CampoTexto value={v.funcao} onChange={(e) => set("funcao", e.target.value)} /></Campo>
            <Campo rotulo="Disciplina"><CampoTexto value={v.disciplina} onChange={(e) => set("disciplina", e.target.value)} /></Campo>
          </div>
          <Campo rotulo="Nome"><CampoTexto value={v.nome} onChange={(e) => set("nome", e.target.value)} autoFocus maxLength={200} /></Campo>
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Empresa"><CampoTexto value={v.empresa} onChange={(e) => set("empresa", e.target.value)} maxLength={200} /></Campo>
            <Campo rotulo="HH base mensal" dica="Usada no cálculo do custo/hora quando não houver taxa manual">
              <CampoTexto inputMode="decimal" value={v.hhBaseMensal} onChange={(e) => set("hhBaseMensal", e.target.value)} />
            </Campo>
          </div>
          <Campo rotulo="Alias no Timesheet" dica="Se vazio, será usado o próprio nome">
            <CampoTexto value={v.aliasTimesheet} onChange={(e) => set("aliasTimesheet", e.target.value)} maxLength={200} />
          </Campo>
          {estado.modo === "editar" && (
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={v.ativo} onChange={(e) => set("ativo", e.target.checked)} />Profissional ativo
            </label>
          )}
          {erro && <p role="alert" className="rounded-md border border-rose-500/40 bg-rose-500/10 px-3 py-2 text-sm text-rose-500">{erro}</p>}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onFechar} disabled={salvando}>Cancelar</Button>
            <Button type="submit" disabled={salvando} className="gap-1.5">
              {salvando && <Loader2 className="h-4 w-4 animate-spin" />}Salvar
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function DetalheProfissional({ projectId, profissional: p, onAlterado, onEditar }: {
  projectId: string; profissional: Profissional; onAlterado: () => void; onEditar: () => void;
}) {
  return (
    <section className="space-y-4 rounded-xl border border-border bg-card/60 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2"><h2 className="text-xl font-semibold">{p.nome}</h2><SeloAtivo ativo={p.ativo} /></div>
          <p className="text-sm text-muted-foreground">{p.funcao ?? "Sem função"} · {p.disciplina ?? "Sem disciplina"} · Alias: {p.aliasTimesheet ?? "—"}</p>
        </div>
        <Button variant="outline" size="sm" className="gap-1.5" onClick={onEditar}><Pencil className="h-3.5 w-3.5" />Editar cadastro</Button>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-6">
        <Valor rotulo="Remuneração" valor={dinheiro(p.remuneracao)} />
        <Valor rotulo="Encargos" valor={dinheiro(p.encargos)} />
        <Valor rotulo="Softwares" valor={dinheiro(p.softwares)} />
        <Valor rotulo="Computadores" valor={dinheiro(p.computadores)} />
        <Valor rotulo="Total" valor={dinheiro(p.custoMensal)} />
        <Valor rotulo="Custo/hora" valor={dinheiro(p.custoHora)} />
      </div>

      <CustosVigenciasProfissional key={p.id} projectId={projectId} professionalId={p.id} hours={p.hhBaseMensal} onSaved={onAlterado} />
    </section>
  );
}

function ImportarProfissionaisDialog({ projectId, onClose, onImported }: {
  projectId: string; onClose: () => void; onImported: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<PreviaImportacaoProfissionais | null>(null);
  const [busy, setBusy] = useState<"modelo" | "previa" | "importar" | null>(null);

  const baixarModelo = async () => {
    setBusy("modelo");
    try {
      const blob = await custosApi.baixarModeloProfissionais(projectId);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = "Modelo_Custos_Profissionais.xlsx";
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      // Revogar só depois do clique ser processado; revogar na hora cancela o download em alguns navegadores.
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      toast.error(mensagemDe(e));
    } finally {
      setBusy(null);
    }
  };

  const gerarPrevia = async () => {
    if (!file) { toast.error("Selecione um arquivo .xlsx."); return; }
    setBusy("previa");
    try {
      setPreview(await custosApi.previaImportacaoProfissionais(projectId, file));
    } catch (e) {
      toast.error(mensagemDe(e));
    } finally {
      setBusy(null);
    }
  };

  const confirmar = async () => {
    if (!file || !preview || preview.invalid > 0 || preview.valid === 0) return;
    setBusy("importar");
    try {
      const result = await custosApi.confirmarImportacaoProfissionais(projectId, file);
      toast.success(
        `Importado: ${result.professionalsCreated} novo(s), ${result.professionalsUpdated} atualizado(s), ${result.periodsCreated} vigência(s) criada(s), ${result.periodsUpdated} atualizada(s).`,
      );
      onImported();
    } catch (e) {
      toast.error(mensagemDe(e));
    } finally {
      setBusy(null);
    }
  };

  return (
    <Dialog open onOpenChange={(open) => { if (!open && busy === null) onClose(); }}>
      <DialogContent className="flex max-h-[90vh] max-w-[1180px] flex-col overflow-hidden">
        <DialogHeader>
          <DialogTitle>Importar custos de profissionais</DialogTitle>
          <DialogDescription>
            As 9 primeiras colunas seguem exatamente o padrão definido. Os campos de vigência ficam depois delas.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" className="gap-1.5" onClick={baixarModelo} disabled={busy !== null}>
            {busy === "modelo" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}Baixar modelo Excel
          </Button>
          <label className="flex min-w-[320px] flex-1 cursor-pointer items-center gap-2 rounded-md border border-border px-3 py-2 text-sm focus-within:ring-2 focus-within:ring-primary">
            <Upload className="h-4 w-4 text-muted-foreground" />
            <span className="truncate">{file?.name ?? "Selecionar arquivo .xlsx"}</span>
            <input
              className="sr-only"
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              aria-label="Arquivo Excel de profissionais"
              disabled={busy !== null}
              onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); }}
            />
          </label>
          <Button onClick={gerarPrevia} disabled={!file || busy !== null}>{busy === "previa" ? "Validando…" : "Gerar prévia"}</Button>
        </div>

        <div className="rounded-lg border bg-muted/20 px-3 py-2 text-xs text-muted-foreground">
          <b>função · disciplina · nome · Remuneração · Encargos · Softwares · Computadores · Total · Custo/hora</b>
          {" "}· opcionais para histórico: <b>Vigência início · Vigência fim · HH base mensal</b>. HH base assume 160 quando vazia.
        </div>

        {preview && (
          <>
            <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-6">
              <Mini label="Válidas" value={String(preview.valid)} />
              <Mini label="Inválidas" value={String(preview.invalid)} danger={preview.invalid > 0} />
              <Mini label="Avisos" value={String(preview.warnings)} />
              <Mini label="Novos profissionais" value={String(preview.professionalsToCreate)} />
              <Mini label="Novas vigências" value={String(preview.periodsToCreate)} />
              <Mini label="Vigências atualizadas" value={String(preview.periodsToUpdate)} />
            </div>
            <div className="min-h-0 flex-1 overflow-auto rounded-lg border">
              <table className="w-full min-w-[1500px] text-xs">
                <caption className="sr-only">Prévia da importação — nada foi gravado</caption>
                <thead className="sticky top-0 bg-muted">
                  <tr>
                    <th className={thC}>Linha</th><th className={thC}>Ação</th><th className={thC}>Função</th><th className={thC}>Disciplina</th><th className={thC}>Nome</th>
                    <th className={thCNum}>Remuneração</th><th className={thCNum}>Encargos</th><th className={thCNum}>Softwares</th><th className={thCNum}>Computadores</th>
                    <th className={thCNum}>Total</th><th className={thCNum}>Custo/hora</th><th className={thC}>Vigência</th><th className={thC}>Validação</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map((row) => (
                    <tr key={row.row} className={cn("border-t", !row.valid && "bg-rose-500/5")}>
                      <td className={tdC}>{row.row}</td>
                      <td className={tdC}>{row.action.replaceAll("_", " ")}</td>
                      <td className={tdC}>{row.funcao ?? "—"}</td>
                      <td className={tdC}>{row.disciplina ?? "—"}</td>
                      <td className={`${tdC} font-semibold`}>{row.nome}</td>
                      <td className={tdCNum}>{dinheiro(row.remuneracao)}</td>
                      <td className={tdCNum}>{dinheiro(row.encargos)}</td>
                      <td className={tdCNum}>{dinheiro(row.softwares)}</td>
                      <td className={tdCNum}>{dinheiro(row.computadores)}</td>
                      <td className={tdCNum}>{dinheiro(row.totalCalculado)}</td>
                      <td className={tdCNum}>{row.custoHora == null ? "Calculado" : dinheiro(row.custoHora)}</td>
                      <td className={tdC}>{dataFmt(row.vigenciaInicio)} – {row.vigenciaFim ? dataFmt(row.vigenciaFim) : "atual"}</td>
                      <td className={tdC}>
                        {row.error ? <span className="text-rose-500">{row.error}</span>
                          : row.warning ? <span className="text-amber-600">{row.warning}</span>
                            : <span className="text-emerald-600">OK</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}

        {preview && (
          <p className={cn("text-xs", preview.invalid > 0 ? "text-rose-500" : "text-muted-foreground")} role="status">
            {preview.invalid > 0
              ? "Prévia com linhas inválidas: corrija o arquivo e gere a prévia novamente. Nada foi gravado."
              : preview.valid === 0
                ? "Nenhuma linha válida para importar."
                : "Prévia apenas — nada foi gravado ainda. Vigências existentes não são apagadas."}
          </p>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy !== null}>Cancelar</Button>
          <Button onClick={confirmar} disabled={!preview || preview.invalid > 0 || preview.valid === 0 || busy !== null}>
            {busy === "importar" && <Loader2 className="mr-1 h-4 w-4 animate-spin" />}Confirmar importação ({preview?.valid ?? 0})
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function AtribuirProfissionaisDialog({ projectId, professionals, onClose, onDone }: {
  projectId: string; professionals: Profissional[]; onClose: () => void; onDone: () => void;
}) {
  const [projects, setProjects] = useState<ProjetoElegivelCusto[] | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [search, setSearch] = useState("");
  const [preview, setPreview] = useState<PreviaAtribuicaoProfissionais | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let vivo = true;
    // Endpoint já filtra apenas projetos ativos ou sincronizados ao menos uma vez.
    custosApi.projetosElegiveisCustos()
      .then((rows) => { if (vivo) setProjects(rows.filter((p) => p.projectIdAcc !== projectId)); })
      .catch((e) => { if (vivo) { setProjects([]); toast.error(mensagemDe(e)); } });
    return () => { vivo = false; };
  }, [projectId]);

  const filtered = useMemo(() => {
    const q = search.trim().toLocaleLowerCase("pt-BR");
    return (projects ?? []).filter((p) => !q || p.name.toLocaleLowerCase("pt-BR").includes(q) || p.projectIdAcc.toLowerCase().includes(q));
  }, [projects, search]);

  const payload = { professionalIds: professionals.map((p) => p.id), destinationProjectIds: [...selected] };
  const totalCriar = preview?.destinations.reduce((acc, d) => acc + d.toCreate, 0) ?? 0;

  const alternar = (id: string, checked: boolean) => {
    setSelected((current) => {
      const next = new Set(current);
      if (checked) next.add(id); else next.delete(id);
      return next;
    });
    setPreview(null);
  };

  const gerar = async () => {
    if (selected.size === 0) { toast.error("Selecione ao menos um projeto destino."); return; }
    setBusy(true);
    try {
      setPreview(await custosApi.previaAtribuicaoProfissionais(projectId, payload));
    } catch (e) {
      toast.error(mensagemDe(e));
    } finally {
      setBusy(false);
    }
  };

  const confirmar = async () => {
    if (!preview || totalCriar === 0) return;
    setBusy(true);
    try {
      const result = await custosApi.confirmarAtribuicaoProfissionais(projectId, payload);
      toast.success(`${result.professionalsCreated} cadastro(s) criado(s) nos projetos selecionados.`);
      onDone();
    } catch (e) {
      toast.error(mensagemDe(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open onOpenChange={(open) => { if (!open && !busy) onClose(); }}>
      <DialogContent className="flex max-h-[88vh] max-w-3xl flex-col overflow-hidden">
        <DialogHeader>
          <DialogTitle>Atribuir profissionais a projetos</DialogTitle>
          <DialogDescription>
            {professionals.length} profissional(is) selecionado(s): {professionals.slice(0, 4).map((p) => p.nome).join(", ")}
            {professionals.length > 4 ? ` e mais ${professionals.length - 4}` : ""}. A cópia leva cadastro, vigências, composição e aliases;
            {" "}cadastros já existentes no destino não são alterados.
          </DialogDescription>
        </DialogHeader>

        {!preview ? (
          <>
            <div className="rounded-lg border bg-muted/20 px-3 py-2 text-xs text-muted-foreground">
              Só aparecem projetos <b>ativos</b> ou que já tiveram <b>ao menos uma sincronização da LDP no banco</b>.
            </div>

            <div className="flex flex-wrap items-center gap-2">
              <div className="relative min-w-[240px] flex-1">
                <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <CampoTexto className="pl-8" placeholder="Buscar projeto…" aria-label="Buscar projeto destino" value={search} onChange={(e) => setSearch(e.target.value)} />
              </div>
              <span className="text-xs text-muted-foreground" aria-live="polite">{selected.size} projeto(s) selecionado(s)</span>
            </div>

            <fieldset className="min-h-0 flex-1 overflow-y-auto rounded-lg border">
              <legend className="sr-only">Projetos destino</legend>
              {projects === null ? (
                <p className="flex items-center gap-2 p-4 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Carregando projetos…</p>
              ) : filtered.length === 0 ? (
                <p className="p-4 text-sm text-muted-foreground">Nenhum projeto elegível{search.trim() ? " para a busca" : ""}.</p>
              ) : filtered.map((p) => (
                <label key={p.projectIdAcc} className="flex cursor-pointer items-center gap-3 border-b px-3 py-2 hover:bg-accent/30">
                  <input type="checkbox" checked={selected.has(p.projectIdAcc)} onChange={(e) => alternar(p.projectIdAcc, e.target.checked)} />
                  <span className="min-w-0 flex-1">
                    <b className="block truncate">{p.name}</b>
                    <span className="text-[10px] text-muted-foreground">{p.projectIdAcc}</span>
                  </span>
                  <SelosProjeto projeto={p} />
                </label>
              ))}
            </fieldset>
          </>
        ) : (
          <div className="min-h-0 flex-1 space-y-2 overflow-y-auto rounded-lg border p-3 text-sm" role="status">
            <p className="font-semibold">Prévia — nenhuma gravação foi feita</p>
            {preview.destinations.map((d) => (
              <div key={d.projectIdAcc} className="flex justify-between gap-3 border-b pb-1 text-xs last:border-b-0">
                <span className="truncate">{d.name}</span>
                <span className="shrink-0 tabular-nums">
                  <b className={d.toCreate > 0 ? "text-emerald-600" : undefined}>{d.toCreate} a criar</b> · {d.alreadyExists} já existente(s)
                </span>
              </div>
            ))}
            {totalCriar === 0 && <p className="text-xs text-amber-600">Todos os profissionais já existem nos destinos selecionados; nada será criado.</p>}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>Cancelar</Button>
          {!preview ? (
            <Button onClick={gerar} disabled={busy || selected.size === 0} className="gap-1.5">
              {busy && <Loader2 className="h-4 w-4 animate-spin" />}Gerar prévia ({selected.size})
            </Button>
          ) : (
            <>
              <Button variant="outline" onClick={() => setPreview(null)} disabled={busy}>Alterar seleção</Button>
              <Button onClick={confirmar} disabled={busy || totalCriar === 0} className="gap-1.5">
                {busy && <Loader2 className="h-4 w-4 animate-spin" />}Confirmar atribuição ({totalCriar})
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

const Valor = ({ rotulo, valor }: { rotulo: string; valor: string }) => (
  <div className="rounded-lg border border-border bg-background/60 p-3">
    <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{rotulo}</p>
    <p className="text-lg font-semibold tabular-nums">{valor}</p>
  </div>
);

const Mini = ({ label, value, danger = false }: { label: string; value: string; danger?: boolean }) => (
  <div className={cn("rounded-lg border px-3 py-2", danger && "border-rose-500/40 bg-rose-500/5")}>
    <p className="text-[10px] uppercase tracking-wide text-muted-foreground">{label}</p>
    <b className={cn("text-lg", danger && "text-rose-500")}>{value}</b>
  </div>
);
