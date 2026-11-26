// Estado local (React Context) das 4 páginas de Configuração da LDP. Rodada 2: persistência REAL
// existe (ver ./ldpApi.ts, chamada explicitamente por botões "Salvar no banco" em cada página) contra
// api/ldp-config/*, mas só funciona depois que Migrations/ConfiguracaoLdp/*.sql forem aplicadas
// manualmente. Enquanto isso, ou quando o usuário não clica em salvar, o estado fica só na memória do
// navegador (perdido ao recarregar) — nunca localStorage. Ver AUDITORIA_FORGESYNC_NA/17-*.md.
import React, { createContext, useContext, useState, useCallback, ReactNode } from "react";
import type {
  LdpTemplate,
  TemplateAba,
  TemplateCampo,
  CronogramaMapeamento,
  CronogramaMapeamentoCampo,
  ExcelModelo,
  ExcelModeloAba,
  ExcelModeloCampo,
  ExcelControleConfig,
  ExcelControleAba,
  ExcelControleCampo,
  OrigemCriacaoTemplate,
  ProjetoConfiguracao,
  ProjetoFase,
  ProjetoFaseRegra,
  ModoEstrutura,
  ModoGeracaoFase,
} from "@/types/ldp-config";

const novoId = () =>
  typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : `tmp_${Date.now()}_${Math.random().toString(36).slice(2)}`;

interface LdpConfigState {
  templates: LdpTemplate[];
  mapeamentos: CronogramaMapeamento[];
  modelos: ExcelModelo[];
  controles: ExcelControleConfig[];

  criarTemplate: (nome: string, descricao: string, origem: OrigemCriacaoTemplate, baseParaClone?: string) => string;
  removerTemplate: (templateId: string) => void;
  atualizarTemplate: (templateId: string, patch: Partial<Pick<LdpTemplate, "nomeTemplate" | "descricao" | "ativo">>) => void;
  marcarTemplateSalvo: (templateId: string, backendId: number) => void;

  criarAba: (templateId: string, nomeAba: string) => string;
  removerAba: (templateId: string, abaId: string) => void;
  atualizarAba: (templateId: string, abaId: string, patch: Partial<Pick<TemplateAba, "nomeAba" | "ativo">>) => void;
  moverAba: (templateId: string, abaId: string, direcao: -1 | 1) => void;

  criarCampo: (templateId: string, abaId: string, campo: Omit<TemplateCampo, "id" | "ordem">) => string;
  removerCampo: (templateId: string, abaId: string, campoId: string) => void;
  moverCampoParaAba: (templateId: string, origemAbaId: string, destinoAbaId: string, campoId: string) => void;
  atualizarCampo: (templateId: string, abaId: string, campoId: string, patch: Partial<Omit<TemplateCampo, "id">>) => void;
  moverCampo: (templateId: string, abaId: string, campoId: string, direcao: -1 | 1) => void;
  importarCamposDeExcel: (templateId: string, abaId: string, nomesColunas: string[]) => void;

  criarMapeamento: (templateId: string, nome: string) => string;
  removerMapeamento: (mapeamentoId: string) => void;
  definirLinhasMapeamento: (mapeamentoId: string, linhas: CronogramaMapeamentoCampo[]) => void;

  criarModelo: (templateId: string, nome: string, arquivoNomeOriginal: string | null, arquivoToken: string | null) => string;
  removerModelo: (modeloId: string) => void;
  marcarModeloSalvo: (modeloId: string, backendId: number) => void;
  criarAbaModelo: (modeloId: string, templateAbaId: string, nomeAbaFisica: string) => string;
  atualizarAbaModelo: (modeloId: string, abaModeloId: string, patch: Partial<Pick<ExcelModeloAba, "nomeAbaFisica" | "linhaCabecalho" | "colunaInicial">>) => void;
  definirCamposAbaModelo: (modeloId: string, abaModeloId: string, campos: ExcelModeloCampo[]) => void;

  criarControle: (templateId: string, nome: string) => string;
  removerControle: (controleId: string) => void;
  criarAbaControle: (controleId: string, templateAbaId: string) => string;
  removerAbaControle: (controleId: string, abaControleId: string) => void;
  atualizarAbaControle: (controleId: string, abaControleId: string, patch: Partial<Pick<ExcelControleAba, "nomeAbaExibido">>) => void;
  definirCamposAbaControle: (controleId: string, abaControleId: string, campos: ExcelControleCampo[]) => void;

