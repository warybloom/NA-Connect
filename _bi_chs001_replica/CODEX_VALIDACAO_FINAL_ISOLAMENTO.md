<!-- Mensagem final do Codex (codex exec -s read-only, 15/10/2026 23:36–23:42), salva sem alteração pelo Claude porque o sandbox do Codex era somente leitura. Respostas: RELATORIO_FINAL_IMPLEMENTACAO_BI_CHS001_20261015.md §12. -->

# Validação final de isolamento — BI CHS001

**Data da auditoria:** 15/10/2026  
**Modo:** somente leitura  
**Repositório:** `C:\Dev\N-A-Connect aa\N-A-Connect`  
**Banco informado:** `.\SQLEXPRESS / DB_NA_CONNECT`  
**Parecer:** **NÃO APROVADO para disponibilização integral das dez telas sem ressalvas.**

O isolamento estrutural foi bem implementado e as evidências disponíveis indicam que os objetos legados não foram alterados. Entretanto, há divergências materiais na série timephased, principalmente na linha de base, além de o backend publicado ainda ser o binário antigo. O build integral do checkout principal também permanece quebrado pela sonda `mpp_probe`.

## 1. Sumário executivo

| Gravidade | Quantidade |
|---|---:|
| Crítico | 1 |
| Alto | 3 |
| Médio | 5 |
| Baixo | 2 |

Conclusões principais:

- O schema `bi_chs001` está efetivamente isolado.
- O inventário antes/depois não mostra alterações em objetos, colunas ou dados legados monitorados.
- A versão 1 contém 883 tarefas, 755 folhas, hierarquia completa e as contagens esperadas por gate: 196/347/190.
- A série real diária fecha com o total de trabalho real do MPP dentro de pequena tolerância global, mas **não reproduz integralmente o `AssignmentByDay` do PBIX**.
- A série de linha de base é materialmente inconsistente: 220 folhas divergentes e excesso global de aproximadamente 605,19 horas.
- O backend em `5277` ainda executa o assembly antigo; portanto, o código auditado não está disponível na API viva.
- Não há evidência registrada de `RESTORE VERIFYONLY` e `RESTORE HEADERONLY` do `.bak`.
- Os testes direcionados não puderam ser reexecutados nesta sessão porque o ambiente recusou a inicialização de processos sob a política de aprovação `never`. As conclusões dinâmicas abaixo usam os logs e artefatos já existentes.

---

## 2. Achados

### [CRÍTICO] C01 — Backend vivo ainda não contém o módulo auditado

**Arquivo/linha:** `Scripts/Deploy-BiChs001Isolado-20261015.ps1:1-80`  
**Componente operacional:** backend em `5277`

**Evidência**

O estado fornecido para esta auditoria informa que o processo em `5277` ainda executa o binário antigo, datado de 16:14. O script de implantação foi preparado, mas não executado.

O frontend tenta usar:

- `/api/bi-chs001/schedule/tarefas`
- `/api/bi-chs001/schedule/trabalho-diario`
- `/api/bi-chs001/schedule/trabalho-semanal`

Essas actions só existem no novo `BiChs001ScheduleController`. Enquanto o assembly antigo continuar carregado, a fonte isolada não estará operacional e o frontend poderá cair na alternativa legada.

**Impacto**

- As dez telas não estão exercitando o código e os dados isolados validados.
- Curvas, matrizes e campos nativos do MPP podem permanecer indisponíveis.
- A versão 1 existente no banco não é suficiente para ativar a funcionalidade sem o controlador correspondente.
- Testes sobre classes instanciadas diretamente não provam a publicação real das rotas.

**Correção sugerida**

Executar o deploy controlado apenas após resolver ou aceitar formalmente os achados altos deste relatório. Depois da troca:

1. confirmar hash do assembly;
2. confirmar que `Worker__RunOnStartup=false`;
3. testar anonimato `401`;
4. testar usuário autorizado nas quatro rotas GET;
5. testar `403` sem `robot.run`;
6. testar leitura explícita da `versao=1`;
7. confirmar que tarefas e série retornam o mesmo `versaoId`.

---

### [ALTO] A01 — Série timephased da linha de base não concilia com o total das tarefas

**Arquivo/linha:** `Services/BiChs001Schedule/BiChs001MppReader.cs:91-98,122-133,145-150`  
**Dados:** `inventario/sync_acc_producao.log`; `inventario/validacao_producao.txt`

