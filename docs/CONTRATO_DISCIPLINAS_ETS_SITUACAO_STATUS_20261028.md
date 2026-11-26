# Contrato: disciplinas de pacotes, Situation e Status (28/10/2026, Claude → Codex)

Mudanças só no backend. Nenhum componente visual foi alterado. Os campos são aditivos: nada foi removido nem renomeado.

## 1. Disciplinas: pacotes terceirizados (ETS) e AT manual

O endpoint existente `GET api/reports/gestao-vista`, nos campos `actual.byDiscipline` e `hhRealizado.disciplinas`, passa a somar o complemento dos pacotes na disciplina **com o nome da Norma do projeto**. É o mesmo nome usado no previsto (`documentos[].disciplinaCanonica`) e no realizado.

O mesmo vale para o AT manual do De/Para: quando o nome digitado equivale (pelo catálogo PT/EN) a um único nome da Norma, esse nome é usado.

BML011.016, antes:

| Disciplina | HH |
|---|---|
| PROCESS | 110 |
| PROCESSO | 69,85 |
| STEEL STRUCTURE | 130 |
| ESTRUTURA METALICA | 35,87 |
| PIPING | 111,5 |
| TUBULAÇÃO | 178,09 |

BML011.016, depois:

| Disciplina | HH |
|---|---|
| PROCESSO | 179,85 |
| ESTRUTURA METALICA | 165,87 |
| TUBULAÇÃO | 289,59 |
| CIVIL | 210,91 (190 ETS + 20,91 internos) |

O total ficou igual: 1.182,97 HH e R$ 107.936,59.

- O novo campo `actual.historicalMeasurement.documents[].normaDiscipline` traz o nome da Norma. `discipline` continua com o nome canônico do pacote, usado nas telas de Custos/Pacotes.
- O frontend **não** deve remapear PT/EN para unir disciplinas. Basta usar o nome recebido.

## 2. `consolidadoDocumental` (novo campo em `GET api/reports/gestao-vista`)

```jsonc
"consolidadoDocumental": {
  "disponivel": true,               // false em projeto sem LDP (sem Situation/Status oficiais)
  "fonte": "...",
  "linhasDocumentais": 97, "documentosUnicos": 97, "linhasRepetidasIgnoradas": 0,
  "situacao": {                     // SOMENTE campo Situation (Tipo 2: gate vigente)
    "total": 97, "emitidos": 69, "cancelados": 28, "aEmitir": 0,      // total = emitidos + cancelados + aEmitir
    "aEmitirPorValorOriginal": [{ "valor": "PENDING ISSUED", "documentos": 0 }]
  },
  "prazo": {                        // regra de prazo EXISTENTE (StatusDocumento), aplicada só aos aEmitir
    "atrasados": 0, "noPrazo": 0, "semPrazo": 0,
    "classificadosComoEmitidoOuCanceladoPeloStatus": 0   // aEmitir pela Situation, mas já emitido/cancelado pela regra antiga
  },                                // aEmitir = atrasados + noPrazo + semPrazo + classificados...
  "status": {                       // SOMENTE Document_Status (Tipo 2: do gate vigente)
    "total": 97,
    "categorias": [ { "chave": "APROVADO", "rotulo": "Aprovado", "documentos": 52,
                      "valoresOriginais": [{ "valor": "APROVADO", "documentos": 52 }] } ]
    // As 6 chaves aparecem sempre, em ordem: APROVADO, CANCELADO, EM_ANALISE_CLIENTE, INFORMATIVO,
    // ATENDIMENTO_COMENTARIOS, EM_ELABORACAO. Quando houver, aparecem também OUTROS e NAO_INFORMADO.
    // A soma das categorias é igual a status.total.
  },
  "diagnosticos": ["..."]
}
```

Regras:
- **Documento único:** número N&A da carga vigente da LDP. AT e linhas sem conteúdo documental ficam fora. Número repetido conta uma vez, pela revisão vigente.
- **Tipo 2:** vale o gate mais alto (90 → 60 → 30) ativo e aplicável. `NOT TO BE ISSUED` significa que o gate não se aplica.
- **Situation e Status não se misturam:**
  - `situacao` nunca lê Document_Status.
  - `status` nunca lê Situation.
  - A regra antiga de emissão (`documentos[].statusDocumento`, robô 4B) não mudou.
