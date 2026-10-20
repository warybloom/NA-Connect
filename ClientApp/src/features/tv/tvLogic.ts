// =============================================================================================
// Configuração de Telas para TV (Gestão à Vista) — lógica PURA, espelho das regras do backend
// (Services/Tv/TvConfiguration.cs → TvConfigurationRules). O backend continua sendo quem valida de fato.
// =============================================================================================

export interface TvSlideProjeto { hubId: string; projectId: string; projectName: string | null }
/**
 * Item da apresentação. hubId/projectId = projeto PRINCIPAL (formato antigo, um projeto). projetos = seleção multiprojeto
 * do item (só em telas que já consolidam vários projetos); o primeiro é sempre o principal.
 * dataReferencia (19/10/2026, opcional) = dia "AAAA-MM-DD" do item — OBRIGATÓRIO nas telas com data (Programação Diária) e
 * ignorado nas demais (o backend o descarta ao salvar, então nunca muda os dados de outra tela).
 */
export interface TvSlide {
  screenId: string; hubId: string; projectId: string; projectName: string | null; projetos?: TvSlideProjeto[] | null;
  dataReferencia?: string | null;
}

/** Data ISO real (AAAA-MM-DD, existe no calendário) — mesma regra do backend (DateOnly.TryParseExact "yyyy-MM-dd"). */
export function dataReferenciaValida(v: string | null | undefined): v is string {
  if (typeof v !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(v)) return false;
  const [a, m, d] = v.split("-").map(Number);
  const data = new Date(Date.UTC(a, m - 1, d));
  return a >= 1 && data.getUTCFullYear() === a && data.getUTCMonth() === m - 1 && data.getUTCDate() === d;
}

/** "15/10/2026" para a listagem/cabeçalho (sem fuso: a data é um dia, não um instante). */
export const formatarDataReferencia = (v: string) => v.split("-").reverse().join("/");

/** Projetos do item, exatamente como configurados (config antiga = só o principal). */
export const projetosDoSlide = (s: TvSlide): TvSlideProjeto[] =>
  s.projetos && s.projetos.length > 0 ? s.projetos : [{ hubId: s.hubId, projectId: s.projectId, projectName: s.projectName }];

/**
 * Item a partir de uma seleção (1 projeto = formato antigo; vários = lista com o principal primeiro). dataReferencia só entra
 * quando informada (telas sem data mantêm exatamente o formato anterior).
 */
export function slideComProjetos(screenId: string, projetos: TvSlideProjeto[], dataReferencia?: string | null): TvSlide {
  const [p] = projetos;
  const slide: TvSlide = { screenId, hubId: p.hubId, projectId: p.projectId, projectName: p.projectName, projetos: projetos.length > 1 ? projetos : null };
  return dataReferencia ? { ...slide, dataReferencia } : slide;
}

const codigoProjeto = (nome: string | null, id: string) => {
  const m = /^\s*([A-Z]{2,5}\d{2,4}(?:\.\d{1,3})*)\s*[-–—]/i.exec(nome ?? "");
  return m ? m[1].toUpperCase() : nome ?? id;
};

/** Escopo do item para a listagem: "CHS001", "CHS001 + BML011.016", "CHS001 + BML011.016 + 1 projeto"; completo no tooltip. */
export function resumoProjetosSlide(s: TvSlide): { texto: string; completo: string } {
  const ps = projetosDoSlide(s);
  const codigos = ps.map((p) => codigoProjeto(p.projectName, p.projectId));
  const resto = codigos.length - 2;
  return {
    // Muitos projetos: só a quantidade (a lista completa fica no tooltip).
    texto: codigos.length > 5 ? `${codigos.length} projetos`
      : resto > 0 ? `${codigos.slice(0, 2).join(" + ")} + ${resto} projeto${resto > 1 ? "s" : ""}` : codigos.join(" + "),
    completo: ps.map((p) => p.projectName ?? p.projectId).join("\n"),
  };
}
/** Configuração de UMA TV: name = "Nome da Tela / TV" (ex.: "TV SALA DE ENGENHARIA"). ativa/atualizadoEm opcionais (arquivos antigos). */
export interface TvPresentation {
  id: string; name: string; secondsPerScreen: number; slides: TvSlide[];
  ativa?: boolean | null; atualizadoEm?: string | null;
}

export const tvAtiva = (a: Pick<TvPresentation, "ativa">) => a.ativa ?? true;

