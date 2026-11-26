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
  /**
   * 20/10/2026 — só no slide AUTOMÁTICO (dataReferencia "AUTO"): exibição. Ausente/"padrao" = layout da 02 dividido em
   * Hoje/Ontem; "cards" = blocos por disciplina; "disciplina" = uma disciplina só (disciplina = chave do grupo, a mesma da
   * classificação/Normalizador; disciplinaNome = rótulo). O backend descarta os campos em qualquer outro slide.
   */
  layoutDiario?: LayoutDiario | null;
  disciplina?: string | null;
  disciplinaNome?: string | null;
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

// ---------------------------------------------------------------------------------------------
// Programação Diária AUTOMÁTICA (20/10/2026): dataReferencia = "AUTO" (espelho de TvDiaAutomatico.Auto no backend).
// HOJE = data civil de America/Bahia no instante da exibição; ONTEM = hoje − 1. Datas fixas continuam como antes.
// ---------------------------------------------------------------------------------------------
export const DATA_REFERENCIA_AUTO = "AUTO";
export const ROTULO_DIARIA_AUTO = "Hoje + pendências de ontem (automático)";
export const dataReferenciaAutomatica = (v: string | null | undefined): v is typeof DATA_REFERENCIA_AUTO => v === DATA_REFERENCIA_AUTO;
/** Data de um item com data: dia fixo ISO real OU "AUTO". Ausente continua inválida. */
export const dataReferenciaDoSlideValida = (v: string | null | undefined) => dataReferenciaAutomatica(v) || dataReferenciaValida(v);

/** Exibição do slide automático (espelho de TvDiaAutomatico.Layout* no backend). */
export type LayoutDiario = "padrao" | "cards" | "disciplina";
export const LAYOUTS_DIARIOS: readonly { id: LayoutDiario; rotulo: string }[] = [
  { id: "padrao", rotulo: "Padrão" },
  { id: "cards", rotulo: "Cards por disciplina" },
  { id: "disciplina", rotulo: "Uma tela por disciplina" },
];
const LAYOUTS_VALIDOS = new Set<string>(LAYOUTS_DIARIOS.map((l) => l.id));
/** Exibição efetiva do slide (ausente = padrão). Valor desconhecido continua desconhecido (a validação recusa). */
export const layoutDoSlide = (s: Pick<TvSlide, "layoutDiario">): string => s.layoutDiario ?? "padrao";
export const rotuloLayoutDiario = (s: Pick<TvSlide, "layoutDiario" | "disciplina" | "disciplinaNome">) =>
  layoutDoSlide(s) === "disciplina" ? `Disciplina: ${s.disciplinaNome?.trim() || s.disciplina}`
    : LAYOUTS_DIARIOS.find((l) => l.id === layoutDoSlide(s))?.rotulo ?? String(s.layoutDiario);
/** Chave de duplicidade do slide automático: tela + projeto + exibição + disciplina (exibições/disciplinas distintas coexistem). */
export const chaveDiariaAutomatica = (s: TvSlide) =>
  `${s.screenId}|${s.projectId}|${layoutDoSlide(s)}|${layoutDoSlide(s) === "disciplina" ? (s.disciplina ?? "").trim() : ""}`;

