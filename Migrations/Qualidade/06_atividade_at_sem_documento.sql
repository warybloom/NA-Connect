SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    06_atividade_at_sem_documento.sql

    Regra:
    - uma classificação AT manual NÃO cria documento de destino;
    - HH/custo permanecem integralmente no projeto;
    - a disciplina gerencial vem da decisão AT manual;
    - DocumentoCodigo_Corrigido fica NULL na camada gerencial para o lançamento AT;
    - o código original continua preservado em DocumentoCodigo_Original para auditoria;
    - redistribuições documentais normais continuam funcionando como antes.
*/

CREATE OR ALTER VIEW gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial
AS
WITH Expandido AS
(
    SELECT
        t.Id AS Id_Origem,
        t.Project_ID_ACC,
        t.NomeProfissional,
        t.HorasPlanejadas,
        t.HorasTrabalhadas,
        t.Projeto,
        t.Funcao,
        t.Disciplina,
        t.DocumentoCodigo_Original,
        t.DocumentoCodigo_Corrigido,
        t.Documento,
        t.Descricao,
        t.Doc1,
        t.Doc2,
        t.Doc3,
        t.Doc4,
        t.Doc5,
        t.DataLancamento,
        t.CustoCalculado,
        t.Data_Sincronizacao,
        t.Tipo_Correcao,
        h.Id AS Redistribuicao_Id,
        h.Status_Documento AS Tratamento_Tipo,
        h.Disciplina AS Tratamento_Disciplina,
        i.Id AS Redistribuicao_Item_Id,
        i.Documento_Destino,
        i.Titulo_Destino,
        i.Peso
    FROM gestao.VW_TimeSheet_Corrigido_Espelho t
    LEFT JOIN gestao.AuditoriaRedistribuicaoDocumento h
        ON h.Project_ID_ACC = t.Project_ID_ACC
       AND UPPER(LTRIM(RTRIM(h.Documento_Origem)))
           = UPPER(LTRIM(RTRIM(t.DocumentoCodigo_Corrigido)))
       AND h.Ativo = 1
       AND h.Status = N'CONFIRMADO'
    LEFT JOIN gestao.AuditoriaRedistribuicaoItem i
        ON i.Redistribuicao_Id = h.Id
)
SELECT
    CAST(ROW_NUMBER() OVER
    (
        ORDER BY
            e.Project_ID_ACC,
            e.Id_Origem,
            ISNULL(e.Redistribuicao_Item_Id, 0)
    ) AS bigint) AS Id,
    e.Project_ID_ACC,
    e.NomeProfissional,
    CASE
        WHEN e.Redistribuicao_Id IS NULL
          OR UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
            THEN e.HorasPlanejadas
        ELSE CAST(ISNULL(e.HorasPlanejadas, 0) * e.Peso AS decimal(38,18))
    END AS HorasPlanejadas,
    CASE
        WHEN e.Redistribuicao_Id IS NULL
          OR UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
            THEN e.HorasTrabalhadas
        ELSE CAST(ISNULL(e.HorasTrabalhadas, 0) * e.Peso AS decimal(38,18))
    END AS HorasTrabalhadas,
    e.Projeto,
    e.Funcao,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
            THEN COALESCE(NULLIF(LTRIM(RTRIM(e.Tratamento_Disciplina)), N''), e.Disciplina)
        WHEN e.Redistribuicao_Id IS NULL
            THEN e.Disciplina
        ELSE COALESCE(ldp.Discipline, e.Disciplina)
    END AS Disciplina,
    e.DocumentoCodigo_Original,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
            THEN CAST(NULL AS nvarchar(255))
        WHEN e.Redistribuicao_Id IS NULL
            THEN e.DocumentoCodigo_Corrigido
        ELSE e.Documento_Destino
    END AS DocumentoCodigo_Corrigido,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
            THEN e.Documento
        WHEN e.Redistribuicao_Id IS NULL
            THEN e.Documento
        ELSE COALESCE(e.Titulo_Destino, ldp.Document_Title, e.Documento)
    END AS Documento,
    e.Descricao,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
          OR e.Redistribuicao_Id IS NULL THEN e.Doc1
        ELSE COALESCE(ldp.CR_OS, e.Doc1)
    END AS Doc1,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
          OR e.Redistribuicao_Id IS NULL THEN e.Doc2
        ELSE COALESCE(ldp.Area, e.Doc2)
    END AS Doc2,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
          OR e.Redistribuicao_Id IS NULL THEN e.Doc3
        ELSE COALESCE(ldp.Discipline_Activity, e.Doc3)
    END AS Doc3,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
            THEN N'AT'
        WHEN e.Redistribuicao_Id IS NULL THEN e.Doc4
        ELSE COALESCE(ldp.Doc_Type_Code, e.Doc4)
    END AS Doc4,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
          OR e.Redistribuicao_Id IS NULL THEN e.Doc5
        ELSE COALESCE(ldp.Seq, e.Doc5)
    END AS Doc5,
    e.DataLancamento,
    CASE
        WHEN e.Redistribuicao_Id IS NULL
          OR UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
            THEN e.CustoCalculado
        ELSE CAST(ISNULL(e.CustoCalculado, 0) * e.Peso AS decimal(38,18))
    END AS CustoCalculado,
    e.Data_Sincronizacao,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT'
            THEN N'AT_MANUAL'
        WHEN e.Redistribuicao_Id IS NULL
            THEN e.Tipo_Correcao
        ELSE N'REDISTRIBUICAO_AUDITORIA'
    END AS Tipo_Correcao
FROM Expandido e
LEFT JOIN dbo.VW_Ldp_Unificada ldp
    ON e.Redistribuicao_Id IS NOT NULL
   AND UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) <> N'AT'
   AND ldp.Project_ID_ACC = e.Project_ID_ACC
   AND UPPER(LTRIM(RTRIM(ldp.NA_Document_Number)))
       = UPPER(LTRIM(RTRIM(e.Documento_Destino)))
WHERE e.Redistribuicao_Id IS NULL
   OR e.Redistribuicao_Item_Id IS NOT NULL
   OR UPPER(LTRIM(RTRIM(ISNULL(e.Tratamento_Tipo, N'')))) = N'AT';
GO

/*
Validação esperada depois de uma confirmação AT:

SELECT
    DocumentoCodigo_Original,
    DocumentoCodigo_Corrigido,
    Doc3,
    Doc4,
    Disciplina,
    HorasTrabalhadas,
    CustoCalculado,
    Tipo_Correcao
FROM gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial
WHERE Project_ID_ACC = N'<PROJECT_ID>'
  AND DocumentoCodigo_Original = N'<CODIGO_ORIGINAL>';

Resultado:
- DocumentoCodigo_Corrigido = NULL
- Doc4 = AT
- Disciplina = disciplina escolhida
- HH/custo preservados
- Tipo_Correcao = AT_MANUAL
*/