/** Resumo para a listagem: projeto (ou MULTIPROJETO), conteúdo e status — sem precisar abrir a configuração. */
export function resumoTv(a: TvPresentation, rotuloTela: (id: string) => string) {
  const projetos = [...new Set(a.slides.flatMap((s) => projetosDoSlide(s).map((p) => p.projectName ?? p.projectId)))];
  const telas = [...new Set(a.slides.map((s) => rotuloTela(s.screenId)))];
  return {
    projeto: projetos.length === 0 ? "—" : projetos.length === 1 ? projetos[0] : "MULTIPROJETO",
    conteudo: telas.length === 0 ? "Sem telas" : telas.length <= 3 ? telas.join(" · ") : `${telas.slice(0, 3).join(" · ")} +${telas.length - 3}`,
    status: tvAtiva(a) ? "ATIVA" : "INATIVA",
  };
}
export interface TvConfiguration {
  version: number;
  revision: number;
  savedAt: string | null;
  savedBy: string | null;
  presentations: TvPresentation[];
}
/**
 * GET /api/tv-config. telasPermitidas = TODAS as telas que o servidor aceita salvar (valida configurações de qualquer
 * autor). telasAutorizadas = as que o usuário conectado pode abrir em algum projeto (null = servidor antigo ou falha ao
 * resolver permissões — o frontend continua exigindo a permissão local, nunca libera por falta da lista).
 */
export interface TvConfigResponse { configuration: TvConfiguration; telasPermitidas: string[]; telasAutorizadas?: string[] | null }

export const TV_LIMITES = { maxApresentacoes: 20, maxTelas: 50, minSegundos: 10, maxSegundos: 600, maxNome: 80 } as const;
export const TV_SEGUNDOS_PADRAO = 30;

const ID_VALIDO = /^[a-z0-9][a-z0-9-]{0,39}$/;
const ehAccId = (v: string | null | undefined) =>
  typeof v === "string" && /^b\.[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);

/** Identificador estável a partir do nome ("Sala de Reunião 2" → "sala-de-reuniao-2"), único na lista. */
export function gerarIdApresentacao(nome: string, existentes: readonly string[]): string {
  const base = (nome.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase()
    .replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 32)) || "apresentacao";
  if (!existentes.includes(base)) return base;
  for (let i = 2; ; i++) {
    const id = `${base}-${i}`;
    if (!existentes.includes(id)) return id;
  }
}

/** hubId vazio é aceito; se informado (configurações antigas), precisa ser um id ACC válido. */
export const hubOpcionalValido = (hub: string | null | undefined) => !hub || ehAccId(hub);

/**
 * Primeira mensagem de erro (mesmas regras do backend) ou null se a configuração pode ser salva.
 * telasProjetoUnico = telas que exibem um projeto por vez (TvTelas.MultiProjeto=false no backend).
 * telasProjetoExclusivo = tela → ÚNICO projeto aceito (TvTela.ProjetoExclusivo; Gestão à Vista Tipo 2 = CHS001).
 * telasComData = telas que exigem dataReferencia válida em cada item (TvTela.DataReferenciaObrigatoria).
 */