  // Estrutura do Projeto (Projeto Único x Projeto por Fase) — rodada 3.
  projetosConfiguracao: ProjetoConfiguracao[];
  obterOuCriarProjetoConfiguracao: (projectIdAcc: string, nomeProjetoExibicao: string) => ProjetoConfiguracao;
  atualizarProjetoConfiguracao: (projectIdAcc: string, patch: Partial<Pick<ProjetoConfiguracao, "modoEstrutura" | "modoGeracaoFase" | "templateId" | "cronogramaMapeamentoId" | "excelEspelhoId" | "excelControleId">>) => void;
  marcarProjetoConfiguracaoSalva: (projectIdAcc: string) => void;
  criarFase: (projectIdAcc: string, codigo: string, nome: string) => string;
  removerFase: (projectIdAcc: string, faseId: string) => void;
  atualizarFase: (projectIdAcc: string, faseId: string, patch: Partial<Pick<ProjetoFase, "codigo" | "nome" | "ativa" | "operadorLogico">>) => void;
  moverFase: (projectIdAcc: string, faseId: string, direcao: -1 | 1) => void;
  criarRegra: (projectIdAcc: string, faseId: string) => string;
  removerRegra: (projectIdAcc: string, faseId: string, regraId: string) => void;
  atualizarRegra: (projectIdAcc: string, faseId: string, regraId: string, patch: Partial<Omit<ProjetoFaseRegra, "id">>) => void;
}

const LdpConfigContext = createContext<LdpConfigState | null>(null);