**Evidência**

A leitura gravada em produção registra:

- `FolhasComSerieDivergenteDoTotalLinhaBase = 220`
- soma da linha de base nas folhas: `10418.740000 h`
- soma da série diária de linha de base: `11023.933441 h`

Diferença:

```text
11023,933441 - 10418,740000 = 605,193441 horas
```

Isto representa aproximadamente **5,81% acima** do total de linha de base das folhas.

O leitor chama `getTimephasedBaselineWork(0)` para cada atribuição e redistribui `getTotalAmount()` segundo o calendário efetivo. O diagnóstico detecta a divergência, mas não a transforma em aviso: os avisos adicionados ao resultado cobrem ausência de série real e blocos sem hora útil, não a divergência da linha de base.

**Impacto**

As medidas de baseline semanal, curvas S, desvios e percentuais derivados da série podem apresentar valores incorretos, ainda que os totais e datas das tarefas estejam corretos.

**Correção sugerida**

- Bloquear a promoção de uma versão quando a soma da série de LB divergir do total das folhas além de tolerância documentada.
- Investigar sobreposição de blocos retornados por MPXJ, séries de resumo, calendários de atribuição e interpretação de `TimephasedWork.getTotalAmount()`.
- Conciliar por `UID_Tarefa` e semana, não apenas por soma global.
- Adicionar teste que exija zero divergências de LB ou uma lista explícita e justificada de exceções.
- Incluir `FolhasComSerieDivergenteDoTotalLinhaBase` e a diferença global em `Diagnostico.Avisos`.

---

### [ALTO] A02 — Série semanal real não tem paridade integral com `AssignmentByDay` do PBIX

**Arquivo/linha:** `_bi_chs001_replica/conciliacao_isolado_pbix_20261015.json:119-181`  
**Código relacionado:** `Services/BiChs001Schedule/BiChs001MppReader.cs:91-113,155-187`; `ClientApp/src/features/production/biChs001/dados/entidades.ts:111-127`

**Evidência**

A conciliação registra:

- pares no PBIX: `2010`
- pares não nulos no MPP: `2229`
- pares apenas no MPP: `219`
- soma PBIX: `7569.337046 h`
- soma MPP nas mesmas tarefas: `7798.856827 h`

Diferença:

```text
7798,856827 - 7569,337046 = 229,519781 horas
```

Os 2010 pares presentes no PBIX coincidem após arredondamento a duas casas, mas existem 219 pares semanais adicionais no resultado MPXJ.

Isso demonstra que a fórmula de agregação dominical está coerente para os pares compartilhados, mas o conjunto de períodos incluídos não é equivalente ao `AssignmentByDay` usado pelo PBIX.

**Impacto**

As curvas vivas poderão divergir do PBIX mesmo com o mesmo MPP v47. A alegação de paridade não pode ser estendida da fórmula do motor para a fonte isolada efetivamente gravada.

**Correção sugerida**

- Classificar os 219 pares extras por tarefa, data, tipo de trabalho e gate.
- Verificar se o PBIX exclui períodos zerados, períodos fora do status date, trabalho futuro, assignments inativos ou registros de baseline específicos.
- Criar uma comparação automatizada por `(TaskUID, domingo)`.
- Não declarar paridade de `AssignmentByDay` até que os pares adicionais sejam explicados ou filtrados conforme a semântica real do PBIX.

---

### [ALTO] A03 — Build do checkout principal está contaminado pela sonda `mpp_probe`

**Arquivo/linha:** `_bi_chs001_replica/auditoria_codex_independente/mpp_probe/Program.cs:1`  
**Diretórios relacionados:** `mpp_probe/bin`, `mpp_probe/obj`

**Evidência**

O próprio prompt registra que essa sonda faz o `dotnet build` do site falhar com `CS8802` e atributos duplicados. O diretório ainda contém:

- `Program.cs`
- `ExtratorMpp.csproj`
- `bin/`
- `obj/`

Como o projeto principal usa inclusão glob de fontes, o arquivo de programa de nível superior e fontes geradas sob `obj` podem ser incorporados indevidamente.

**Impacto**

- O checkout principal não possui build limpo e reproduzível.
- Falhas reais do módulo podem ficar escondidas pelo ruído da sonda.
- Pipelines ou builds locais que não usam uma cópia saneada falharão.