/** Data civil (AAAA-MM-DD) da Bahia no instante dado. Sem base de fusos no navegador: UTC−3 fixo (sem horário de verão desde 2012). */
export function dataCivilBahia(instante: Date = new Date()): string {
  try { return instante.toLocaleDateString("en-CA", { timeZone: "America/Bahia" }); }
  catch { return new Date(instante.getTime() - 3 * 3_600_000).toISOString().slice(0, 10); }
}
export function diaAnterior(dia: string): string {
  const d = new Date(dia + "T12:00:00Z");
  d.setUTCDate(d.getUTCDate() - 1);
  return d.toISOString().slice(0, 10);
}
/** Janela consultada pela tela automática: [ontem, hoje] — no domingo cruza a semana numa consulta só (sem duplicar). */
export const janelaDiariaAutomatica = (hoje: string) => ({ hoje, ontem: diaAnterior(hoje), inicio: diaAnterior(hoje), fim: hoje });

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
// ---------------------------------------------------------------------------------------------
// Titulo central de TODOS os slides de projeto unico.
// Multprojeto permanece com o nome original da tela ate nova decisao do usuario.
// A disciplina aparece somente quando o slide exibe uma disciplina individual.
// ---------------------------------------------------------------------------------------------
export interface TituloCentralTv { texto: string; destaque: boolean; partes?: { projeto: string; disciplina: string } }
export function tituloCentralTv(s: TvSlide, rotuloTela: string | null | undefined, telaDiaria: string): TituloCentralTv {
  const original = { texto: rotuloTela ?? s.screenId, destaque: false };
  const ps = projetosDoSlide(s);
  if (ps.length !== 1) return original;
  const [p] = ps;
  const projeto = p.projectName?.trim() || p.projectId?.trim() || s.projectId?.trim();
  if (!projeto) return original;
  // Apenas diaria AUTO com layout de disciplina unica recebe o nome dessa disciplina.
  // Nas demais telas, mesmo com varias disciplinas, o cabecalho mostra SOMENTE o projeto.
  const unicaDisciplina = s.screenId === telaDiaria
    && dataReferenciaAutomatica(s.dataReferencia)
    && layoutDoSlide(s) === "disciplina"
    && !!s.disciplina?.trim();
  const disciplina = unicaDisciplina ? s.disciplinaNome?.trim() || s.disciplina?.trim() : null;
  return disciplina
    ? { texto: projeto + " — " + disciplina, destaque: true, partes: { projeto, disciplina } }
    : { texto: projeto, destaque: true };
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
export interface TvConfigResponse { configuration: TvConfiguration; telasPermitidas: string[]; telasAutorizadas?: string[] | null;
  /** 20/10/2026: servidor aceita dataReferencia "AUTO" (ausente = backend anterior). */
  diariaAutomatica?: boolean | null;
  /** 20/10/2026: servidor guarda a exibição do slide automático (layoutDiario/disciplina); ausente = só o padrão é seguro. */
  layoutsDiarios?: boolean | null }

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
      if (telasComData.includes(s.screenId) && !dataReferenciaDoSlideValida(s.dataReferencia))
        return `"${nome}": informe uma data de referência válida (AAAA-MM-DD) em cada Programação Diária.`;
    }
    // Programação Diária automática idêntica (mesma tela + projeto + exibição + disciplina) nunca se repete (mesma regra do backend).
    const automaticas = a.slides.filter((s) => telasComData.includes(s.screenId) && dataReferenciaAutomatica(s.dataReferencia));
    for (const s of automaticas) {
      if (!LAYOUTS_VALIDOS.has(layoutDoSlide(s))) return `"${nome}": exibição inválida na Programação Diária automática (${s.layoutDiario}).`;
      if (layoutDoSlide(s) === "disciplina" && !s.disciplina?.trim())
        return `"${nome}": a Programação Diária automática "Uma tela por disciplina" de ${s.projectName ?? s.projectId} está sem disciplina.`;
    }
    const repetida = automaticas.find((s, i) => automaticas.findIndex((x) => chaveDiariaAutomatica(x) === chaveDiariaAutomatica(s)) !== i);
    if (repetida) return `"${nome}": Programação Diária automática repetida para ${repetida.projectName ?? repetida.projectId}`
      + `${layoutDoSlide(repetida) === "padrao" ? "" : ` (${rotuloLayoutDiario(repetida)})`} — remova a cópia.`;
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

