# Sessão de TV que expira (usuário NACONSULT, perfil TV restrito) — auditoria e correção

20/10/2026. **Status: código pronto e testado em worktree isolado; NÃO aplicado no checkout, NÃO publicado e backend NÃO reiniciado.** O recurso novo vem **desligado** (`TvPortal:DeviceSession:Enabled=false`). Até alguém aplicar o pacote, publicar e ligar a chave, nada muda.

- Worktree: `.claude/worktrees/tv-sessao-dispositivo-20261020`, branch `worktree-tv-sessao-dispositivo-20261020`. Commit `df8e319`. Os commits de base `71b3dc5`, `30fa8a5` e `716f630` são o snapshot do checkout às 15:13.
- Pacote: `C:\Dev\N-A-Connect aa\_entrega_claude_sessao_tv_naconsult_20261020\`, com `aplicar.py --conferir`, aplicação e `--reverter`.
- Builds e testes ficaram em `C:\Dev\N-A-Connect aa\_build_verify\tv_sessao_dispositivo_20261020\`.

Não houve SQL, DDL nem leitura do banco. Não toquei `gestaoVistaJamile*` nem o normalizador.

## 1. Diagnóstico: por que a TV expira mesmo com o poll de 15 s

Com o backend no ar, o poll de 15 s **mantém** a sessão viva. O ASP.NET renova o `IdleTimeout` de 60 min a cada request que lê a sessão. A expiração vem dos caminhos abaixo.

| # | Caminho | Efeito | Prova |
|---|---|---|---|
| 1 | **Restart do backend**: publicação, reinício do serviço Windows, queda. A sessão fica em `AddDistributedMemoryCache` (memória do processo). | Todas as sessões somem. O poll seguinte recebe 401 e a TV pede login. Nesta semana houve várias publicações. | Teste `SemDispositivo_RestartDoBackendDerrubaSessaoTv_Diagnostico` (mesmo cookie, nova instância → 401). |
| 2 | **Player trava no erro**. Em `TvPlayerPage`, qualquer falha do poll (502 do túnel, 503 quando a validação de identidade/SQL oscila, segundos de restart) fazia `setErro` + `setApresentacoes([])`. Quando o servidor voltava, o erro **não era limpo**. | Uma oscilação de segundos deixava a TV parada no aviso até alguém clicar Voltar/Iniciar, mesmo com a sessão ainda válida. | Código antigo: `.catch(e => { setErro(e.message); setApresentacoes([]) })`. Não havia `setErro(null)` no sucesso. |
| 3 | **Seletor `/tv/iniciar` parado**: sem poll. | Expira após 60 min ocioso. | `TvPortal` só busca a lista uma vez. |
| 4 | **TV ligada antes do servidor**: o gate fazia uma única tentativa em `/api/tv-portal/session`. | Erro permanente com o link "Entrar". | `TvPortalGate` não tinha nova tentativa. |
| 5 | **Navegador fechado, TV desligada ou PC suspenso por mais de 60 min**. O cookie de sessão não tem `Expires` e some ao fechar o navegador. Sem requests, o ocioso vence. | Exige login. | Configuração do `AddSession` em `Program.cs`. |
| 6 | **Chaves de Data Protection** não estão configuradas, e o app roda como serviço Windows (`UseWindowsService`). | Se a conta do serviço não tiver perfil carregado, as chaves são efêmeras e o cookie de sessão fica ilegível após restart (agrava o item 1). | **Não verificado em produção**: não sei a conta do serviço. A solução nova não depende dessas chaves. |

O `Auth:UserId` do login local (`NaAuthController`) vive só nessa sessão volátil. Não existe "lembrar-me".

## 2. O que foi implementado (escopo: só a conta TV restrita)

### Sessão de dispositivo TV (opt-in na própria TV)

1. A TV logada (perfil restrito + `tv.view`) mostra no seletor o botão **"Manter esta TV conectada"**. Ele faz `POST /api/tv-portal/device`.
2. O servidor recusa o registro (403/401/404) se a conta:
   - não for TV restrita;
   - não tiver `tv.view`;
   - não tiver entrado por senha local N&A;
   - tiver troca de senha pendente;
   - não tiver nenhuma apresentação ativa atribuída;
   - estiver com a chave desligada;
   - ou se a origem for cross-site.
3. Com tudo válido, o servidor gera um token aleatório de 256 bits e o entrega no cookie `__Host-NaTvDevice`: `HttpOnly; Secure; SameSite=Strict; Path=/`, sem `Domain`, com `Expires`.
4. O disco guarda **só o SHA-256** do token, em `App_Data\tv-device-sessions.json` (ou em `TvConfiguration:Directory`, ao lado de `tv-portal-assignments.json`). A gravação é atômica, com `.bak`. **Não há DDL.** O registro sobrevive a restart.
5. Expiração:
   - deslizante de **14 dias** sem uso, renovada no máximo 1×/h enquanto a TV faz requests;
   - teto absoluto de **180 dias**, depois novo login;
   - no máximo **5 dispositivos** por usuário (os mais antigos são revogados).

   Tudo configurável. **Nunca indefinido.**
6. Depois de restart ou ocioso, o `TvDeviceSessionMiddleware` (entre `UseSession` e `TvPortalMiddleware`) recompõe a sessão do usuário TV. Isso acontece **só** em GET de leitura do portal: `session`, `presentations`, `presentations/{id}`, `data/*`, `device`. Telas e APIs administrativas nunca restauram sessão.
7. **A cada request** de uma sessão de dispositivo, o servidor revalida:
   - o cookie é o mesmo dispositivo e não está revogado nem expirado;
   - o usuário está **ativo**;
   - a senha local não mudou (o `PasswordUpdatedAt` precisa bater com o gravado);
   - não há troca obrigatória de senha;
   - o perfil continua **TV restrito** e a conta tem **`tv.view`**, pela mesma `TvPortalIdentityService` da barreira.

   Qualquer falha revoga o dispositivo, apaga o cookie, limpa a sessão e responde 401.
8. **Apresentação atribuída e projetos ativos** continuam checados pelo gateway e pelo controller existentes, a cada request de apresentação/dado. O registro também exige uma atribuição ativa.
9. **Revogação**:
   - administrador em Configurações → Configuração de Telas para TV, bloco "TVs conectadas", botão "Revogar" (`DELETE /api/tv-portal/devices/{id}`, exige `settings.tv.configure` real e não restrito);
   - "Desconectar este dispositivo" na própria TV;
   - Logout.

   A TV cai na próxima requisição (≤ 15 s), inclusive com a sessão em memória ainda viva.
10. **Cookie Secure recusado** (TV aberta por `http://IP-da-rede`): o registro fica *pendente* e só é promovido quando o navegador devolve o cookie. Se o navegador recusar, a sessão comum continua como antes, sem 401 surpresa, e a TV mostra "abra pelo endereço HTTPS".

### Player e seletor (frontend)

- Respostas 401/403/404/409 tiram a apresentação da tela na hora (revogação), como antes.
- Rede e 5xx são tratados como transitórios: o player mantém o conteúdo já autorizado por até 4 tentativas (~60 s), continua tentando e **se recupera sozinho** quando o servidor responde (`setErro(null)`).
- O gate tenta de novo a cada 15 s, exceto em 401.
- Nenhum token, senha ou cookie vai para JS, `localStorage` ou `sessionStorage`. Um teste de contrato garante isso.

### Garantias de "sem elevação" e "sem afetar outros logins"

- A sessão restaurada é exatamente a do usuário TV. A barreira `TvPortalMiddleware` continua decidindo cada API: `assignments`, `devices`, `tv-config`, PUT e recurso fora da allowlist recebem 403 (testado).
- Se a conta virar admin ou comum, ou perder `tv.view`, o dispositivo é revogado. A sessão nunca "sobe" de privilégio (testado nas 6 variações).
- Login comum (N&A ou Autodesk) passa intocado: nenhum cookie extra, nenhuma consulta extra além de ler a sessão (testado). TV que não optar mantém o comportamento antigo.
- Cookie forjado ou malformado recebe 401 e não derruba a TV legítima. Cookie de outra TV não assume a sessão. Registro corrompido falha fechado (503), sem liberar e sem apagar.
- Não existe bypass de API: o novo endpoint só registra, esquece, lista e revoga. Não entrega dados.

## 3. Arquivos (pacote `base/` → `novo/`)

Novos:
- `Services/Tv/Portal/TvDeviceSession.cs`: opções, registro com hash, validação.
- `Services/Tv/Portal/TvDeviceSessionMiddleware.cs`
- `Controllers/TvDeviceSessionController.cs`, com `[PermissionExempt]` e motivo; a autorização é estrita na action.
- `ClientApp/src/features/tv/portal/TvDeviceSessions.tsx`: bloco administrativo.
- `Tests/NA_Connect.TvPortal.Tests/TvDeviceSessionTests.cs`
- `ClientApp/src/features/tv/__tests__/tvSessaoDispositivo.test.ts`
- Este relatório.

Alterados pontualmente:
- `Program.cs`: DI e 1 linha de middleware.
- `Services/Tv/Portal/TvPortalMiddleware.cs`: allowlist só para `/api/tv-portal/device`.
- `ClientApp/src/features/tv/TvPlayerPage.tsx`: só o efeito de revalidação do portal.
- `ClientApp/src/features/tv/portal/TvPortalGate.tsx`
- `ClientApp/src/features/tv/portal/portalTransport.ts`
- `ClientApp/src/features/tv/portal/TvPortalAssignments.tsx`: monta o bloco novo.
- `Tests/NA_Connect.TvPortal.Tests/NA_Connect.TvPortal.Tests.csproj`: o caminho da DLL virou a propriedade `TvPortalWebAppDll`, com o mesmo padrão de antes.

## 4. Testes executados

- **Build .NET isolado:** 0 erros. Saída em `_build_verify\tv_sessao_dispositivo_20261020\bin`. Os avisos são preexistentes; nenhum vem dos arquivos novos.
- **Suíte TV (.NET): 108/108 aprovados**, sendo 82 existentes e 26 novos. TRX em `_build_verify\tv_sessao_dispositivo_20261020\results\tv-sessao-dispositivo.trx`.
  - O teste de integração sobe Kestrel real em 127.0.0.1, com `NaAuthController` real, cookies HTTP reais e a sessão em memória como em produção.
  - O "restart" é uma nova instância com cache e chaves de Data Protection novos, sobre os mesmos arquivos.
  - Cobertura:
    - diagnóstico do restart;
    - atributos do cookie, e token nunca gravado em claro;
    - restauração após restart com e sem cookie de sessão;
    - nada administrativo após a restauração;
    - nenhuma restauração fora do portal;
    - revogação pelo admin (imediata e após restart);
    - revogação persistida;
    - expiração ociosa;
    - renovação deslizante limitada a 180 dias;
    - 6 mudanças de conta (inativo, senha alterada, troca obrigatória, virou admin, sem `tv.view`, perfil comum);
    - registro recusado (anônimo, admin, cross-site, sem atribuição);
    - chave desligada;
    - cookie forjado ou malformado;
    - cookie de outra TV;
    - logout e "esquecer";
    - outros logins inalterados;
    - registro corrompido → 503;
    - limite de dispositivos;
    - navegador recusando cookie Secure;
    - promoção do registro pendente;
    - contrato `[PermissionExempt]`.
- **TypeScript:** `tsc --noEmit` sem erros.
- **Vite:** build isolado ok em `_build_verify\tv_sessao_dispositivo_20261020\vite`. Só o aviso preexistente de chunk.
- **`npm run test:pmo`:** 390/397 no worktree.
  - As 7 falhas são as **mesmas 6 do checkout principal**: mojibake ×1, Tela 02, chave canônica, Tipo 2 ×2 e o contrato TvTelas. Ver a rodada de base no checkout: 405/411 com essas 6.
  - A 7ª é o arquivo `normalizadorSiglas.test`, que não carrega no worktree porque a fixture `Tests/NA_Connect.DisciplineAlias.Tests/Fixtures/casos_resolucao.json` não foi copiada. É só ambiente; o normalizador está fora do escopo.
  - Os 5 testes novos passam.
- **Não executado:**
  - `EndpointPermissionContractTests`: `NA_Connect.Planning.Tests` **não compila no checkout atual** (preexistente: `ProductionAuditControllerTests.Repo` está desatualizado em relação a `IProductionAuditRepository`). Coberto por um teste equivalente na suíte TV.
  - Observação, também preexistente: `TvPortalController` (Codex) não declara `[RequirePermission]`/`[PermissionExempt]` e seria apontado por esse contrato quando ele voltar a compilar.
- **Não houve teste com o backend publicado nem com a conta NACONSULT real.** Não consultei o banco. Precisa ser confirmado:
  - se NACONSULT tem perfil 7 e `tv.view`;
  - se tem senha local (sem troca pendente);
  - se tem atribuição de apresentação ativa;
  - se a TV acessa por HTTPS (túnel) ou localhost.

## 5. Como ativar (quando aprovado; nada disso foi feito)

1. Rode `python "C:\Dev\N-A-Connect aa\_entrega_claude_sessao_tv_naconsult_20261020\aplicar.py" --conferir`. Ele deve terminar sem conflitos.
2. Rode o mesmo comando sem `--conferir` para fazer o backup e aplicar. Depois, `cd "C:\Dev\N-A-Connect aa\N-A-Connect"; graphify update .`
3. Em `appsettings.json`, ligue o recurso (ou use a variável `TvPortal__DeviceSession__Enabled=true`):
   ```json
   "TvPortal": { "DeviceSession": { "Enabled": true, "IdleDays": 14, "MaxDays": 180 } }
   ```
4. Publique o backend e o frontend na mesma janela e reinicie o serviço (por você). A ordem é segura nos dois sentidos:
   - frontend novo com backend antigo: o bloco não aparece (403/404 → oculto);
   - backend novo com a chave desligada: nada muda.
5. Na TV, **pelo endereço HTTPS**:
   1. Entre com NACONSULT e abra `/tv/iniciar`.
   2. Clique em "Manter esta TV conectada" e confira a data exibida.
   3. Inicie a apresentação.
6. Homologue:
   - reinicie o backend: a TV deve continuar em até ~15 s, sem login;
   - feche e reabra o navegador: deve abrir o seletor sem login;
   - revogue em Configurações: a TV deve cair em até 15 s;
   - confira que admin e usuários comuns não mudaram.

**Rollback:**
- Imediato: `Enabled=false` e reinício. O cookie passa a ser ignorado e as TVs voltam à sessão comum.
- Completo: `aplicar.py --reverter`.

O arquivo `tv-device-sessions.json` pode ficar. Ele só contém hashes.

## 6. Limites e pendências (não implementados)

- **Retomada automática após reiniciar o navegador ou a TV:** a TV volta ao seletor já autenticada, mas o operador ainda clica "Iniciar apresentação" (o pedido de tela cheia exige um gesto). Proposta, aguardando aprovação: lembrar só o id da apresentação e o intervalo (não são segredos) e iniciar sozinho.
- **Processo único:** o registro em arquivo assume um único processo de backend, como as atribuições TV. Com várias instâncias, seria preciso um armazenamento compartilhado (aí exigiria DDL e aprovação).
- **Sem vínculo a IP ou user-agent:** o túnel Cloudflare e o NAT tornariam isso frágil. A mitigação é HttpOnly, Strict, expiração, revogação, invalidação por troca de senha, limite de 5 e "último uso" visível ao admin.
- **Conta TV que entra pela Autodesk** (não local) não consegue registrar dispositivo, por desenho.
- **Incidente durante a sessão:** por volta de 15:13–15:25, a cópia do checkout para o worktree (6 GB) **lotou o disco C:** (que já tinha ~5 GB livres). O espaço foi liberado em seguida, removendo a parte não versionada da cópia; restaram ~11 GB livres. Nesse intervalo, outros processos (backend, outras sessões) podem ter falhado ao gravar arquivos ou logs. O checkout principal não foi alterado.