**Correção sugerida**

Mover a sonda para fora da árvore do projeto ou excluir explicitamente de `Compile`, `Content` e `None`:

```xml
<ItemGroup>
  <Compile Remove="_bi_chs001_replica\auditoria_codex_independente\mpp_probe\**\*.cs" />
  <Content Remove="_bi_chs001_replica\auditoria_codex_independente\mpp_probe\**\*" />
  <None Remove="_bi_chs001_replica\auditoria_codex_independente\mpp_probe\**\*" />
</ItemGroup>
```

Remover `bin/obj` da sonda por operação controlada e incluí-los no ignore adequado. Em seguida, repetir o build do checkout principal com saída fora do repositório.

---

### [MÉDIO] M01 — A migração registra sucesso antes de criar as views

**Arquivo/linha:** `Migrations/BiChs001/001_bi_chs001_UP.sql:121-168`

**Evidência**

A inserção em `bi_chs001.Migracao` e a criação das tabelas são confirmadas por `COMMIT TRANSACTION` antes do primeiro `GO`. As três views são criadas depois, por `CREATE OR ALTER VIEW`.

Se a criação de qualquer view falhar:

- a migração `001` já estará registrada;
- as tabelas e o alias já estarão confirmados;
- o schema ficará parcialmente instalado.

**Impacto**

`SchemaExisteAsync` verifica somente `bi_chs001.Versao_Cronograma`. Assim, um schema parcial pode ser declarado instalado e falhar posteriormente em `VW_Tarefa` ou `VW_Trabalho_Semana`.

**Correção sugerida**

- Criar as views antes de registrar a versão da migração, ou usar SQL dinâmico para manter tudo em uma transação.
- Fazer `SchemaExisteAsync` validar todos os objetos obrigatórios.
- Registrar `Migracao 001` apenas após as três views existirem.
- Adicionar teste de falha intencional durante a criação de view e confirmar rollback integral.

---

### [MÉDIO] M02 — Índice de alias ultrapassa o limite seguro de chave do SQL Server

**Arquivo/linha:** `Migrations/BiChs001/001_bi_chs001_UP.sql:101-103`  
**Evidência dinâmica:** `inventario/teste_migracao_clone.log`

**Evidência**

O índice único usa:

```sql
(Project_ID_ACC nvarchar(100), Campo nvarchar(50), Valor_Bruto nvarchar(400))
```

O SQL Server registrou:

```text
O índice 'UX_bi_chs001_Alias' tem um comprimento máximo de 2300 bytes.
Para combinação de valores maiores, a operação inserir/atualizar apresentará uma falha.
```

**Impacto**

A linha atual é curta e funciona, mas o esquema permite inserir alias válidos segundo as colunas que não poderão ser indexados.

**Correção sugerida**

Usar uma coluna hash persistida para unicidade, reduzir limites ou criar unicidade sobre identificador/hash adequado. Exemplo conceitual:

```sql
Valor_Bruto_Hash AS CONVERT(binary(32), HASHBYTES('SHA2_256', Valor_Bruto)) PERSISTED
```

e indexar projeto, campo e hash, mantendo verificação contra colisão na aplicação.

---

### [MÉDIO] M03 — Falhas do POST não seguem contrato consistente de indisponibilidade

**Arquivo/linha:** `Controllers/BiChs001ScheduleController.cs:83-94`

**Evidência**

As actions GET capturam `SqlException` e retornam `503`. O POST:

```csharp
var r = await servico.SincronizarDoAccAsync(...);
return r.Sucesso ? Ok(r) : StatusCode(502, r);
```

não possui `try/catch`. Exceções de SQL, MPXJ, filesystem ou Autodesk podem escapar como `500`. Falhas funcionais são sempre convertidas em `502`, mesmo quando a causa é schema ou banco indisponível.

Os testes de contrato validam bloqueios de autenticação, mas não cobrem exceção SQL/ACC/MPXJ no POST.

**Impacto**

Contrato HTTP inconsistente e diagnóstico operacional impreciso.

**Correção sugerida**

- Mapear falha de dependência externa para `502`;
- indisponibilidade SQL/schema para `503`;
- entrada inválida para `400`;
- arquivo MPP inválido para `422`;
- exceção inesperada para resposta sanitizada `500`;
- adicionar testes para cada caminho.