export function validarApresentacoes(lista: readonly TvPresentation[], telasPermitidas: readonly string[],
  telasProjetoUnico: readonly string[] = [], telasProjetoExclusivo: Readonly<Record<string, string>> = {},
  telasComData: readonly string[] = []): string | null {
  if (lista.length > TV_LIMITES.maxApresentacoes) return `Máximo de ${TV_LIMITES.maxApresentacoes} apresentações.`;
  const ids = new Set<string>();
  for (const a of lista) {
    const nome = a.name.trim();
    if (!nome || nome.length > TV_LIMITES.maxNome) return "Toda TV precisa de um Nome da Tela (até 80 caracteres).";
    if (!ID_VALIDO.test(a.id)) return `Identificador inválido na apresentação "${nome}".`;
    if (ids.has(a.id)) return `Apresentação duplicada: ${nome}.`;
    ids.add(a.id);
    if (!Number.isInteger(a.secondsPerScreen) || a.secondsPerScreen < TV_LIMITES.minSegundos || a.secondsPerScreen > TV_LIMITES.maxSegundos)
      return `"${nome}": tempo por tela deve ficar entre ${TV_LIMITES.minSegundos} e ${TV_LIMITES.maxSegundos} segundos.`;
    if (a.slides.length === 0) return `"${nome}" precisa de ao menos uma tela.`;
    if (a.slides.length > TV_LIMITES.maxTelas) return `"${nome}": máximo de ${TV_LIMITES.maxTelas} telas.`;
    for (const s of a.slides) {
      if (!telasPermitidas.includes(s.screenId)) return `"${nome}": a tela "${s.screenId}" não pode ser exibida na TV.`;
      // hubId é opcional: telas SQL identificam o projeto pelo Project_ID_ACC (usuário N&A sem Autodesk não tem hub).
      if (!hubOpcionalValido(s.hubId) || !ehAccId(s.projectId)) return `"${nome}": uma das telas está sem projeto válido.`;
      const ps = projetosDoSlide(s);
      if (ps.some((p) => !hubOpcionalValido(p.hubId) || !ehAccId(p.projectId))) return `"${nome}": projeto inválido em uma das telas.`;
      if (new Set(ps.map((p) => p.projectId)).size !== ps.length) return `"${nome}": projeto repetido em uma das telas.`;
      if (ps.length > 1 && telasProjetoUnico.includes(s.screenId))
        return `"${nome}": a tela "${s.screenId}" exibe um projeto por vez — use um item por projeto.`;
      const exclusivo = Object.hasOwn(telasProjetoExclusivo, s.screenId) ? telasProjetoExclusivo[s.screenId] : undefined;
      if (exclusivo && (s.projectId !== exclusivo || ps.some((p) => p.projectId !== exclusivo)))
        return `"${nome}": a tela "${s.screenId}" é exclusiva do projeto CHS001 (${exclusivo}) — remova o item ou troque o projeto para a CHS001.`;
      if (telasComData.includes(s.screenId) && !dataReferenciaValida(s.dataReferencia))
        return `"${nome}": informe uma data de referência válida (AAAA-MM-DD) em cada Programação Diária.`;
    }
  }
  return null;
}

/** Move um item da lista (reordenação das telas). Fora dos limites = lista inalterada. */
export function mover<T>(lista: readonly T[], de: number, para: number): T[] {
  if (de < 0 || de >= lista.length || para < 0 || para >= lista.length || de === para) return [...lista];
  const copia = [...lista];
  const [item] = copia.splice(de, 1);
  copia.splice(para, 0, item);
  return copia;
}

/** Igualdade estrutural (detecta alterações não salvas). */
export const mesmasApresentacoes = (a: readonly TvPresentation[], b: readonly TvPresentation[]) => JSON.stringify(a) === JSON.stringify(b);

/** Próximo índice do rodízio (volta ao início). */
export const proximoIndice = (atual: number, total: number, passo: 1 | -1 = 1) =>
  total <= 0 ? 0 : (((atual + passo) % total) + total) % total;

/** Apresentação a exibir no player: a pedida na URL; senão a primeira. */
export const escolherApresentacao = (lista: readonly TvPresentation[], id: string | null) =>
  (id ? lista.find((a) => a.id === id) : undefined) ?? lista[0];

// ---------------------------------------------------------------------------------------------
// Seletor de projetos da TV: só projetos ATIVOS no N&A (dbo.Projetos.Is_Ativo — mesmo registro da Home) e que o usuário
// enxerga; pesquisa por código ou nome, sem diferenciar acento/maiúscula.
// ---------------------------------------------------------------------------------------------
export interface ProjetoOpcao { id: string; nome: string }
const normalizar = (v: string) => v.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
const comPrefixo = (id: string) => (id.startsWith("b.") ? id : `b.${id}`);

export function projetosAtivos<T extends ProjetoOpcao>(projetos: readonly T[], ativo: (projectIdAcc: string) => boolean | undefined): T[] {
  return projetos.filter((p) => ativo(comPrefixo(p.id)) === true);
}

export function pesquisarProjetos<T extends ProjetoOpcao>(projetos: readonly T[], busca: string): T[] {
  const q = normalizar(busca.trim());
  return q ? projetos.filter((p) => normalizar(p.nome).includes(q) || normalizar(codigoProjeto(p.nome, p.id)).includes(q)) : [...projetos];
}

/** Texto compacto do seletor quando há muitos selecionados: "CHS001 + 14 projetos". */
export const resumoSelecao = (nomes: readonly { nome: string; id: string }[]) =>
  nomes.length === 0 ? "" : nomes.length === 1 ? codigoProjeto(nomes[0].nome, nomes[0].id)
    : `${codigoProjeto(nomes[0].nome, nomes[0].id)} + ${nomes.length - 1} projeto${nomes.length > 2 ? "s" : ""}`;
