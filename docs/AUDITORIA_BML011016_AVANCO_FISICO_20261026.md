# Auditoria — BML011.016 | Avanço físico (26/10/2026)

## Escopo e regra vigente
Projeto ACC `b.9907135f-ff67-43bb-887b-20c3a7f75807`, Tipo LDP 1. **Nenhuma modificação nos pesos de status nem nos valores da LDP foi efetuada durante a auditoria.**
Fonte: `Services/Reports/PmoAvanco.cs` (Tipo1), `Services/Reports/PmoExecutiveData.cs`, `qualidade.DocumentStatusAvancoConfig`, `dbo.VW_Ldp_Unificada`.

A regra efetiva do Resumo não usa HH realizadas como avanço físico.
1. Exclui linhas com `doc4 = AT` e documentos cancelados. Exige `Planned_HH > 0`.
2. Resolve o status documental em `DocumentStatusCanonical.Key(Document_Status)` e procura o peso ativo do projeto `qualidade.DocumentStatusAvancoConfig`.
3. Quando não há peso definido ou há conflito, `AvancoFisico = null`. No agregado, essa linha elegível **permanece no denominador mas contribui com zero no numerador**.
4. Avanço = `Σ(Planned_HH × AvancoFisico/100) / Σ(Planned_HH elegível) × 100`.
5. Percentuais da LDP (`Percent_Physical_Completed`) são mantidos como diagnóstico: na regra Tipo 1, a configuração de status do planejador tem prioridade.

## Resultado da conferência SQL READ-ONLY
- 109 linhas na `dbo.VW_Ldp_Unificada`, incluindo cancelados e atividades. HH planejadas brutas: **1.083,8388**.
- 71 documentos elegíveis após desconsiderar cancelados e atividades `AT`: **807,36 HH** ponderáveis.
- Apenas 3 status com peso ativo: `APROVADO=100%`, `CANCELADO=100%`, `ELABORANDO=100%`. O peso CANCELADO não entra no agregado físico pela regra de exclusão.
- **12 documentos elegíveis sem configuração de status**, somando **192,07 HH**, ou aproximadamente **23,79%** do denominador físico.
  - 7 em `ATENDIMENTO A COMENTÁRIOS`: 142,8836 HH.
  - 4 `INFORMATIVO`: 48,0000 HH.
  - 1 `EM ANÁLISE DO CLIENTE`: 1,1856 HH.
- Reproduzindo por SQL a ponderação dos três pesos ativos: **76,21%**. É uma simulação do agregado conforme os campos atuais da view e deve ser confrontada com a resposta autenticada da API, inclusive snapshot corrente e classificações canônicas.
- Usar diretamente os percentuais de origem da LDP em todos os documentos elegíveis daria aproximadamente **63,65%**, não um aumento. A origem LDP não deve substituir automaticamente a regra gerencial.
- Se todos os status sem configuração fossem tratados como 100%, o resultado subiria para 100%; **isso seria injustificado e não foi feito**. A alocação de pesos reais precisa ser validada pelo planejamento.

## Evidências por número de documento
- `BML011.016-212-EL4-DE-003`: Atendimento a comentários, 17,70 HH planejadas, físico LDP 66,67%, mas sem peso gerencial configurado — conta como 0 na soma efetiva.
- `BML011.016-212-EL3-DE-001`: Atendimento a comentários, 16,73 HH, físico LDP 66,67%, idem.
- `BML011.016-212-STE-3D-001`: Em análise do cliente, 1,19 HH, físico LDP 100%, mas sem peso configurado.
- `BML011.016-212-3DM-3D-001` e `...002`: Informativo, 12 HH cada, físico LDP 100%, mas sem peso configurado.
- `BML011.016-212-PL1-LD-001` e `...002`: Informativo, 12 HH cada, físico da LDP nulo; não se pode inferir conclusão.

## Divergência de escopo entre LDP atualizada e sistema (análise separada)
- Excel atualizado = 105 itens, 1.043,8388 HH planejadas.
- View atual = 109 itens, 1.083,8388 HH. Quatro itens extras totalizam 40 HH: PL1-LD-001 (12h), PL1-LD-002 (12h), PI0-AT-001 (8h), PX0-AT-001 (8h).
- As HH planejadas dos 105 códigos comuns coincidem; 1 item IA4-DE-001 possui 4,25 HH realizadas a mais no Excel (23,83h) do que na LDP atual do sistema (19,58h). Isso **não prova** problema na fórmula do avanço físico.
- Timesheet documental corrigido BML011.016 = 697,22 HH; `VW_Ldp_Unificada.Actual_HH` = 651,47 HH, diferença = 45,75 HH entre fontes. Não somar automaticamente.

## Ações seguras
- Conferir com João/Planejamento os percentuais esperados para `ATENDIMENTO A COMENTÁRIOS`, `INFORMATIVO`, `EM ANÁLISE DO CLIENTE`, e se Informativo integra ou não o indicador físico oficial.
- Verificar os quatro códigos extras no sistema antes de qualquer atualização da LDP, pois sua exclusão também altera o escopo.
- Homologar resultado do endpoint Resumo autenticado comparando com a regra atual; aplicar alteração somente depois da validação dos pesos e universo elegível, mantendo rastreabilidade de antes/depois.