---

### [MÉDIO] M04 — `502` da fonte principal é tratado como “endpoint não publicado” e aciona fallback legado

**Arquivo/linha:** `ClientApp/src/features/production/biChs001/dados/useDadosBi.ts:14-20,43-51`

**Evidência**

`lerFonte` transforma tanto `404` quanto `502` em:

```text
endpoint do cronograma isolado do BI ainda não publicado
```

e, quando existe `endpointAlternativo`, carrega silenciosamente a fonte legada.

Um `502` normalmente significa que a rota existe, mas uma dependência falhou. Isso é diferente de controlador não publicado.

**Impacto**

- Uma falha do serviço isolado pode ficar mascarada.
- O usuário recebe dados legados com semântica e campos diferentes.
- Pode haver regressão silenciosa justamente após o deploy.

**Correção sugerida**

Permitir fallback automático somente para `404` que identifique inequivocamente rota ausente. Para `502/503`, exibir erro da fonte isolada e exigir ação explícita do usuário para usar o legado.

---

### [MÉDIO] M05 — Verificação formal do backup não está documentada

**Arquivo:** `C:\Dev\N-A-Connect aa\_backups\bi_chs001_isolado_20261015\DB_NA_CONNECT_antes_bi_chs001_20261015_2245.bak`  
**Inventário:** `C:\Dev\N-A-Connect aa\_backups\bi_chs001_isolado_20261015\inventario\`

**Evidência**

O `.bak` existe, e o clone foi usado para testar UP/DOWN. Contudo, no inventário lido não há saída identificável de:

```sql
RESTORE VERIFYONLY
RESTORE HEADERONLY
```

O teste do clone é evidência positiva de restaurabilidade prática, mas não substitui o registro explícito pedido pela auditoria.

**Impacto**

Lacuna de rastreabilidade do backup e dos metadados de restore.

**Correção sugerida**

Executar e arquivar, em sessão autorizada:

```sql
RESTORE VERIFYONLY
FROM DISK = N'...\DB_NA_CONNECT_antes_bi_chs001_20261015_2245.bak'
WITH CHECKSUM;