export const LdpConfigProvider = ({ children }: { children: ReactNode }) => {
  const [templates, setTemplates] = useState<LdpTemplate[]>([]);
  const [mapeamentos, setMapeamentos] = useState<CronogramaMapeamento[]>([]);
  const [modelos, setModelos] = useState<ExcelModelo[]>([]);
  const [controles, setControles] = useState<ExcelControleConfig[]>([]);
  const [projetosConfiguracao, setProjetosConfiguracao] = useState<ProjetoConfiguracao[]>([]);

  const criarTemplate = useCallback(
    (nome: string, descricao: string, origem: OrigemCriacaoTemplate, baseParaClone?: string) => {
      const id = novoId();
      const base = baseParaClone ? templates.find((t) => t.id === baseParaClone) : undefined;
      const abasClonadas: TemplateAba[] = base
        ? base.abas.map((aba) => ({
            ...aba,
            id: novoId(),
            campos: aba.campos.map((c) => ({ ...c, id: novoId() })),
          }))
        : [];
      setTemplates((prev) => [
        ...prev,
        { id, nomeTemplate: nome, descricao, origemCriacao: origem, ativo: true, abas: abasClonadas },
      ]);
      return id;
    },
    [templates]
  );

  const removerTemplate = useCallback((templateId: string) => {
    setTemplates((prev) => prev.filter((t) => t.id !== templateId));
    setMapeamentos((prev) => prev.filter((m) => m.templateId !== templateId));
    setModelos((prev) => prev.filter((m) => m.templateId !== templateId));
  }, []);

  const atualizarTemplate = useCallback(
    (templateId: string, patch: Partial<Pick<LdpTemplate, "nomeTemplate" | "descricao" | "ativo">>) => {
      setTemplates((prev) => prev.map((t) => (t.id === templateId ? { ...t, ...patch } : t)));
    },
    []
  );

  const marcarTemplateSalvo = useCallback((templateId: string, backendId: number) => {
    setTemplates((prev) => prev.map((t) => (t.id === templateId ? { ...t, backendId } : t)));
  }, []);

  const criarAba = useCallback((templateId: string, nomeAba: string) => {
    const id = novoId();
    setTemplates((prev) =>
      prev.map((t) =>
        t.id === templateId
          ? { ...t, abas: [...t.abas, { id, nomeAba, ordem: t.abas.length, ativo: true, campos: [] }] }
          : t
      )
    );
    return id;
  }, []);

  const removerAba = useCallback((templateId: string, abaId: string) => {
    setTemplates((prev) =>
      prev.map((t) => (t.id === templateId ? { ...t, abas: t.abas.filter((a) => a.id !== abaId) } : t))
    );
  }, []);

  const atualizarAba = useCallback(
    (templateId: string, abaId: string, patch: Partial<Pick<TemplateAba, "nomeAba" | "ativo">>) => {
      setTemplates((prev) =>
        prev.map((t) =>
          t.id === templateId
            ? { ...t, abas: t.abas.map((a) => (a.id === abaId ? { ...a, ...patch } : a)) }
            : t
        )
      );
    },
    []
  );

  const moverAba = useCallback((templateId: string, abaId: string, direcao: -1 | 1) => {
    setTemplates((prev) =>
      prev.map((t) => {
        if (t.id !== templateId) return t;
        const ordenadas = [...t.abas].sort((a, b) => a.ordem - b.ordem);
        const idx = ordenadas.findIndex((a) => a.id === abaId);
        const destino = idx + direcao;
        if (idx === -1 || destino < 0 || destino >= ordenadas.length) return t;
        [ordenadas[idx], ordenadas[destino]] = [ordenadas[destino], ordenadas[idx]];
        return { ...t, abas: ordenadas.map((a, i) => ({ ...a, ordem: i })) };
      })
    );
  }, []);

  const criarCampo = useCallback((templateId: string, abaId: string, campo: Omit<TemplateCampo, "id" | "ordem">) => {
    const id = novoId();
    setTemplates((prev) =>
      prev.map((t) =>
        t.id === templateId
          ? {
              ...t,
              abas: t.abas.map((a) =>
                a.id === abaId ? { ...a, campos: [...a.campos, { ...campo, id, ordem: a.campos.length }] } : a
              ),
            }
          : t
      )
    );
    return id;
  }, []);

  const removerCampo = useCallback((templateId: string, abaId: string, campoId: string) => {
    setTemplates((prev) =>
      prev.map((t) =>
        t.id === templateId
          ? { ...t, abas: t.abas.map((a) => (a.id === abaId ? { ...a, campos: a.campos.filter((c) => c.id !== campoId) } : a)) }
          : t
      )
    );
  }, []);

  const moverCampoParaAba = useCallback((templateId: string, origemAbaId: string, destinoAbaId: string, campoId: string) => {
    if (origemAbaId === destinoAbaId) return;
    setTemplates((prev) =>
      prev.map((t) => {
        if (t.id !== templateId) return t;
        const origem = t.abas.find((a) => a.id === origemAbaId);
        const campo = origem?.campos.find((c) => c.id === campoId);
        if (!campo) return t;
        return {
          ...t,
          abas: t.abas.map((a) => {
            if (a.id === origemAbaId) {
              return { ...a, campos: a.campos.filter((c) => c.id !== campoId).map((c, i) => ({ ...c, ordem: i })) };
            }
            if (a.id === destinoAbaId) {
              return { ...a, campos: [...a.campos, { ...campo, ordem: a.campos.length }] };
            }
            return a;
          }),
        };
      })
    );
  }, []);

  const atualizarCampo = useCallback(
    (templateId: string, abaId: string, campoId: string, patch: Partial<Omit<TemplateCampo, "id">>) => {
      setTemplates((prev) =>
        prev.map((t) =>
          t.id === templateId
            ? {
                ...t,
                abas: t.abas.map((a) =>
                  a.id === abaId
                    ? { ...a, campos: a.campos.map((c) => (c.id === campoId ? { ...c, ...patch } : c)) }
                    : a
                ),
              }
            : t
        )
      );
    },
    []
  );

  const moverCampo = useCallback((templateId: string, abaId: string, campoId: string, direcao: -1 | 1) => {
    setTemplates((prev) =>
      prev.map((t) => {
        if (t.id !== templateId) return t;
        return {
          ...t,
          abas: t.abas.map((a) => {
            if (a.id !== abaId) return a;
            const ordenados = [...a.campos].sort((x, y) => x.ordem - y.ordem);
            const idx = ordenados.findIndex((c) => c.id === campoId);
            const destino = idx + direcao;
            if (idx === -1 || destino < 0 || destino >= ordenados.length) return a;
            [ordenados[idx], ordenados[destino]] = [ordenados[destino], ordenados[idx]];
            return { ...a, campos: ordenados.map((c, i) => ({ ...c, ordem: i })) };
          }),
        };
      })
    );
  }, []);

  const importarCamposDeExcel = useCallback((templateId: string, abaId: string, nomesColunas: string[]) => {
    setTemplates((prev) =>
      prev.map((t) =>
        t.id === templateId
          ? {
              ...t,
              abas: t.abas.map((a) => {
                if (a.id !== abaId) return a;
                const base = a.campos.length;
                const novos: TemplateCampo[] = nomesColunas.map((nome, i) => ({
                  id: novoId(),
                  nomeExibido: nome,
                  chaveCampo: nome
                    .trim()
                    .toUpperCase()
                    .replace(/[^A-Z0-9]+/g, "_")
                    .replace(/^_+|_+$/g, "") || `CAMPO_${base + i + 1}`,
                  tipoDado: "TEXTO",
                  obrigatorio: false,
                  ordem: base + i,
                  origem: "MANUAL",
                  ativo: true,
                }));
                return { ...a, campos: [...a.campos, ...novos] };
              }),
            }
          : t
      )
    );
  }, []);

  const criarMapeamento = useCallback((templateId: string, nome: string) => {
    const id = novoId();
    setMapeamentos((prev) => [...prev, { id, templateId, nomeMapeamento: nome, ativo: true, campos: [] }]);
    return id;
  }, []);

  const removerMapeamento = useCallback((mapeamentoId: string) => {
    setMapeamentos((prev) => prev.filter((m) => m.id !== mapeamentoId));
  }, []);

  const definirLinhasMapeamento = useCallback((mapeamentoId: string, linhas: CronogramaMapeamentoCampo[]) => {
    setMapeamentos((prev) => prev.map((m) => (m.id === mapeamentoId ? { ...m, campos: linhas } : m)));
  }, []);

  const criarModelo = useCallback((templateId: string, nome: string, arquivoNomeOriginal: string | null, arquivoToken: string | null) => {
    const id = novoId();
    setModelos((prev) => [...prev, { id, templateId, nomeModelo: nome, arquivoNomeOriginal, arquivoToken, abas: [] }]);
    return id;
  }, []);

  const removerModelo = useCallback((modeloId: string) => {
    setModelos((prev) => prev.filter((m) => m.id !== modeloId));
  }, []);

  const marcarModeloSalvo = useCallback((modeloId: string, backendId: number) => {
    setModelos((prev) => prev.map((m) => (m.id === modeloId ? { ...m, backendId } : m)));
  }, []);

  const criarAbaModelo = useCallback((modeloId: string, templateAbaId: string, nomeAbaFisica: string) => {
    const id = novoId();
    setModelos((prev) =>
      prev.map((m) =>
        m.id === modeloId
          ? {
              ...m,
              abas: [
                ...m.abas,
                { id, templateAbaId, nomeAbaFisica, linhaCabecalho: 1, colunaInicial: 1, ordem: m.abas.length, campos: [] },
              ],
            }
          : m
      )
    );
    return id;
  }, []);

  const atualizarAbaModelo = useCallback(
    (modeloId: string, abaModeloId: string, patch: Partial<Pick<ExcelModeloAba, "nomeAbaFisica" | "linhaCabecalho" | "colunaInicial">>) => {
      setModelos((prev) =>
        prev.map((m) =>
          m.id === modeloId
            ? { ...m, abas: m.abas.map((a) => (a.id === abaModeloId ? { ...a, ...patch } : a)) }
            : m
        )
      );
    },
    []
  );

  const definirCamposAbaModelo = useCallback((modeloId: string, abaModeloId: string, campos: ExcelModeloCampo[]) => {
    setModelos((prev) =>
      prev.map((m) =>
        m.id === modeloId
          ? { ...m, abas: m.abas.map((a) => (a.id === abaModeloId ? { ...a, campos } : a)) }
          : m
      )
    );
  }, []);

  const criarControle = useCallback((templateId: string, nome: string) => {
    const id = novoId();
    setControles((prev) => [...prev, { id, templateId, nomeConfiguracao: nome, abas: [] }]);
    return id;
  }, []);

  const removerControle = useCallback((controleId: string) => {
    setControles((prev) => prev.filter((c) => c.id !== controleId));
  }, []);

  const criarAbaControle = useCallback((controleId: string, templateAbaId: string) => {
    const id = novoId();
    setControles((prev) =>
      prev.map((c) =>
        c.id === controleId
          ? { ...c, abas: [...c.abas, { id, templateAbaId, nomeAbaExibido: null, ordem: c.abas.length, campos: [] }] }
          : c
      )
    );
    return id;
  }, []);

  const removerAbaControle = useCallback((controleId: string, abaControleId: string) => {
    setControles((prev) =>
      prev.map((c) => (c.id === controleId ? { ...c, abas: c.abas.filter((a) => a.id !== abaControleId) } : c))
    );
  }, []);

  const atualizarAbaControle = useCallback(
    (controleId: string, abaControleId: string, patch: Partial<Pick<ExcelControleAba, "nomeAbaExibido">>) => {
      setControles((prev) =>
        prev.map((c) =>
          c.id === controleId
            ? { ...c, abas: c.abas.map((a) => (a.id === abaControleId ? { ...a, ...patch } : a)) }
            : c
        )
      );
    },
    []
  );

  const definirCamposAbaControle = useCallback((controleId: string, abaControleId: string, campos: ExcelControleCampo[]) => {
    setControles((prev) =>
      prev.map((c) =>
        c.id === controleId
          ? { ...c, abas: c.abas.map((a) => (a.id === abaControleId ? { ...a, campos } : a)) }
          : c
      )
    );
  }, []);

  // ------------------------------------------------------------------
  // Estrutura do Projeto (Projeto Único x Projeto por Fase) — rodada 3. Independente de
  // dbo.Projetos.Tipo_LDP; Project_ID_ACC é a chave real (não um uuid local).
  // ------------------------------------------------------------------

  const obterOuCriarProjetoConfiguracao = useCallback(
    (projectIdAcc: string, nomeProjetoExibicao: string): ProjetoConfiguracao => {
      const existente = projetosConfiguracao.find((p) => p.projectIdAcc === projectIdAcc);
      if (existente) return existente;
      const novo: ProjetoConfiguracao = {
        projectIdAcc,
        nomeProjetoExibicao,
        modoEstrutura: "PROJETO_UNICO",
        modoGeracaoFase: null,
        templateId: null,
        cronogramaMapeamentoId: null,
        excelEspelhoId: null,
        excelControleId: null,
        fases: [],
        salvoNoBanco: false,
      };
      setProjetosConfiguracao((prev) => [...prev, novo]);
      return novo;
    },
    [projetosConfiguracao]
  );

  const atualizarProjetoConfiguracao = useCallback(
    (
      projectIdAcc: string,
      patch: Partial<Pick<ProjetoConfiguracao, "modoEstrutura" | "modoGeracaoFase" | "templateId" | "cronogramaMapeamentoId" | "excelEspelhoId" | "excelControleId">>
    ) => {
      setProjetosConfiguracao((prev) => prev.map((p) => (p.projectIdAcc === projectIdAcc ? { ...p, ...patch } : p)));
    },
    []
  );

  const marcarProjetoConfiguracaoSalva = useCallback((projectIdAcc: string) => {
    setProjetosConfiguracao((prev) => prev.map((p) => (p.projectIdAcc === projectIdAcc ? { ...p, salvoNoBanco: true } : p)));
  }, []);

  const criarFase = useCallback((projectIdAcc: string, codigo: string, nome: string) => {
    const id = novoId();
    setProjetosConfiguracao((prev) =>
      prev.map((p) =>
        p.projectIdAcc === projectIdAcc
          ? { ...p, fases: [...p.fases, { id, codigo, nome, ordem: p.fases.length, ativa: true, operadorLogico: "ALL", regras: [] }] }
          : p
      )
    );
    return id;
  }, []);

  const removerFase = useCallback((projectIdAcc: string, faseId: string) => {
    setProjetosConfiguracao((prev) =>
      prev.map((p) => (p.projectIdAcc === projectIdAcc ? { ...p, fases: p.fases.filter((f) => f.id !== faseId) } : p))
    );
  }, []);

  const atualizarFase = useCallback(
    (projectIdAcc: string, faseId: string, patch: Partial<Pick<ProjetoFase, "codigo" | "nome" | "ativa" | "operadorLogico">>) => {
      setProjetosConfiguracao((prev) =>
        prev.map((p) =>
          p.projectIdAcc === projectIdAcc
            ? { ...p, fases: p.fases.map((f) => (f.id === faseId ? { ...f, ...patch } : f)) }
            : p
        )
      );
    },
    []
  );

  const moverFase = useCallback((projectIdAcc: string, faseId: string, direcao: -1 | 1) => {
    setProjetosConfiguracao((prev) =>
      prev.map((p) => {
        if (p.projectIdAcc !== projectIdAcc) return p;
        const ordenadas = [...p.fases].sort((a, b) => a.ordem - b.ordem);
        const idx = ordenadas.findIndex((f) => f.id === faseId);
        const destino = idx + direcao;
        if (idx === -1 || destino < 0 || destino >= ordenadas.length) return p;
        [ordenadas[idx], ordenadas[destino]] = [ordenadas[destino], ordenadas[idx]];
        return { ...p, fases: ordenadas.map((f, i) => ({ ...f, ordem: i })) };
      })
    );
  }, []);

  const criarRegra = useCallback((projectIdAcc: string, faseId: string) => {
    const id = novoId();
    setProjetosConfiguracao((prev) =>
      prev.map((p) =>
        p.projectIdAcc === projectIdAcc
          ? {
              ...p,
              fases: p.fases.map((f) =>
                f.id === faseId
                  ? { ...f, regras: [...f.regras, { id, campoOrigem: "", operador: "COMECA_COM", valorComparacao: "", ordem: f.regras.length, ativa: true }] }
                  : f
              ),
            }
          : p
      )
    );
    return id;
  }, []);

  const removerRegra = useCallback((projectIdAcc: string, faseId: string, regraId: string) => {
    setProjetosConfiguracao((prev) =>
      prev.map((p) =>
        p.projectIdAcc === projectIdAcc
          ? { ...p, fases: p.fases.map((f) => (f.id === faseId ? { ...f, regras: f.regras.filter((r) => r.id !== regraId) } : f)) }
          : p
      )
    );
  }, []);

  const atualizarRegra = useCallback(
    (projectIdAcc: string, faseId: string, regraId: string, patch: Partial<Omit<ProjetoFaseRegra, "id">>) => {
      setProjetosConfiguracao((prev) =>
        prev.map((p) =>
          p.projectIdAcc === projectIdAcc
            ? {
                ...p,
                fases: p.fases.map((f) =>
                  f.id === faseId
                    ? { ...f, regras: f.regras.map((r) => (r.id === regraId ? { ...r, ...patch } : r)) }
                    : f
                ),
              }
            : p
        )
      );
    },
    []
  );

  return (
    <LdpConfigContext.Provider
      value={{
        templates,
        mapeamentos,
        modelos,
        controles,
        projetosConfiguracao,
        criarTemplate,
        removerTemplate,
        atualizarTemplate,
        marcarTemplateSalvo,
        criarAba,
        removerAba,
        atualizarAba,
        moverAba,
        criarCampo,
        removerCampo,
        moverCampoParaAba,
        atualizarCampo,
        moverCampo,
        importarCamposDeExcel,
        criarMapeamento,
        removerMapeamento,
        definirLinhasMapeamento,
        criarModelo,
        removerModelo,
        marcarModeloSalvo,
        criarAbaModelo,
        atualizarAbaModelo,
        definirCamposAbaModelo,
        criarControle,
        removerControle,
        criarAbaControle,
        removerAbaControle,
        atualizarAbaControle,
        definirCamposAbaControle,
        obterOuCriarProjetoConfiguracao,
        atualizarProjetoConfiguracao,
        marcarProjetoConfiguracaoSalva,
        criarFase,
        removerFase,
        atualizarFase,
        moverFase,
        criarRegra,
        removerRegra,
        atualizarRegra,
      }}
    >
      {children}
    </LdpConfigContext.Provider>
  );
};

export const useLdpConfig = () => {
  const ctx = useContext(LdpConfigContext);
  if (!ctx) throw new Error("useLdpConfig deve ser usado dentro de <LdpConfigProvider>");
  return ctx;
};