// ---------------------------------------------------------------------------------------------
// Botão "Gerar telas diárias automáticas" (20/10/2026): UMA Programação Diária AUTO por projeto + 01 VISÃO GERAL no início
// (se ainda não houver). Só altera o RASCUNHO; nada é parcial: acima do limite, nada é adicionado.
// ---------------------------------------------------------------------------------------------
export type SituacaoVisaoGeral = "existente" | "inserida" | "sem-permissao" | "indisponivel";
/** Disciplina escolhida para "Uma tela por disciplina": chave do grupo (classificação/Normalizador) + rótulo. */
export interface DisciplinaEscolhida { chave: string; nome: string }
export interface GeracaoDiariaAutomatica {
  /** Nova sequência (null = nada muda: bloqueado ou tudo já existia). */
  slides: TvSlide[] | null;
  adicionados: TvSlideProjeto[];
  jaExistentes: TvSlideProjeto[];
  /** Slides diários novos (um por projeto; na exibição por disciplina, um por projeto × disciplina). */
  novosSlides: TvSlide[];
  semPermissao: TvSlideProjeto[];
  visaoGeral: SituacaoVisaoGeral;
  /** Projetos sem permissão da 01 (quando ela não pôde ser inserida por isso). */
  semPermissaoVisaoGeral: TvSlideProjeto[];
  /** Itens que a sequência teria (para a contagem × limite). */
  totalResultante: number;
  bloqueio: string | null;
  mensagens: string[];
}

const nomeProjeto = (p: TvSlideProjeto) => codigoProjeto(p.projectName, p.projectId);

/** Slide AUTO de um projeto na exibição escolhida (padrão = formato anterior, sem campos extras). */
export function slideDiarioAutomatico(telaDiaria: string, p: TvSlideProjeto, layout: LayoutDiario = "padrao", disciplina?: DisciplinaEscolhida): TvSlide {
  const base = slideComProjetos(telaDiaria, [p], DATA_REFERENCIA_AUTO);
  return layout === "padrao" ? base
    : layout === "cards" ? { ...base, layoutDiario: "cards" }
    : { ...base, layoutDiario: "disciplina", disciplina: disciplina!.chave, disciplinaNome: disciplina!.nome };
}