RESTORE HEADERONLY
FROM DISK = N'...\DB_NA_CONNECT_antes_bi_chs001_20261015_2245.bak';
```

Não executar restore sobre produção.

---

### [BAIXO] B01 — Divergências importantes do leitor não geram aviso no resultado

**Arquivo/linha:** `Services/BiChs001Schedule/BiChs001MppReader.cs:122-150`

**Evidência**

O diagnóstico conta:

- série real divergente;
- série de LB divergente;
- blocos sem hora útil.

Mas `Avisos` só recebe mensagens para:

- real maior que zero sem série;
- blocos sem hora útil.

Assim, uma versão com 220 folhas divergentes na LB foi gravada com:

```json
"Avisos": []
```

**Impacto**

O status aparenta normalidade apesar de uma inconsistência material.

**Correção sugerida**

Adicionar avisos para ambas as divergências e incluir diferença absoluta/percentual. Definir limiar de bloqueio para sincronização.

---

### [BAIXO] B02 — Testes de contrato dependem do estado corrente do clone

**Arquivo/linha:** `Tests/NA_Connect.BiChs001.Tests/ApiContratoTests.cs:93-148`

**Evidência**

Os testes esperam diretamente:

- 883 tarefas;
- 755 folhas;
- 16024 linhas diárias;
- `Alias_Id = 1`;
- 883 linhas no legado.

Esses valores provam a versão v47, mas tornam os testes sensíveis a qualquer nova versão do clone e ao valor de identidade do alias.

**Impacto**

Uma sincronização legítima futura pode quebrar testes de contrato sem que o contrato tenha regredido.

**Correção sugerida**

Separar:

- testes de contrato com banco isolado/fixture;
- teste de aceitação da versão v47 explicitamente nomeado e fixado a `versao=1`;
- testes de alias sem depender de `Alias_Id = 1`.

---

## 3. Isolamento e regressão do legado

### Resultado: aprovado com base nas evidências disponíveis

Os inventários SQL apresentam os mesmos valores antes e depois:

| Verificação | Antes | Depois |
|---|---:|---:|
| Objetos fora de `bi_chs001` | 642 / checksum `-1258081025` | 642 / mesmo checksum |
| Colunas fora de `bi_chs001` | 2403 / checksum `1925769961` | 2403 / mesmo checksum |
| Maior `modify_date` fora do schema | `2026-10-13 16:16:25.290` | igual |
| `Cronograma_Projetos` CHS001 | 46313 / checksum `-1309517800` | igual |

Também permaneceram documentados:

- 883 tarefas na view legada;
- 148 linhas de programação semanal;
- 2034 linhas na view de gates da LDP;
- texto legado `Emgineering - 30%` preservado.

O log de cópia para o checkout principal mostra arquivos novos no backend isolado e alterações concentradas no módulo frontend `biChs001`. Não há evidência de alteração em:

- `Services/CronogramaTipo2/*`;
- `Models/CronogramaTipo2Registro.cs`;
- tabelas ou views `dbo`;
- importador legado;
- MPP do ACC.

A única dependência direta do leitor novo sobre o legado é reutilização de `CronogramaTipo2FieldResolver`, por import de namespace. Isso não altera o importador, mas cria acoplamento de compilação. É aceitável enquanto o resolver permanecer uma utilidade pura.

---

## 4. Migração e rollback

### Resultado parcial

Aspectos aprovados:

- UP cria somente objetos no schema `bi_chs001`.
- Não há `ALTER`, `UPDATE` ou `DELETE` sobre `dbo`.
- DOWN remove somente views e tabelas do schema isolado.
- UP foi executado duas vezes no clone com `exit=0`.
- DOWN foi executado duas vezes no clone com `exit=0`.
- Após DOWN, `SCHEMA=0`.
- Após novo UP, os oito objetos esperados voltaram a existir.
- O baseline externo permaneceu invariável.

Ressalvas:

- transação não abrange as views;
- índice de alias produz aviso de comprimento;
- verificação formal `VERIFYONLY/HEADERONLY` não foi encontrada;
- DOWN é destrutivo para snapshots, embora isso esteja documentado e os dados sejam reproduzíveis.

---

## 5. Leitor MPXJ

### Campos e estrutura

A implementação lê:

- `Start` e `Finish`;
- `BaselineStart` e `BaselineFinish`;
- `Work`, `ActualWork`, `BaselineWork` e `RemainingWork`;
- UID, GUID, ID, pai e nível;
- resumo e marco;
- calendário efetivo;
- atribuições;
- séries diária real, LB0 e planejada;
- campos customizados via alias.

A unidade é convertida explicitamente para horas por `Duration.convertUnits(TimeUnit.HOURS, props)`.

A versão gravada confirma:

- 883 tarefas;
- 755 folhas;
- 128 resumos;
- 755 atribuições;
- um único nó sem pai, correspondente à raiz;
- zero pais ausentes;
- zero folhas sem campos básicos;
- zero blocos sem hora útil;
- status date `2026-10-08 17:00:00`.

### Ressalvas

- A linha de base timephased não fecha com o total das tarefas.
- A série real tem pequena diferença contra o total nativo: `7856.456830` versus `7856.302000`.
- A comparação contra o PBIX contém 219 semanas adicionais.
- O teste aceita diferença global real inferior a uma hora, mas não impõe limite à série LB.

---

## 6. Alias CHS001

### Resultado: aprovado

A implementação atende aos requisitos:

- projeto restrito ao ID exato do CHS001;
- campo `Nome`;
- bruto exato `Emgineering - 30%`;
- canônico exato `Engineering - 30%`;
- comparação case-sensitive;
- somente tarefas de resumo;
- valor original preservado;
- alias exposto com `Alias_Id`;
- MPP e legado permanecem intactos.

Contagens confirmadas:

| Gate | Folhas |
|---|---:|
| Engineering - 30% | 196 |
| Engineering - 60% | 347 |
| Engineering - 90% | 190 |

---

## 7. API e permissões

### Resultado estático: aprovado com ressalva de tratamento de falhas

Rotas:

- `GET status`
- `GET tarefas`
- `GET trabalho-diario`
- `GET trabalho-semanal`
- `POST sincronizar`

Permissões:

- GET: `reports.gestao.view` ou `reports.cronograma.view`
- POST: `robot.run`

Restrição:

- valida formato ACC;
- executa autorização por projeto;
- depois restringe ao Project ID fixo do CHS001;
- outro projeto recebe `404`.

Contratos testados em código:

- `401` não autenticado;
- `403` sem acesso/permissão;
- `404` outro projeto ou versão inexistente;
- `503` indisponibilidade nos GETs;
- `robot.run` no POST.

Ressalva: exceções do POST podem resultar em `500`, e falhas funcionais são agrupadas em `502`.

---

## 8. Frontend e dez telas

As dez páginas prioritárias estão representadas:

- Resumo 30/60/90;
- LDP Engineering 30/60/90;
- LDP 3D 30/60/90;
- Programação.

Aspectos positivos:

- tarefas e série são fixadas à mesma versão;
- hierarquia Level 3–7 usa pais quando disponíveis;
- expansão de matrizes foi persistida;
- `ALLSELECTED` foi modelado para prazos;
- `TREATAS` de documentos 3D foi modelado;
- a segmentação de `dSemana` usa o valor salvo do PBIR;
- o `Not` sobre outra entidade não é mais invertido para falso;
- indisponibilidade de fonte gera estado pendente em vez de valor inventado;
- o alias é aplicado na camada SQL e auditável.

Ressalvas:

- as curvas dependem da série com divergências A01/A02;
- não há evidência de paridade pixel a pixel com Power BI Desktop;
- cross-filter entre visuais continua fora do escopo implementado;
- o fallback por `502` pode ocultar falha da fonte isolada;
- o cache em memória não possui invalidação explícita após nova sincronização;
- a ausência das duas logos extras foi tratada conforme o material de referência, mas não houve validação visual ao vivo nesta sessão.

---

## 9. Testes e builds

### Evidência preexistente

Os arquivos de testes cobrem adequadamente:

- hierarquia;
- campos nativos;
- preservação do bruto;
- série diária;
- não alteração do MPP;
- permissões;
- versão única;
- alias;
- domingo;
- atomicidade e rollback SQL;
- recusa de outro projeto.

Registros existentes informam:

- `node .../testar.mjs`: 32/32;
- builds isolados anteriores: sucesso;
- LDP Tipo 2: 106/106;
- Cronograma: 27/28, com uma falha registrada;
- Planning Tests: não compilaram devido a interfaces desatualizadas fora do módulo.

### Não executado nesta auditoria

Não foram reexecutados:

- `node _bi_chs001_replica/tools/testar.mjs`;
- `dotnet test ... MppReaderTests|ApiContratoTests`;
- script Python de conciliação;
- build do checkout;
- consultas SQL;
- `RESTORE VERIFYONLY`;
- `RESTORE HEADERONLY`.

Motivo: o ambiente permitiu leitura direta dos arquivos, mas recusou a criação de processos sob a política de aprovação vigente. Nenhum teste que grava (`SnapshotSqlTests`) foi executado.

---

## 10. Parecer final

### Isolamento

**APROVADO.** Não foi encontrada evidência de alteração do importador Tipo 2, do MPP no ACC ou de objetos/dados legados monitorados.

### Schema e versão 1

**APROVADO COM RESSALVAS.** A versão foi gravada atomicamente e apresenta hierarquia, campos e alias esperados. A migração precisa melhorar atomicidade das views e o índice de alias.

### Fidelidade das tarefas e gates

**APROVADO.** Datas, hierarquia e contagens 196/347/190 apresentam evidência forte de paridade.

### Fidelidade da série e das curvas

**NÃO APROVADO.** Há 220 divergências na série LB, excesso de aproximadamente 605,19 horas e 219 pares semanais adicionais frente ao PBIX.

### Deploy

**NÃO EXECUTADO / NÃO APROVADO COMO ESTADO ATUAL.** O processo vivo ainda contém o binário antigo.

### Critérios mínimos antes de liberar integralmente

1. Explicar ou corrigir as 220 divergências da série de linha de base.
2. Explicar ou corrigir os 219 pares semanais extras frente ao PBIX.
3. Fazer o diagnóstico bloquear ou alertar explicitamente versões com divergência material.
4. Corrigir a contaminação do build por `mpp_probe`.
5. Registrar `RESTORE VERIFYONLY` e `RESTORE HEADERONLY`.
6. Reexecutar testes direcionados e build limpo do checkout.
7. Somente então executar o deploy controlado.
8. Após o deploy, validar as rotas com autenticação real, versão única e números das dez telas.