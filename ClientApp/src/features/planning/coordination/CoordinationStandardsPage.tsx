// Cadastro FUNCIONAL de Norma de Coordenação (Sigla -> Disciplina) — PLANEJAMENTO > NORMAS DE
// COORDENAÇÃO. Persistência real via api/coordination-standards/* (schema coordenacao.*). Autoridade
// única de classificação do HH/Custo REALIZADO (Timesheet.Doc3) — ver PmoActualFactsProvider.cs.
import { useEffect, useMemo, useState } from 'react';
import { getProjetosRegistrados, type ProjetoRegistrado } from '../../../lib/projectsRegistry';
import * as api from './coordinationApi';
import type { ImportPreviewResultado, Norma, UnmappedSiglaDiagnostic } from './coordinationApi';

function useNormas() {
  const [normas, setNormas] = useState<Norma[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const reload = () => {
    setLoading(true); setError(null);
    api.listarNormas().then(setNormas).catch(e => setError(String(e.message || e))).finally(() => setLoading(false));
  };
  useEffect(reload, []);
  return { normas, loading, error, reload };
}

function Secao({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-lg border border-border bg-card/30 p-4 space-y-3">
      <h2 className="text-sm font-semibold tracking-wide text-foreground uppercase">{title}</h2>
      {children}
    </section>
  );
}

const inputCls = "rounded border border-border bg-background px-2 py-1 text-sm text-foreground";
const btnCls = "rounded bg-primary px-3 py-1 text-sm font-medium text-primary-foreground disabled:opacity-50";
const btnGhostCls = "rounded border border-border px-3 py-1 text-sm text-foreground hover:bg-card/60";

function NormaForm({ norma, onSaved }: { norma: Norma | null; onSaved: () => void }) {
  const [nome, setNome] = useState(norma?.nome ?? '');
  const [descricao, setDescricao] = useState(norma?.descricao ?? '');
  const [origem, setOrigem] = useState<'N&A' | 'CLIENTE'>(norma?.origem ?? 'N&A');
  const [ativa, setAtiva] = useState(norma?.ativa ?? true);
  const [salvando, setSalvando] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  useEffect(() => { setNome(norma?.nome ?? ''); setDescricao(norma?.descricao ?? ''); setOrigem(norma?.origem ?? 'N&A'); setAtiva(norma?.ativa ?? true); setMsg(null); }, [norma?.normaId]);

  async function salvar() {
    setSalvando(true); setMsg(null);
    try {
      if (norma) await api.atualizarNorma(norma.normaId, nome, descricao, origem, ativa);
      else await api.criarNorma(nome, descricao, origem);
      setMsg('Salvo.'); onSaved();
    } catch (e: any) { setMsg(e.message || String(e)); }
    finally { setSalvando(false); }
  }

  return (
    <div className="space-y-2">
      <label className="block text-xs text-muted-foreground">Nome
        <input className={`${inputCls} w-full`} value={nome} onChange={e => setNome(e.target.value)} placeholder="Ex.: N&A CORPORATIVA / CHS CLIENTE" />
      </label>
      <label className="block text-xs text-muted-foreground">Descrição (opcional)
        <input className={`${inputCls} w-full`} value={descricao ?? ''} onChange={e => setDescricao(e.target.value)} />
      </label>
      <div className="flex items-center gap-4">
        <label className="text-xs text-muted-foreground">Origem
          <select className={`${inputCls} ml-2`} value={origem} onChange={e => setOrigem(e.target.value as 'N&A' | 'CLIENTE')}>
            <option value="N&A">N&A</option>
            <option value="CLIENTE">CLIENTE</option>
          </select>
        </label>
        {norma && (
          <label className="flex items-center gap-1 text-xs text-muted-foreground">
            <input type="checkbox" checked={ativa} onChange={e => setAtiva(e.target.checked)} /> Ativa
          </label>
        )}
      </div>
      <div className="flex items-center gap-2">
        <button className={btnCls} disabled={salvando || !nome.trim()} onClick={salvar}>{norma ? 'Salvar alterações' : 'Criar Norma'}</button>
        {msg && <span className="text-xs text-muted-foreground">{msg}</span>}
      </div>
    </div>
  );
}

function MapeamentosEditor({ norma, onChanged }: { norma: Norma; onChanged: () => void }) {
  const [sigla, setSigla] = useState('');
  const [disciplina, setDisciplina] = useState('');
  const [busca, setBusca] = useState('');
  const [editandoId, setEditandoId] = useState<number | null>(null);
  const [editSigla, setEditSigla] = useState('');
  const [editDisciplina, setEditDisciplina] = useState('');
  const [erro, setErro] = useState<string | null>(null);

  const filtrados = useMemo(() => {
    const q = busca.trim().toUpperCase();
    return q === '' ? norma.mapeamentos : norma.mapeamentos.filter(m => m.sigla.includes(q) || m.disciplina.toUpperCase().includes(q));
  }, [norma.mapeamentos, busca]);

  async function adicionar() {
    setErro(null);
    try { await api.adicionarMapeamento(norma.normaId, sigla, disciplina); setSigla(''); setDisciplina(''); onChanged(); }
    catch (e: any) { setErro(e.message || String(e)); }
  }
  function iniciarEdicao(id: number, s: string, d: string) { setEditandoId(id); setEditSigla(s); setEditDisciplina(d); setErro(null); }
  async function salvarEdicao() {
    if (editandoId == null) return;
    try { await api.atualizarMapeamento(editandoId, editSigla, editDisciplina); setEditandoId(null); onChanged(); }
    catch (e: any) { setErro(e.message || String(e)); }
  }
  async function remover(id: number) {
    try { await api.removerMapeamento(id); onChanged(); }
    catch (e: any) { setErro(e.message || String(e)); }
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-xs text-muted-foreground">Sigla
          <input className={`${inputCls} ml-1 w-24`} value={sigla} onChange={e => setSigla(e.target.value)} placeholder="CV" />
        </label>
        <label className="text-xs text-muted-foreground">Disciplina
          <input className={`${inputCls} ml-1 w-56`} value={disciplina} onChange={e => setDisciplina(e.target.value)} placeholder="CIVIL" />
        </label>
        <button className={btnCls} disabled={!sigla.trim() || !disciplina.trim()} onClick={adicionar}>Adicionar</button>
        <input className={`${inputCls} ml-auto w-48`} placeholder="Pesquisar sigla/disciplina" value={busca} onChange={e => setBusca(e.target.value)} />
      </div>
      {erro && <p className="text-xs text-destructive">{erro}</p>}
      <div className="max-h-72 overflow-auto rounded border border-border">
        <table className="w-full text-sm">
          <thead className="sticky top-0 bg-card"><tr className="text-left text-xs text-muted-foreground">
            <th className="px-2 py-1">Sigla</th><th className="px-2 py-1">Disciplina</th><th className="px-2 py-1 w-32">Ações</th>
          </tr></thead>
          <tbody>
            {filtrados.map(m => (
              <tr key={m.normaMapeamentoId} className="border-t border-border/60">
                {editandoId === m.normaMapeamentoId ? (
                  <>
                    <td className="px-2 py-1"><input className={`${inputCls} w-20`} value={editSigla} onChange={e => setEditSigla(e.target.value)} /></td>
                    <td className="px-2 py-1"><input className={`${inputCls} w-full`} value={editDisciplina} onChange={e => setEditDisciplina(e.target.value)} /></td>
                    <td className="px-2 py-1 space-x-1">
                      <button className={btnGhostCls} onClick={salvarEdicao}>Salvar</button>
                      <button className={btnGhostCls} onClick={() => setEditandoId(null)}>Cancelar</button>
                    </td>
                  </>
                ) : (
                  <>
                    <td className="px-2 py-1 font-mono">{m.sigla}</td>
                    <td className="px-2 py-1">{m.disciplina}</td>
                    <td className="px-2 py-1 space-x-1">
                      <button className={btnGhostCls} onClick={() => iniciarEdicao(m.normaMapeamentoId, m.sigla, m.disciplina)}>Editar</button>
                      <button className={btnGhostCls} onClick={() => remover(m.normaMapeamentoId)}>Remover</button>
                    </td>
                  </>
                )}
              </tr>
            ))}
            {filtrados.length === 0 && <tr><td colSpan={3} className="px-2 py-3 text-center text-xs text-muted-foreground">Nenhuma sigla cadastrada.</td></tr>}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function ImportExcel({ norma, onImported }: { norma: Norma; onImported: () => void }) {
  const [preview, setPreview] = useState<ImportPreviewResultado | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  async function selecionar(e: React.ChangeEvent<HTMLInputElement>) {
    const arquivo = e.target.files?.[0]; e.target.value = '';
    if (!arquivo) return;
    setCarregando(true); setErro(null); setMsg(null); setPreview(null);
    try { setPreview(await api.previewImportarExcel(norma.normaId, arquivo)); }
    catch (e: any) { setErro(e.message || String(e)); }
    finally { setCarregando(false); }
  }
  async function confirmar() {
    if (!preview) return;
    const linhas = preview.linhas.filter(l => l.valida).map(l => ({ sigla: l.siglaNormalizada, disciplina: l.disciplinaOriginal!.trim() }));
    setCarregando(true); setErro(null);
    try { const r = await api.confirmarImportacao(norma.normaId, linhas); setMsg(r.message); setPreview(null); onImported(); }
    catch (e: any) { setErro(e.message || String(e)); }
    finally { setCarregando(false); }
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <input type="file" accept=".xlsx" onChange={selecionar} className="text-xs text-muted-foreground" disabled={carregando} />
        <span className="text-xs text-muted-foreground">Colunas: SIGLA, DISCIPLINA.</span>
      </div>
      {erro && <p className="text-xs text-destructive">{erro}</p>}
      {msg && <p className="text-xs text-emerald-500">{msg}</p>}
      {preview && (
        <div className="space-y-2">
          <p className="text-xs text-muted-foreground">{preview.totalLinhas} linha(s) · {preview.linhasValidas} válida(s) · {preview.linhasInvalidas} com erro.</p>
          <div className="max-h-64 overflow-auto rounded border border-border">
            <table className="w-full text-xs">
              <thead className="sticky top-0 bg-card"><tr className="text-left text-muted-foreground">
                <th className="px-2 py-1">Linha</th><th className="px-2 py-1">Sigla</th><th className="px-2 py-1">Disciplina</th><th className="px-2 py-1">Status</th>
              </tr></thead>
              <tbody>
                {preview.linhas.map(l => (
                  <tr key={l.linha} className={`border-t border-border/60 ${l.valida ? '' : 'bg-destructive/10'}`}>
                    <td className="px-2 py-1">{l.linha}</td>
                    <td className="px-2 py-1 font-mono">{l.siglaOriginal}</td>
                    <td className="px-2 py-1">{l.disciplinaOriginal}</td>
                    <td className="px-2 py-1">{l.erros.concat(l.avisos).join(' ')}{l.valida && l.erros.length === 0 && l.avisos.length === 0 ? 'OK' : ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <button className={btnCls} disabled={carregando || preview.linhasValidas === 0} onClick={confirmar}>Importar {preview.linhasValidas} linha(s) válida(s)</button>
        </div>
      )}
    </div>
  );
}

function ProjetoNormaVinculo({ normas }: { normas: Norma[] }) {
  const [projetos, setProjetos] = useState<ProjetoRegistrado[]>([]);
  const [projectId, setProjectId] = useState('');
  const [normaAtual, setNormaAtual] = useState<{ normaId: number | null; norma: Norma | null } | null>(null);
  const [normaEscolhida, setNormaEscolhida] = useState<number | ''>('');
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => { getProjetosRegistrados().then(map => setProjetos([...map.values()].sort((a, b) => a.nomeProjeto.localeCompare(b.nomeProjeto)))).catch(() => setProjetos([])); }, []);
  useEffect(() => {
    if (!projectId) { setNormaAtual(null); return; }
    api.obterNormaDoProjeto(projectId).then(r => { setNormaAtual(r); setNormaEscolhida(r.normaId ?? ''); }).catch(() => setNormaAtual(null));
  }, [projectId]);

  async function salvar() {
    if (!projectId || normaEscolhida === '') return;
    setMsg(null);
    try { await api.definirNormaDoProjeto(projectId, Number(normaEscolhida)); setMsg('Norma do projeto atualizada.'); const r = await api.obterNormaDoProjeto(projectId); setNormaAtual(r); }
    catch (e: any) { setMsg(e.message || String(e)); }
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <label className="text-xs text-muted-foreground">Projeto
          <select className={`${inputCls} ml-1`} value={projectId} onChange={e => setProjectId(e.target.value)}>
            <option value="">Selecione…</option>
            {projetos.map(p => <option key={p.projectIdAcc} value={p.projectIdAcc}>{p.nomeProjeto}</option>)}
          </select>
        </label>
        {projectId && (
          <>
            <label className="text-xs text-muted-foreground">Norma de Coordenação
              <select className={`${inputCls} ml-1`} value={normaEscolhida} onChange={e => setNormaEscolhida(e.target.value ? Number(e.target.value) : '')}>
                <option value="">Selecione…</option>
                {normas.map(n => <option key={n.normaId} value={n.normaId}>{n.nome} ({n.origem})</option>)}
              </select>
            </label>
            <button className={btnCls} disabled={normaEscolhida === ''} onClick={salvar}>Vincular projeto</button>
          </>
        )}
      </div>
      {projectId && <p className="text-xs text-muted-foreground">Norma atual: {normaAtual?.norma?.nome ?? 'nenhuma vinculada — lançamentos deste projeto caem em SEM DISCIPLINA.'}</p>}
      {msg && <p className="text-xs text-emerald-500">{msg}</p>}
    </div>
  );
}

function Diagnostico() {
  const [projetos, setProjetos] = useState<ProjetoRegistrado[]>([]);
  const [selecionados, setSelecionados] = useState<string[]>([]);
  const [resultado, setResultado] = useState<UnmappedSiglaDiagnostic[] | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => { getProjetosRegistrados().then(map => setProjetos([...map.values()].filter(p => p.isAtivo).sort((a, b) => a.nomeProjeto.localeCompare(b.nomeProjeto)))).catch(() => setProjetos([])); }, []);

  function toggle(id: string) { setSelecionados(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]); }
  async function apurar() {
    if (selecionados.length === 0) return;
    setCarregando(true); setErro(null);
    try { setResultado(await api.siglasNaoMapeadas(selecionados)); }
    catch (e: any) { setErro(e.message || String(e)); }
    finally { setCarregando(false); }
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        {projetos.map(p => (
          <label key={p.projectIdAcc} className="flex items-center gap-1 text-xs text-muted-foreground">
            <input type="checkbox" checked={selecionados.includes(p.projectIdAcc)} onChange={() => toggle(p.projectIdAcc)} /> {p.nomeProjeto}
          </label>
        ))}
      </div>
      <button className={btnCls} disabled={carregando || selecionados.length === 0} onClick={apurar}>Apurar siglas não mapeadas</button>
      {erro && <p className="text-xs text-destructive">{erro}</p>}
      {resultado && (
        <div className="max-h-64 overflow-auto rounded border border-border">
          <table className="w-full text-xs">
            <thead className="sticky top-0 bg-card"><tr className="text-left text-muted-foreground">
              <th className="px-2 py-1">Sigla</th><th className="px-2 py-1">Lançamentos</th><th className="px-2 py-1">HH</th><th className="px-2 py-1">Projetos</th>
            </tr></thead>
            <tbody>
              {resultado.map(r => (
                <tr key={r.sigla} className="border-t border-border/60">
                  <td className="px-2 py-1 font-mono">{r.sigla}</td>
                  <td className="px-2 py-1">{r.lancamentos}</td>
                  <td className="px-2 py-1">{r.hh?.toFixed(2) ?? '—'}</td>
                  <td className="px-2 py-1">{r.projetos.length}</td>
                </tr>
              ))}
              {resultado.length === 0 && <tr><td colSpan={4} className="px-2 py-3 text-center text-muted-foreground">Nenhuma sigla sem correspondência — todas as horas foram classificadas.</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export function CoordinationStandardsPage() {
  const { normas, loading, error, reload } = useNormas();
  const [selecionadaId, setSelecionadaId] = useState<number | null>(null);
  const [criandoNova, setCriandoNova] = useState(false);
  const selecionada = normas.find(n => n.normaId === selecionadaId) ?? null;

  return (
    <div className="space-y-4 p-4">
      <p className="text-sm text-muted-foreground">
        HH/Custo REALIZADO é classificado por disciplina através de Project_ID_ACC + Doc3 (3ª parte do N&amp;A
        Document Number) resolvido pela Norma de Coordenação do próprio projeto. Sem correspondência, o
        lançamento vira <span className="font-mono">SEM DISCIPLINA</span> mas nunca é excluído do total.
      </p>
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Secao title="Normas de Coordenação">
          {loading && <p className="text-xs text-muted-foreground">Carregando…</p>}
          {error && <p className="text-xs text-destructive">{error}</p>}
          <ul className="space-y-1">
            {normas.map(n => (
              <li key={n.normaId}>
                <button
                  className={`w-full rounded border px-2 py-1 text-left text-sm ${selecionadaId === n.normaId ? 'border-primary bg-primary/10' : 'border-border hover:bg-card/60'}`}
                  onClick={() => { setSelecionadaId(n.normaId); setCriandoNova(false); }}
                >
                  <span className={n.ativa ? '' : 'opacity-50'}>{n.nome}</span>
                  <span className="ml-2 text-xs text-muted-foreground">{n.origem} · {n.mapeamentos.length} sigla(s) · {n.projetosVinculados} projeto(s)</span>
                </button>
              </li>
            ))}
          </ul>
          <button className={btnGhostCls} onClick={() => { setCriandoNova(true); setSelecionadaId(null); }}>+ Nova Norma</button>
        </Secao>

        <div className="lg:col-span-2 space-y-4">
          {criandoNova && (
            <Secao title="Nova Norma de Coordenação">
              <NormaForm norma={null} onSaved={() => { setCriandoNova(false); reload(); }} />
            </Secao>
          )}
          {selecionada && (
            <>
              <Secao title={`Norma: ${selecionada.nome}`}>
                <NormaForm norma={selecionada} onSaved={reload} />
              </Secao>
              <Secao title="Mapeamentos (Sigla → Disciplina)">
                <MapeamentosEditor norma={selecionada} onChanged={reload} />
              </Secao>
              <Secao title="Importar Excel (SIGLA, DISCIPLINA)">
                <ImportExcel norma={selecionada} onImported={reload} />
              </Secao>
            </>
          )}
          {!selecionada && !criandoNova && <p className="text-sm text-muted-foreground">Selecione uma Norma à esquerda, ou crie uma nova.</p>}
        </div>
      </div>

      <Secao title="Norma de Coordenação do projeto">
        <ProjetoNormaVinculo normas={normas} />
      </Secao>

      <Secao title="Diagnóstico — siglas não mapeadas">
        <Diagnostico />
      </Secao>
    </div>
  );
}

export default CoordinationStandardsPage;