export function gerarTelasDiariasAutomaticas({ slides, projetos, telaDiaria, telaVisaoGeral, podeDiaria, podeVisaoGeral, visaoGeralDisponivel,
  limite = TV_LIMITES.maxTelas, layout = "padrao", disciplinas = [] }: {
  slides: readonly TvSlide[];
  /** Projetos escolhidos (selecionados OU todos os permitidos), na ordem de exibição. */
  projetos: readonly TvSlideProjeto[];
  telaDiaria: string; telaVisaoGeral: string;
  podeDiaria: (projectId: string) => boolean;
  podeVisaoGeral: (projectId: string) => boolean;
  /** 01 Visão Geral está entre as telas que o usuário pode adicionar (servidor + permissão). */
  visaoGeralDisponivel: boolean;
  limite?: number;
  /** Exibição dos slides gerados (padrão = layout da 02 em Hoje/Ontem). */
  layout?: LayoutDiario;
  /** Só "disciplina": disciplinas escolhidas (vindas da Norma/programação do servidor, nunca digitadas); um slide por projeto × disciplina. */
  disciplinas?: readonly DisciplinaEscolhida[];
}): GeracaoDiariaAutomatica {
  const unicos = projetos.filter((p, i) => projetos.findIndex((x) => x.projectId === p.projectId) === i);
  const semPermissao = unicos.filter((p) => !podeDiaria(p.projectId));
  const aptos = unicos.filter((p) => podeDiaria(p.projectId));
  const discs = disciplinas.filter((d, i) => d.chave.trim() !== "" && disciplinas.findIndex((x) => x.chave === d.chave) === i);
  // Candidatos: padrão/cards = um por projeto; disciplina = um por projeto × disciplina. Duplicata = mesma chave (tela+projeto+exibição+disciplina).
  const candidatos = aptos.flatMap((p) => layout === "disciplina" ? discs.map((d) => ({ p, slide: slideDiarioAutomatico(telaDiaria, p, layout, d) }))
    : [{ p, slide: slideDiarioAutomatico(telaDiaria, p, layout) }]);
  const chavesExistentes = new Set(slides.filter((s) => s.screenId === telaDiaria && dataReferenciaAutomatica(s.dataReferencia)).map(chaveDiariaAutomatica));
  const novosC = candidatos.filter((c) => !chavesExistentes.has(chaveDiariaAutomatica(c.slide)));
  const existentesC = candidatos.filter((c) => chavesExistentes.has(chaveDiariaAutomatica(c.slide)));
  const projetosDe = (cs: typeof candidatos) => cs.map((c) => c.p).filter((p, i, xs) => xs.findIndex((x) => x.projectId === p.projectId) === i);
  const novos = projetosDe(novosC);
  const jaExistentes = projetosDe(existentesC).filter((p) => !novos.some((x) => x.projectId === p.projectId));
  const semPermissaoVisaoGeral = aptos.filter((p) => !podeVisaoGeral(p.projectId));
  const visaoGeral: SituacaoVisaoGeral = slides.some((s) => s.screenId === telaVisaoGeral) ? "existente"
    : !visaoGeralDisponivel ? "indisponivel"
    : aptos.length === 0 || semPermissaoVisaoGeral.length > 0 ? "sem-permissao" : "inserida";
  const inserir = visaoGeral === "inserida";
  const totalResultante = slides.length + (inserir ? 1 : 0) + novosC.length;
  const base = { adicionados: [] as TvSlideProjeto[], novosSlides: [] as TvSlide[], jaExistentes, semPermissao, visaoGeral, semPermissaoVisaoGeral, totalResultante };
  const lista = (ps: readonly TvSlideProjeto[]) => ps.map(nomeProjeto).join(", ");
  const listaSlides = (cs: typeof candidatos) => cs.map((c) => layout === "disciplina" ? `${nomeProjeto(c.p)} · ${c.slide.disciplinaNome}` : nomeProjeto(c.p)).join(", ");
  const sufixoLayout = layout === "padrao" ? "" : ` (${LAYOUTS_DIARIOS.find((l) => l.id === layout)!.rotulo})`;

  if (unicos.length === 0) return { ...base, slides: null, bloqueio: "Nenhum projeto escolhido.", mensagens: [] };
  if (aptos.length === 0)
    return { ...base, slides: null, mensagens: [],
      bloqueio: `Sem permissão para a Programação Diária em nenhum dos ${unicos.length} projeto(s): ${lista(semPermissao)}.` };
  if (layout === "disciplina" && discs.length === 0)
    return { ...base, slides: null, mensagens: [], bloqueio: "Uma tela por disciplina: escolha ao menos uma disciplina do projeto (nada foi adicionado)." };
  if (totalResultante > limite)
    return { ...base, slides: null, mensagens: [],
      bloqueio: `Limite de ${limite} telas por TV: seriam ${totalResultante} (${slides.length} atuais + ${novosC.length} diária(s)${inserir ? " + 01 Visão Geral" : ""}). `
        + "Nada foi adicionado — escolha menos projetos ou remova telas." };

  const mensagens = [
    novosC.length > 0 ? `${novosC.length} tela(s) diária(s) automática(s)${sufixoLayout} adicionada(s): ${listaSlides(novosC)}.` : "",
    existentesC.length > 0 ? `${existentesC.length} já existia(m) e não foi(ram) duplicada(s): ${listaSlides(existentesC)}.` : "",
    semPermissao.length > 0 ? `${semPermissao.length} projeto(s) ignorado(s) por falta de permissão na Programação Diária: ${lista(semPermissao)}.` : "",
    visaoGeral === "inserida" ? `01 Visão Geral inserida no início, com os ${aptos.length} projeto(s).`
      : visaoGeral === "existente" ? "01 Visão Geral já está na sequência (posição mantida)."
      : visaoGeral === "indisponivel" ? "01 Visão Geral NÃO inserida: tela indisponível para o seu perfil/servidor."
      : `01 Visão Geral NÃO inserida: sem permissão em ${lista(semPermissaoVisaoGeral)}.`,
  ].filter(Boolean);
  if (novosC.length === 0 && !inserir)
    return { ...base, slides: null, bloqueio: null, mensagens: ["Nada a adicionar: todas as telas diárias automáticas já existem.", ...mensagens] };
  const novosSlides = novosC.map((c) => c.slide);
  return {
    ...base, adicionados: novos, novosSlides, bloqueio: null, mensagens: [...mensagens, "Clique em Salvar para gravar."],
    slides: [...(inserir ? [slideComProjetos(telaVisaoGeral, [...aptos])] : []), ...slides, ...novosSlides],
  };
}
