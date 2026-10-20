# Fundação visual de planejamento

Dois módulos distintos: `WeeklyPlanningPage` para disciplina/Job Leader e `PlannerDashboardPage` para planejador. Exportados em `index.ts`; caminhos sugeridos `/planejamento/programacao-semanal` e `/planejamento/painel-do-planejador`. Integração no router/sidebar propositalmente pendente. Não existem novas regras de autenticação. `FUTURE_MODULE_ACCESS` documenta papéis futuros e não é uma barreira de segurança.

## Demonstração

As páginas usam uma store em memória comum, sem localStorage e sem gravação remota. Recarregar reinicia os dados fictícios. O projeto, os números dos documentos, o aviso permanente e o XLSX identificam DEMO. Abra o painel, selecione projeto/semana ou abra uma nova semana e escolha disciplinas esperadas. Na programação selecione documentos, informe responsável, data e HH semanal (opcional), salve e envie. No painel, devolva com motivo, comente ou aprove. ENVIADO/APROVADO bloqueiam edição também na store. DEVOLVIDO permite correção; histórico e motivo são preservados. Não há reabertura de aprovado nesta fase.

O prazo usa timestamp com fuso e exibição no horário local do navegador. Atraso é derivado a cada 30 segundos para RASCUNHO após o prazo; DEVOLVIDO mantém seu estado. Envio depois do prazo continua possível e registra a data no histórico. Pendentes incluem rascunhos e devolvidas; atrasadas são subconjunto, não uma categoria aditiva. HH vazio permanece desconhecido, não assume HH total. Indicadores somam valores conhecidos e informam a quantidade sem HH.

## Consulta real separada

`WeeklyPlanningPage` aceita opcionalmente `source={{ hubId, projectId, start, end }}`. O painel adicional usa GET `/api/weekly-planning/documents` e a autenticação existente (`getJson`). Os documentos reais são somente leitura, paginados e identificados como referências de snapshot. Nunca são adicionados à store demo. Os UUIDs devem vir do contexto de projeto existente quando o módulo for integrado. A identidade canônica está pendente de decisão backend.

## Exportação e evolução

`planningExport.ts` gera arquivo XLSX de saída com todos os documentos selecionados e respectivos estados, inclusive rascunhos, metadados de período e indicação explícita de preview. Não importa planilhas nem persiste dados. O contrato de eventos contempla os seis nomes solicitados; a demo emite abertura, envio, devolução e aprovação. Aproximação de prazo e atraso aguardam agendamento/idempotência backend; nenhuma notificação externa é enviada.

Persistência, concorrência entre usuários, autoria auditável, permissões, revisões oficiais e seleção de fonte canônica dependem da próxima fase. O serviço de demonstração deve ser substituído por comandos autenticados/transacionais após essa integração, preservando os limites entre os módulos.

## Validação

Preview isolado com Vite: acesse `/src/features/planning/preview.html`. As abas deste preview não integram o router/sidebar do produto. Os testes `.test.cjs` não são importados por nenhum arquivo de produção.

A demo usa horário local do navegador como fuso da semana, limita o período a 1–7 dias e não aceita prazo após a data final. Histórico guarda cópia dos documentos selecionados em cada ação e distingue atores DEMO; expandir uma entrada permite comparar dados enviados antes e depois de uma devolução. Observações/comentários têm limite de 2000 caracteres; HH semanal respeita a escala de 6 casas e magnitude decimal(18,6), sujeito à precisão numérica do JavaScript. A persistência deve usar decimal exato no servidor.

Da pasta `ClientApp`: `node node_modules/typescript/bin/tsc --noEmit --pretty false` e `node --test src/features/planning/tests/workflow.test.cjs`. Os testes exercitam transições, congelamento, validação, prazo, abertura de semana e isolamento do HH semanal.
