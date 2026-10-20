using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Implementação real (gravação) da linha principal da LDP Tipo 2 em dbo.Ldp_Unificada. A tabela já
    /// existe e é compartilhada com o Tipo 1 — esta classe nunca executa DDL, nunca recria a tabela,
    /// nunca faz DELETE/UPDATE em linhas antigas (cada sincronização válida gera uma linha NOVA; o
    /// histórico é preservado, exatamente como o Tipo 1 já faz).
    ///
    /// ESTRUTURA REAL INSPECIONADA (somente leitura, via INFORMATION_SCHEMA.COLUMNS e sys.indexes —
    /// nenhuma alteração de estrutura foi feita):
    ///   - Chave primária real: ID_Registro (int, IDENTITY, NOT NULL) — NÃO é "Id"/"ID"/"Id_Registro".
    ///   - As 33 propriedades de LdpTipo2Documento mapeiam 1:1 para colunas reais de Ldp_Unificada (ver
    ///     lista de parâmetros abaixo); ProjectIdAcc e DataSincronizacao vêm de fora do documento
    ///     (Project_ID_ACC, Data_Sincronizacao).
    ///   - DUAS colunas da tabela (Percent_Planned_Physical, Percent_Physical_Completed) NÃO têm campo
    ///     correspondente em LdpTipo2Documento (esses valores só existem por Gate, em
    ///     Ldp_Documento_Gates.Planned_Physical/Physical_Completed) — por isso NÃO são incluídas neste
    ///     INSERT e ficam NULL na linha principal (ambas são nullable na tabela real). Nenhum dado foi
    ///     inventado para preenchê-las.
    /// </summary>
    public sealed class LdpTipo2Repository : ILdpTipo2Repository
    {
        private const string InsertSql = @"
            INSERT INTO dbo.Ldp_Unificada
            (
                Project_ID_ACC, Item, Schedule_ID, Discipline, NA_Document_Number, Client_Document_Number,
                Document_Title, Format, No_Pages, Planned_Date_Baseline, Replanned_Date, Emission_Date,
                Transmittal_Number, Issued_Purpose, Revision, Document_Status, WBS_Percent, Status_In_Flow,
                Situation, Date_Comments_Received, Comment_Status, Inclusion_Date, CR, CR_OS, Area,
                Discipline_Activity, Doc_Type_Code, Seq, Planned_HH, Actual_HH, Remaining_HH,
                Planned_Cost, Actual_Cost, Remaining_Cost, Data_Sincronizacao
            )
            OUTPUT INSERTED.ID_Registro
            VALUES
            (
                @ProjectIdAcc, @Item, @ScheduleId, @Discipline, @NaDocumentNumber, @ClientDocumentNumber,
                @DocumentTitle, @Format, @NoPages, @PlannedDateBaseline, @ReplannedDate, @EmissionDate,
                @TransmittalNumber, @IssuedPurpose, @Revision, @DocumentStatus, @WbsPercent, @StatusInFlow,
                @Situation, @DateCommentsReceived, @CommentStatus, @InclusionDate, @Cr, @CrOs, @Area,
                @DisciplineActivity, @DocTypeCode, @Seq, @PlannedHh, @ActualHh, @RemainingHh,
                @PlannedCost, @ActualCost, @RemainingCost, @DataSincronizacao
            );";

        public async Task<long> InserirDocumentoPrincipalAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            LdpTipo2Documento documento,
            string projectIdAcc,
            DateTime dataSincronizacao,
            CancellationToken cancellationToken = default)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (documento == null) throw new ArgumentNullException(nameof(documento));
            if (string.IsNullOrWhiteSpace(projectIdAcc)) throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));

            using (var cmd = new SqlCommand(InsertSql, connection, transaction))
            {
                cmd.Parameters.Add("@ProjectIdAcc", SqlDbType.NVarChar, 100).Value = projectIdAcc;
                cmd.Parameters.Add("@Item", SqlDbType.NVarChar, 50).Value = (object)documento.Item ?? DBNull.Value;
                cmd.Parameters.Add("@ScheduleId", SqlDbType.NVarChar, 100).Value = (object)documento.ScheduleId ?? DBNull.Value;
                cmd.Parameters.Add("@Discipline", SqlDbType.NVarChar, 100).Value = (object)documento.Disciplina ?? DBNull.Value;
                cmd.Parameters.Add("@NaDocumentNumber", SqlDbType.NVarChar, 100).Value = (object)documento.NumeroDocumentoNa ?? DBNull.Value;
                cmd.Parameters.Add("@ClientDocumentNumber", SqlDbType.NVarChar, 100).Value = (object)documento.NumeroDocumentoCliente ?? DBNull.Value;
                cmd.Parameters.Add("@DocumentTitle", SqlDbType.NVarChar, -1).Value = (object)documento.TituloDocumento ?? DBNull.Value;
                cmd.Parameters.Add("@Format", SqlDbType.NVarChar, 50).Value = (object)documento.Formato ?? DBNull.Value;
                cmd.Parameters.Add("@NoPages", SqlDbType.Int).Value = (object)documento.NumeroPaginas ?? DBNull.Value;
                cmd.Parameters.Add("@PlannedDateBaseline", SqlDbType.DateTime).Value = (object)documento.PlannedBaseline ?? DBNull.Value;
                cmd.Parameters.Add("@ReplannedDate", SqlDbType.DateTime).Value = (object)documento.ReplannedDate ?? DBNull.Value;
                cmd.Parameters.Add("@EmissionDate", SqlDbType.DateTime).Value = (object)documento.EmissionDate ?? DBNull.Value;
                cmd.Parameters.Add("@TransmittalNumber", SqlDbType.NVarChar, 100).Value = (object)documento.TransmittalNumber ?? DBNull.Value;
                cmd.Parameters.Add("@IssuedPurpose", SqlDbType.NVarChar, 100).Value = (object)documento.IssuedPurpose ?? DBNull.Value;
                cmd.Parameters.Add("@Revision", SqlDbType.NVarChar, 50).Value = (object)documento.Revision ?? DBNull.Value;
                cmd.Parameters.Add("@DocumentStatus", SqlDbType.NVarChar, 100).Value = (object)documento.DocumentStatus ?? DBNull.Value;

                var pWbs = cmd.Parameters.Add("@WbsPercent", SqlDbType.Decimal);
                pWbs.Precision = 18; pWbs.Scale = 4;
                pWbs.Value = (object)documento.WbsPercent ?? DBNull.Value;

                cmd.Parameters.Add("@StatusInFlow", SqlDbType.NVarChar, 100).Value = (object)documento.StatusInFlow ?? DBNull.Value;
                cmd.Parameters.Add("@Situation", SqlDbType.NVarChar, 100).Value = (object)documento.Situation ?? DBNull.Value;
                cmd.Parameters.Add("@DateCommentsReceived", SqlDbType.DateTime).Value = (object)documento.DateCommentsReceived ?? DBNull.Value;
                cmd.Parameters.Add("@CommentStatus", SqlDbType.NVarChar, 100).Value = (object)documento.CommentStatus ?? DBNull.Value;
                cmd.Parameters.Add("@InclusionDate", SqlDbType.DateTime).Value = (object)documento.InclusionDate ?? DBNull.Value;
                cmd.Parameters.Add("@Cr", SqlDbType.NVarChar, 100).Value = (object)documento.Cr ?? DBNull.Value;
                cmd.Parameters.Add("@CrOs", SqlDbType.NVarChar, 100).Value = (object)documento.CrOs ?? DBNull.Value;
                cmd.Parameters.Add("@Area", SqlDbType.NVarChar, 100).Value = (object)documento.Area ?? DBNull.Value;
                cmd.Parameters.Add("@DisciplineActivity", SqlDbType.NVarChar, 100).Value = (object)documento.DisciplineActivity ?? DBNull.Value;
                cmd.Parameters.Add("@DocTypeCode", SqlDbType.NVarChar, 100).Value = (object)documento.DocTypeCode ?? DBNull.Value;
                cmd.Parameters.Add("@Seq", SqlDbType.NVarChar, 50).Value = (object)documento.Seq ?? DBNull.Value;

                var pPlannedHh = cmd.Parameters.Add("@PlannedHh", SqlDbType.Decimal);
                pPlannedHh.Precision = 18; pPlannedHh.Scale = 2;
                pPlannedHh.Value = (object)documento.PlannedHh ?? DBNull.Value;

                var pActualHh = cmd.Parameters.Add("@ActualHh", SqlDbType.Decimal);
                pActualHh.Precision = 18; pActualHh.Scale = 2;
                pActualHh.Value = (object)documento.ActualHh ?? DBNull.Value;

                var pRemainingHh = cmd.Parameters.Add("@RemainingHh", SqlDbType.Decimal);
                pRemainingHh.Precision = 18; pRemainingHh.Scale = 2;
                pRemainingHh.Value = (object)documento.RemainingHh ?? DBNull.Value;

                var pPlannedCost = cmd.Parameters.Add("@PlannedCost", SqlDbType.Decimal);
                pPlannedCost.Precision = 18; pPlannedCost.Scale = 2;
                pPlannedCost.Value = (object)documento.PlannedCost ?? DBNull.Value;

                var pActualCost = cmd.Parameters.Add("@ActualCost", SqlDbType.Decimal);
                pActualCost.Precision = 18; pActualCost.Scale = 2;
                pActualCost.Value = (object)documento.ActualCost ?? DBNull.Value;

                var pRemainingCost = cmd.Parameters.Add("@RemainingCost", SqlDbType.Decimal);
                pRemainingCost.Precision = 18; pRemainingCost.Scale = 2;
                pRemainingCost.Value = (object)documento.RemainingCost ?? DBNull.Value;

                cmd.Parameters.Add("@DataSincronizacao", SqlDbType.DateTime).Value = dataSincronizacao;

                object idGerado = await cmd.ExecuteScalarAsync(cancellationToken);

                if (idGerado == null || idGerado == DBNull.Value)
                {
                    throw new InvalidOperationException(
                        $"OUTPUT INSERTED.ID_Registro não retornou valor para o documento '{documento.NumeroDocumentoNa}' (linha Excel {documento.LinhaExcel}).");
                }

                // ID_Registro é int no banco; LdpTipo2Gate.LdpRegistroId é long? — conversão segura.
                return Convert.ToInt64(idGerado);
            }
        }
    }
}
