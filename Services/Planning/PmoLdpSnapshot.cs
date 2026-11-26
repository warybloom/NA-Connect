namespace APS.WebApp.Services.Planning;

/// <summary>
/// FONTE CANÔNICA DA LDP ATUAL DO PROJETO (relatórios). Universo de linhas = SOMENTE a última CARGA (snapshot) da LDP do projeto —
/// nunca "última versão de cada documento" (dbo.VW_Ldp_Unificada faz rn=1 por N&amp;A Document Number e por isso mantém vivos itens que já saíram da LDP).
/// A classificação documento × atividade é posterior: Doc_Type_Code/doc4 = AT continua nesta fonte para HH/custo, mas não é documento.
///
/// Tipo 2 grava um único Data_Sincronizacao por lote: somente o maior timestamp do projeto pertence ao snapshot atual.
/// Tipo 1 grava um timestamp por linha: mantém o bloco contíguo ao final da linha do tempo do projeto,
/// iniciado depois da última lacuna &gt; <see cref="GapSegundosNovaCarga"/> s.
///
/// Linhas-modelo totalmente vazias (sem N&amp;A Document Number, sem Client Document Number e sem título) não são documentos (o BML011.021 tem 39) e ficam fora;
/// qualquer outra linha entra, inclusive N&amp;A Document Number repetido no mesmo snapshot (linhas reais da LDP, com HH/custo próprios).
/// Requer o parâmetro @projectId. Expõe as mesmas colunas de dbo.VW_Ldp_Unificada usadas pelos relatórios (Planned_*/Actual_* como DECIMAL(18,4), Inicio_da_Semana).
/// </summary>
public static class PmoLdpSnapshot
{
    public const int GapSegundosNovaCarga = 30;

    /// <summary>
    /// Hint OBRIGATÓRIO no statement EXTERNO que lê <see cref="Tabela"/> (nunca dentro da derived table, que é reutilizada
    /// como FROM (&lt;snapshot&gt;) AS LdpAtual). Com @projectId parametrizado (SqlClient → sp_executesql) o SQL Server
    /// compila UM plano genérico, sem conhecer o projeto: para projetos Tipo 1 com milhares de cargas o corte por lacuna
    /// (LAG + MAX) vira varredura repetida da Ldp_Unificada (~19,7 mi leituras lógicas, &gt; 90 s → timeout/HTTP 503).
    /// Com RECOMPILE o plano é compilado com o valor real do parâmetro (constant folding dos ramos Tipo 1/Tipo 2),
    /// como acontece com o literal (~40 ms). Não altera a semântica do snapshot. Contrato:
    /// Tests/NA_Connect.Planning.Tests/PmoLdpSnapshotPlanoTests.cs.
    /// </summary>
    public const string OpcaoPlano = "OPTION (RECOMPILE)";

    public static readonly string Tabela = $"""
        (SELECT L.ID_Registro, L.Project_ID_ACC, L.Item, L.Schedule_ID, L.Discipline, L.NA_Document_Number, L.Client_Document_Number, L.Document_Title,
                L.Format, L.No_Pages, L.Planned_Date_Baseline, L.Replanned_Date, L.Emission_Date, L.Transmittal_Number, L.Issued_Purpose, L.Revision,
                L.Document_Status, L.WBS_Percent, L.Status_In_Flow, L.Situation, L.Date_Comments_Received, L.Comment_Status, L.Percent_Planned_Physical,
                L.Percent_Physical_Completed, L.Inclusion_Date, L.CR, L.CR_OS, L.Area, L.Discipline_Activity, L.Doc_Type_Code, L.Doc_Type_Code AS doc4, L.Seq,
                CAST(L.Planned_HH AS DECIMAL(18,6)) AS Planned_HH, CAST(L.Actual_HH AS DECIMAL(18,4)) AS Actual_HH, CAST(L.Remaining_HH AS DECIMAL(18,4)) AS Remaining_HH,
                CAST(L.Planned_Cost AS DECIMAL(18,6)) AS Planned_Cost, CAST(L.Actual_Cost AS DECIMAL(18,4)) AS Actual_Cost, CAST(L.Remaining_Cost AS DECIMAL(18,4)) AS Remaining_Cost,
                L.Data_Sincronizacao,
                CASE WHEN L.Planned_Date_Baseline IS NOT NULL THEN DATEADD(DAY, 1 - DATEPART(WEEKDAY, L.Planned_Date_Baseline), CAST(L.Planned_Date_Baseline AS DATE)) END AS Inicio_da_Semana
         FROM dbo.Ldp_Unificada L
         WHERE L.Project_ID_ACC = @projectId
           AND ((EXISTS (SELECT 1 FROM dbo.Projetos P WHERE P.Project_ID_ACC = @projectId AND P.Tipo_LDP = 2)
                 AND L.Data_Sincronizacao = (SELECT MAX(T2.Data_Sincronizacao)
                     FROM dbo.Ldp_Unificada T2 WHERE T2.Project_ID_ACC = @projectId))
             OR (NOT EXISTS (SELECT 1 FROM dbo.Projetos P WHERE P.Project_ID_ACC = @projectId AND P.Tipo_LDP = 2)
                 AND L.Data_Sincronizacao >= ISNULL((SELECT MAX(G.Data_Sincronizacao)
                   FROM (SELECT X.Data_Sincronizacao,
                                DATEDIFF(SECOND, LAG(X.Data_Sincronizacao) OVER (ORDER BY X.Data_Sincronizacao, X.ID_Registro), X.Data_Sincronizacao) AS Gap
                         FROM dbo.Ldp_Unificada X WHERE X.Project_ID_ACC = @projectId) G
                   WHERE G.Gap > {GapSegundosNovaCarga}), CAST('19000101' AS DATETIME))))
           AND NOT (LTRIM(RTRIM(ISNULL(L.NA_Document_Number, ''))) = '' AND LTRIM(RTRIM(ISNULL(L.Client_Document_Number, ''))) = ''
                    AND LTRIM(RTRIM(ISNULL(L.Document_Title, ''))) = '')) AS LdpAtual
        """;
}
