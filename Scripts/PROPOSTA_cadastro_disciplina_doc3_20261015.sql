-- PROPOSTA — NÃO APLICADA. Aguarda aprovação explícita do usuário (15/10/2026).
-- O código já não depende destes ajustes: a regra de 2 caracteres ignora CIM/CVM=3D MODELING e o mapa canônico
-- prioriza Nome_Padrao. Estes UPDATEs só limpam o cadastro para as telas de Norma/Padronização ficarem coerentes.
-- Referências auditadas do ID 19: 1 alias ('3D MODELING'), Norma 2 CIM e Norma 3 CVM. Nenhuma FK além de DisciplinaAlias;
-- ref.tbMapaFuncaoVenda guarda texto e não usa 3D MODELING. Nada é excluído.
SET XACT_ABORT ON;
BEGIN TRAN;

-- 1) ID 19 '3D MODELING': Nome_PT/Nome_EN estavam como 'DIGITAL SYSTEMS' (nome do ID 8) e causavam ambiguidade.
UPDATE coordenacao.DisciplinaPadrao
   SET Nome_PT = N'MODELAGEM 3D', Nome_EN = N'3D MODELING', Data_Atualizacao = SYSUTCDATETIME()
 WHERE DisciplinaPadraoId = 19 AND Nome_Padrao = N'3D MODELING' AND Nome_EN = N'DIGITAL SYSTEMS';

-- 2) Sigla CIM (Norma 2) e CVM (Norma 3) apontam para 3D MODELING; pela regra CI/CV são CIVIL.
UPDATE coordenacao.NormaMapeamento SET Disciplina = N'CIVIL'
 WHERE (NormaId = 2 AND Sigla = N'CIM' AND Disciplina = N'3D MODELING')
    OR (NormaId = 3 AND Sigla = N'CVM' AND Disciplina = N'3D MODELING');

-- Conferir antes de confirmar:
SELECT DisciplinaPadraoId, Nome_Padrao, Nome_PT, Nome_EN FROM coordenacao.DisciplinaPadrao WHERE DisciplinaPadraoId IN (8, 19);
SELECT NormaId, Sigla, Disciplina FROM coordenacao.NormaMapeamento WHERE (NormaId = 2 AND Sigla = N'CIM') OR (NormaId = 3 AND Sigla = N'CVM');

ROLLBACK TRAN; -- trocar por COMMIT somente após aprovação

-- PONTOS PARA DECISÃO DO USUÁRIO (sem script, dependem da norma de cada projeto):
--  * Norma 2: SF -> PLANNING AND COORDINATION (LDP diz SAFETY, 134,5 HH previstas no BML011.013).
--  * Norma 2: ST -> STRUCTURAL STEEL (ID 24) x LDP STEEL STRUCTURE (ID 13): dois padrões globais para a mesma disciplina.
--  * Norma 2: PM -> PROJECT MANAGEMENT x LDP CONTRACT MANAGEMENT (PM0-AT-001, 440 HH).
--  * Norma 5: PM* -> 'GERENTE DE PROJETO'/'GERENTE DE PROEJTO' sem Disciplina Padrão; VA* sem sigla.
--  * Norma 3: sem siglas CI* (CHS001-000-CI0-AT-002, 5,5 HH ficam SEM DISCIPLINA).
--  * 5 projetos com Timesheet e sem Norma vinculada: BML012.01, BML012.02, TIM003, CON006, LOCAL-OS-BML011-BRASIL-SUPORTE.
