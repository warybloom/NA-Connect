#nullable disable
using System;
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Globalization;
using NA_Connect.Models;
using System.Linq;
using Dapper;
using APS.WebApp.Models;
using NA_Connect.Models;

namespace NA_Connect.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly string _localConnectionString;

        public DatabaseService(Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _localConnectionString = configuration.GetConnectionString("LocalConnection");
        }

        #region 🏗️ Módulo 01: Hubs e Projetos Base

        public DataTable GetHubs()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT HubContaID, Nome_Hub, Account_ID_ACC FROM Hubs_Conta";
                    var dt = new DataTable();
                    try 
                    {
                        conn.Open();
                        using (var da = new SqlDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                    catch (SqlException ex)
                    {
                        throw new Exception($"Falha de conexão com o SQL Server (Hubs): {ex.Message}", ex);
                    }
                    return dt;
                }
            }
        }

        public void RegistrarDnaProjeto(string projectId, string ldpFileId, string hubId, string ldpName, string idioma, string padrao)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF EXISTS (SELECT 1 FROM Metadados_Projetos WHERE Project_ID_ACC = @pid)
                        UPDATE Metadados_Projetos 
                        SET LDP_File_ID = @fid, 
                            Account_ID_ACC = @hub, 
                            Nome_LDP = @name, 
                            Idioma = @lang, 
                            Padrao_Nomenclatura = @std,
                            Data_Update = GETDATE()
                        WHERE Project_ID_ACC = @pid
                    ELSE
                        INSERT INTO Metadados_Projetos (Project_ID_ACC, LDP_File_ID, Account_ID_ACC, Nome_LDP, Idioma, Padrao_Nomenclatura, Data_Update)
                        VALUES (@pid, @fid, @hub, @name, @lang, @std, GETDATE())";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectId);
                    cmd.Parameters.AddWithValue("@fid", ldpFileId);
                    cmd.Parameters.AddWithValue("@hub", hubId);
                    cmd.Parameters.AddWithValue("@name", ldpName);
                    cmd.Parameters.AddWithValue("@lang", idioma);
                    cmd.Parameters.AddWithValue("@std", padrao);

                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public dynamic ObterNormaDoProjeto (string projectIdAcc)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                const string sql = @"
                    SELECT N.NormaID, N.Nome_Norma, N.Descricao
                    FROM Projetos P
                    LEFT JOIN Metadados_Projetos M ON P.Project_ID_ACC = M.Project_ID_ACC
                    LEFT JOIN Normas_Coordenacao N ON M.NormaID = N.NormaID
                    WHERE P.Project_ID_ACC = @pid";

                    if (conn.State == ConnectionState.Closed) conn.Open();
                    return conn.QueryFirstOrDefault(sql, new {pid = projectIdAcc});
            }
        }

        public void DeletarTemplate(int templateId)
{
    using (var connection = new SqlConnection(_connectionString))
    {
        var query = "DELETE FROM [DB_NA_CONNECT].[dbo].[Normas_Templates] WHERE TemplateID = @TemplateID";

        using (var command = new SqlCommand(query, connection))
        {
            command.Parameters.AddWithValue("@TemplateID", templateId);
            connection.Open();
            command.ExecuteNonQuery();
        }
    }
}



                public dynamic ObterProjetoPorIdAcc(string projectIdAcc)
        {
            using var conn = new SqlConnection(_localConnectionString);
            conn.Open();

            const string sql = @"
                SELECT 
                    p.Project_ID_ACC, 
                    h.Account_ID_ACC, 
                    p.Nome_Projeto 
                FROM Projetos p
                INNER JOIN Hubs_Conta h ON p.HubContaID = h.HubContaID
                WHERE p.Project_ID_ACC = @pid";

            return conn.QueryFirstOrDefault(sql, new { pid = projectIdAcc });
        }

                public void AtualizarProjetoPacoteOCR(int projetoId, int? pacoteId)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                string sql = "UPDATE Projetos SET PacoteOCRID = @pacoteId WHERE ProjetoID = @projetoId";
                conn.Execute(sql, new { projetoId, pacoteId });
            }
        }

                public void VincularProjetoAoPacoteOCR(string projectIdAcc, int pacoteId)
        {
        }

        public void ExcluirNorma(int id)
        {
            using var conn = new SqlConnection(_localConnectionString);
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                conn.Execute("DELETE FROM Normas_Mappings WHERE NormaID = @id", new { id }, trans);
                conn.Execute("DELETE FROM Normas_Coordenacao WHERE NormaID = @id", new { id }, trans);
                conn.Execute("UPDATE Projetos SET NormaID = NULL WHERE NormaID = @id", new { id }, trans);

                trans.Commit();
            }
            catch (Exception ex)
            {
                trans.Rollback();
                throw new Exception($"Erro ao excluir norma: {ex.Message}");
            }
        }


                public List<NormaDTO> ListarNormas() 
        {
            using var conn = new SqlConnection(_localConnectionString);
            
            var normas = conn.Query<NormaDTO>(@"
                SELECT 
                    NormaID as normaId, 
                    Nome_Norma as nome_Norma, 
                    Descricao 
                FROM Normas_Coordenacao").ToList();

            foreach(var n in normas) 
            {
                n.mappings = conn.Query<MappingDTO>(@"
                    SELECT 
                        Sigla as sigla, 
                        Pasta_Destino as Pasta_Destino, 
                        Tipo_Documento as tipo_Documento 
                    FROM Normas_Mappings 
                    WHERE NormaID = @id", new { id = n.normaId }).ToList();
            }

            return normas;
        }

                public void SalvarConfiguracaoProjetoManual(string projectIdAcc, int? normaId, int? pacoteOcrId)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                const string sqlMetadados = @"
                    IF EXISTS (SELECT 1 FROM Metadados_Projetos WHERE Project_ID_ACC = @pid)
                    BEGIN
                        UPDATE Metadados_Projetos 
                        SET NormaID = @nid, PacoteOCRID = @pocr, Data_Update = GETDATE()
                        WHERE Project_ID_ACC = @pid
                    END
                    ELSE
                    BEGIN
                        INSERT INTO Metadados_Projetos (Project_ID_ACC, NormaID, PacoteOCRID, Data_Update)
                        VALUES (@pid, @nid, @pocr, GETDATE())
                    END";

                conn.Open();
                conn.Execute(sqlMetadados, new { pid = projectIdAcc, nid = normaId, pocr = pacoteOcrId });
            }
        }

        public string ObterColunaChaveProjeto(string projectId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string cleanId = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId;
                string query = @"
                    SELECT TOP 1 Coluna_Chave 
                    FROM Metadados_Projetos 
                    WHERE Project_ID_ACC = @pid OR Project_ID_ACC = @cleanPid";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectId);
                    cmd.Parameters.AddWithValue("@cleanPid", cleanId);
                    conn.Open();
                    var result = cmd.ExecuteScalar();
                    string valor = result != null && result != DBNull.Value ? result.ToString() : null;
                    return !string.IsNullOrEmpty(valor) ? valor : "N&A DOCUMENT NUMBER";
                }
            }
        }

                public List<TemplateDTO> ListarTemplates()
        {
            using var conn = new SqlConnection(_localConnectionString);
            
            return conn.Query<TemplateDTO>(@"
                SELECT 
                    TemplateID, 
                    Sigla_Doc, 
                    Template_Nome, 
                    Tipo_Doc, 
                    NormaID 
                FROM Normas_Templates").ToList();
        }

                public void CriarTemplate(TemplateDTO tpl)
        {
            using var conn = new SqlConnection(_localConnectionString);
            
            const string sql = @"
                INSERT INTO Normas_Templates (Sigla_Doc, Template_Nome, Tipo_Doc, NormaID) 
                VALUES (@Sigla_Doc, @Template_Nome, @Tipo_Doc, @NormaID)";
            
            conn.Execute(sql, new { 
                Sigla_Doc = tpl.Sigla_Doc, 
                Template_Nome = tpl.Template_Nome, 
                Tipo_Doc = tpl.Tipo_Doc ?? "Geral", 
                NormaID = tpl.NormaID 
            });
        }

                public void AtualizarTemplateNorma(int tplId, int? normaId)
        {
            using var conn = new SqlConnection(_localConnectionString);
            conn.Execute("UPDATE Normas_Templates SET NormaID = @normaId WHERE TemplateID = @tplId", 
                new { tplId, normaId });
        }

        public void SalvarProjeto(int hubContaId, string nomeProjeto, string projectIdAcc)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF NOT EXISTS (SELECT 1 FROM Projetos WHERE Project_ID_ACC = @projectIdAcc)
                    BEGIN
                        INSERT INTO Projetos (HubContaID, Nome_Projeto, Project_ID_ACC, Is_Ativo)
                        VALUES (@hubContaId, @nomeProjeto, @projectIdAcc, 1)
                    END
                    ELSE
                    BEGIN
                        UPDATE Projetos 
                        SET Nome_Projeto = @nomeProjeto 
                        WHERE Project_ID_ACC = @projectIdAcc
                    END";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@hubContaId", hubContaId);
                    cmd.Parameters.AddWithValue("@nomeProjeto", nomeProjeto);
                    cmd.Parameters.AddWithValue("@projectIdAcc", projectIdAcc);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

               public async Task SalvarAtivosAuditoriaAsync(List<Plant3DAtivo> ativos)
{
    using (var conn = new SqlConnection(_localConnectionString))
    {
        string sql = @"
            INSERT INTO Plant3D_Ativos (
                AuditoriaID, Tag, Categoria, Desenho_PID, Desenho_3D, Spec_PID, Spec_3D, Status_Cruzamento,
                Tag_Fatiado_JSON, -- 👈 ESTAVA FALTANDO SALVAR ISTO NO BANCO!
                Atributo_01, Atributo_02, Atributo_03, Atributo_04, Atributo_05,
                Atributo_06, Atributo_07, Atributo_08, Atributo_09, Atributo_10,
                Atributo_11, Atributo_12, Atributo_13, Atributo_14, Atributo_15,
                Atributo_16, Atributo_17, Atributo_18, Atributo_19, Atributo_20
            ) VALUES (
                @AuditoriaID, @Tag, @Categoria, @Desenho_PID, @Desenho_3D, @Spec_PID, @Spec_3D, @Status_Cruzamento,
                @Tag_Fatiado_JSON, -- 👈 O VALOR VEM AQUI
                @Atributo_01, @Atributo_02, @Atributo_03, @Atributo_04, @Atributo_05,
                @Atributo_06, @Atributo_07, @Atributo_08, @Atributo_09, @Atributo_10,
                @Atributo_11, @Atributo_12, @Atributo_13, @Atributo_14, @Atributo_15,
                @Atributo_16, @Atributo_17, @Atributo_18, @Atributo_19, @Atributo_20
            )";
        
        await conn.ExecuteAsync(sql, ativos);
    }
}

        public async Task<IEnumerable<Plant3DAtivo>> ObterUltimosResultadosAsync(string projectId)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                string sql = @"
                    SELECT TOP 1000 * FROM Plant3D_Ativos 
                    WHERE AuditoriaID = (SELECT MAX(AuditoriaID) FROM Plant3D_Auditorias WHERE Project_ID_ACC = @projectId)
                    ORDER BY Tag ASC";
                return await conn.QueryAsync<Plant3DAtivo>(sql, new { projectId });
            }
        }

                public async Task SalvarConfiguracaoPlant3DAsync(Plant3DConfig config)
{
    using (var conn = new SqlConnection(_localConnectionString))
    {
        string sql = @"
            IF EXISTS (SELECT 1 FROM Plant3D_Configuracao WHERE Project_ID_ACC = @Project_ID_ACC)
            BEGIN
                UPDATE Plant3D_Configuracao 
                SET Mapeamento_JSON = ISNULL(@Mapeamento_JSON, Mapeamento_JSON),
                    Nome_Projeto = ISNULL(@Nome_Projeto, Nome_Projeto),
                    Folder_ID_DCF = ISNULL(@Folder_ID_DCF, Folder_ID_DCF),
                    Regras_Tag_JSON = ISNULL(@Regras_Tag_JSON, Regras_Tag_JSON) -- 👈 ATUALIZA A REGRA
                WHERE Project_ID_ACC = @Project_ID_ACC
            END
            ELSE
            BEGIN
                INSERT INTO Plant3D_Configuracao (Project_ID_ACC, Nome_Projeto, Mapeamento_JSON, Folder_ID_DCF, Regras_Tag_JSON)
                VALUES (@Project_ID_ACC, @Nome_Projeto, @Mapeamento_JSON, @Folder_ID_DCF, @Regras_Tag_JSON)
            END";

        await conn.ExecuteAsync(sql, config);
    }
}

        public async Task<Plant3DConfig?> GetConfiguracaoPlant3DAsync(string projectId)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                string sql = "SELECT * FROM Plant3D_Configuracao WHERE Project_ID_ACC = @projectId";
                return await conn.QueryFirstOrDefaultAsync<Plant3DConfig>(sql, new { projectId });
            }
        }

        /// <summary>
        /// Sincroniza em lote os projetos vindos do Autodesk (ACC) com a tabela local Projetos.
        /// Cria os que não existem e atualiza o nome dos que já existem, usando o Project_ID_ACC como chave.
        /// </summary>
        public void SincronizarProjetosEmLote(List<(string ProjectIdAcc, string NomeProjeto)> projetos)
        {
            if (projetos == null || projetos.Count == 0) return;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // Resolve um HubContaID padrão (o primeiro cadastrado) para satisfazer a FK da tabela Projetos.
                int? hubContaIdPadrao = null;
                using (var cmdHub = new SqlCommand("SELECT TOP 1 HubContaID FROM Hubs_Conta ORDER BY HubContaID ASC", conn))
                {
                    var result = cmdHub.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                        hubContaIdPadrao = Convert.ToInt32(result);
                }

                foreach (var p in projetos)
                {
                    if (string.IsNullOrWhiteSpace(p.ProjectIdAcc)) continue;

                    string query = @"
                        IF NOT EXISTS (SELECT 1 FROM Projetos WHERE Project_ID_ACC = @projectIdAcc)
                        BEGIN
                            INSERT INTO Projetos (HubContaID, Nome_Projeto, Project_ID_ACC, Is_Ativo)
                            VALUES (@hubContaId, @nomeProjeto, @projectIdAcc, 1)
                        END
                        ELSE
                        BEGIN
                            UPDATE Projetos 
                            SET Nome_Projeto = @nomeProjeto 
                            WHERE Project_ID_ACC = @projectIdAcc
                        END";

                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@hubContaId", (object)hubContaIdPadrao ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@nomeProjeto", p.NomeProjeto ?? "Projeto Sem Nome");
                        cmd.Parameters.AddWithValue("@projectIdAcc", p.ProjectIdAcc);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        public DataTable GetProjetosAtivos()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT P.Project_ID_ACC, P.Nome_Projeto, H.Account_ID_ACC 
                    FROM Projetos P
                    JOIN Hubs_Conta H ON P.HubContaID = H.HubContaID
                    WHERE P.Is_Ativo = 1";

                using (var cmd = new SqlCommand(query, conn))
                {
                    var dt = new DataTable();
                    conn.Open();
                    using (var da = new SqlDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                    return dt;
                }
            }
        }

        #endregion

        #region 🔍 Módulo 02: DNA do Projeto (Contexto e Cache)

        public ProjectDiscoveryResult ObterContextoProjeto(string projectId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT Naming_Standard, Project_Language FROM Projetos WHERE Project_ID_ACC = @pid";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectId);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new ProjectDiscoveryResult
                            {
                                NamingStandard = reader["Naming_Standard"]?.ToString(),
                                Language = reader["Project_Language"]?.ToString()
                            };
                        }
                    }
                }
            }
            return null;
        }

                public int InserirNovoPacoteOCR(string nome)
        {
            const string sql = @"
                INSERT INTO [dbo].[OCR_Pacotes] (Nome_Pacote) 
                OUTPUT INSERTED.PacoteID 
                VALUES (@nome)";

            try
            {
                using (IDbConnection db = new SqlConnection(_localConnectionString))
                {
                    if (db.State == ConnectionState.Closed) db.Open();
                    int newId = db.QuerySingle<int>(sql, new { nome });
                    return newId;
                }
            }
            catch (SqlException ex)
            {
                throw new Exception($"Erro de SQL ao inserir pacote OCR: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro interno no DatabaseService: {ex.Message}", ex);
            }
        }

                public List<dynamic> ListarPacotesOCR()
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                conn.Open();
                string sql = "SELECT PacoteID AS id, Nome_Pacote AS nome FROM OCR_Pacotes"; 
                return conn.Query(sql).ToList();
            }
        } 

        public void SalvarContextoProjeto(string projectId, string standard, string lang)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    UPDATE Projetos SET 
                        Naming_Standard = @standard, 
                        Project_Language = @lang 
                    WHERE Project_ID_ACC = @pid";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectId);
                    cmd.Parameters.AddWithValue("@standard", standard ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@lang", lang ?? (object)DBNull.Value);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public string GetLdpFileIdFromCache(string projectIdAcc)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT File_ID_LDP_ACC FROM Projetos WHERE Project_ID_ACC = @projectId";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@projectId", projectIdAcc);
                    conn.Open();
                    var result = cmd.ExecuteScalar();
                    return (result != null && result != DBNull.Value) ? result.ToString() : null;
                }
            }
        }

        public void AtualizarIdLdpProjeto(string projectIdAcc, string fileIdLdp)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = "UPDATE Projetos SET File_ID_LDP_ACC = @fileId WHERE Project_ID_ACC = @projectId";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@fileId", fileIdLdp ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@projectId", projectIdAcc);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        #endregion

        #region 📅 Módulo 03: Cronograma e Power BI

        public void SalvarTarefaCronograma(
            string projectIdAcc, string uid, string docNum, string name, 
            decimal phys, decimal pred, string wbsPct, 
            DateTime? start, DateTime? baseline, DateTime? finish, 
            string edt, string projeto, decimal trabalhoMpp)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF NOT EXISTS (SELECT 1 FROM Cronograma_Projetos WHERE Project_ID_ACC = @pid AND UID_Tarefa = @uid)
                    BEGIN
                        INSERT INTO Cronograma_Projetos (
                            Project_ID_ACC, UID_Tarefa, NA_DOCUMENT_NUMBER, TASK_NAME, 
                            PERCENT_PHYSICAL_COMPLETED, PERCENT_PREDICTED_PHYSICAL, WBS_PERCENT,
                            PLANNED_DATE_START, PLANNED_DATA_BASELINE, REPLANNED_DATE, 
                            EDT_WBS, CR_OS, Trabalho)
                        VALUES (@pid, @uid, @docNum, @name, @phys, @pred, @wbsPct, @start, @baseline, @finish, @edt, @projeto, @trabalho)
                    END
                    ELSE
                    BEGIN
                        UPDATE Cronograma_Projetos SET 
                            NA_DOCUMENT_NUMBER = @docNum, TASK_NAME = @name, 
                            PERCENT_PHYSICAL_COMPLETED = @phys, PERCENT_PREDICTED_PHYSICAL = @pred, 
                            WBS_PERCENT = @wbsPct, PLANNED_DATE_START = @start, 
                            PLANNED_DATA_BASELINE = @baseline, REPLANNED_DATE = @finish, 
                            EDT_WBS = @edt, CR_OS = @projeto, 
                            Trabalho = @trabalho, 
                            Data_Sincronizacao = GETDATE()
                        WHERE Project_ID_ACC = @pid AND UID_Tarefa = @uid
                    END";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectIdAcc);
                    cmd.Parameters.AddWithValue("@uid", uid);
                    cmd.Parameters.AddWithValue("@docNum", docNum ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@phys", phys);
                    cmd.Parameters.AddWithValue("@pred", pred);
                    cmd.Parameters.AddWithValue("@wbsPct", wbsPct ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@start", (object)start ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@baseline", (object)baseline ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@finish", (object)finish ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@edt", edt);
                    cmd.Parameters.AddWithValue("@projeto", projeto);
                    cmd.Parameters.AddWithValue("@trabalho", trabalhoMpp);

                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public int GetTotalTarefasSincronizadas(string projectIdAcc)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT COUNT(*) FROM Cronograma_Projetos WHERE Project_ID_ACC = @projectIdAcc";
                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@projectIdAcc", projectIdAcc);
                    conn.Open();
                    return (int)cmd.ExecuteScalar();
                }
            }
        }
        public void AtualizarUrnMasterList(string projectId, string urn)
{
    using (var conn = new SqlConnection(_localConnectionString))
    {
        string sql = @"
            UPDATE C 
            SET C.[Folder_MasterList_URN] = @urn
            FROM [dbo].[ConfiguracoesLDP] C
            INNER JOIN [dbo].[Projetos] P ON P.ConfigLDPID = C.ConfigLDPID
            WHERE P.Project_ID_ACC = @projectId";
        
        conn.Execute(sql, new { urn, projectId });
    }
}
public ConfigLdpContext ObterConfiguracaoLdp(string projectIdAcc)
{
    using (var conn = new SqlConnection(_localConnectionString))
    {
        // 🎯 ADICIONADO: C.[Folder_MasterList_URN] na projeção do SELECT
        string sql = @"
            SELECT 
                C.[Folder_ID_LDP], 
                C.[Folder_ID_Cronograma], 
                C.[SheetName], 
                C.[HeaderRow], 
                C.[MappingJSON],
                C.[File_Name_Pattern],
                C.[Folder_MasterList_URN]
            FROM [dbo].[Projetos] P
            INNER JOIN [dbo].[ConfiguracoesLDP] C ON P.ConfigLDPID = C.ConfigLDPID
            WHERE P.Project_ID_ACC = @projectId
              AND P.Is_Ativo = 1;";

        if (conn.State == ConnectionState.Closed) conn.Open();
        return conn.QueryFirstOrDefault<ConfigLdpContext>(sql, new { projectId = projectIdAcc });
    }
}

        public void LimparDadosProjetoParaNovoCiclo(string projectIdAcc)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                string sql = @"
                    DELETE FROM [dbo].[Ldp_Unificada] WHERE [Project_ID_ACC] = @pid;
                    DELETE FROM [dbo].[Logs_Processamento] WHERE [Project_ID_ACC] = @pid;";
                
                if (conn.State == ConnectionState.Closed) conn.Open();
                conn.Execute(sql, new { pid = projectIdAcc });
            }
        }

                public void SalvarLogProcessamento(string projectIdAcc, string fileName, string status, string mensagem)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                const string sql = @"
                    INSERT INTO [dbo].[Logs_Processamento] (Project_ID_ACC, FileName, [Status], Mensagem, Data_Log)
                    VALUES (@pid, @file, @st, @msg, GETDATE())";
                
                if (conn.State == ConnectionState.Closed) conn.Open();
                conn.Execute(sql, new { pid = projectIdAcc, file = fileName, st = status, msg = mensagem });
            }
        }
                public void SalvarMetadadosTemporarios(string projectIdAcc, string fileIdAcc, Dictionary<string, string> metadados)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                foreach (var item in metadados)
                {
                    string sql = @"
                        INSERT INTO Logs_Processamento (Project_ID_ACC, FileName, Status, Mensagem)
                        VALUES (@projectIdAcc, @fileId, 'OCR_EXTRAIDO', @msg)";
                    
                    conn.Execute(sql, new { 
                        projectIdAcc, 
                        fileId = fileIdAcc, 
                        msg = $"Campo: {item.Key} | Valor: {item.Value}" 
                    });
                }
            }
        }
                public List<OCRCoordenadaDTO> ObterCoordenadasOCR(string projectIdAcc, string tipoFolha)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                string sql = @"
                    SELECT c.X, c.Y, c.Largura as Width, c.Altura as Height, c.Campo_Destino as CampoDestino
                    FROM OCR_Coordenadas c
                    JOIN OCR_Pacotes p ON c.PacoteID = p.PacoteID
                    JOIN Projetos proj ON p.PacoteID = proj.PacoteOCRID 
                    WHERE proj.Project_ID_ACC = @projectIdAcc AND c.Tipo_Folha = @tipoFolha";

                return conn.Query<OCRCoordenadaDTO>(sql, new { projectIdAcc, tipoFolha }).ToList();
            }
        }

                public void SalvarNovaCoordenada(int pacoteId, string tipoFolha, OCRCoordenadaDTO coord)
        {
            const string sql = @"
                INSERT INTO [dbo].[OCR_Coordenadas] 
                    (PacoteID, Tipo_Folha, Campo_Destino, X, Y, Largura, Altura)
                VALUES 
                    (@pacoteId, @tipoFolha, @Campo_Destino, @X, @Y, @Largura, @Altura)";
            
            try 
            {
                using (IDbConnection db = new SqlConnection(_localConnectionString))
                {
                    if (db.State == ConnectionState.Closed) db.Open();

                    db.Execute(sql, new { 
                        pacoteId = pacoteId, 
                        tipoFolha = tipoFolha, 
                        Campo_Destino = coord.Campo ?? coord.CampoDestino, 
                        X = coord.X, 
                        Y = coord.Y, 
                        Largura = coord.Width,   
                        Altura = coord.Height    
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro de SQL ao salvar a coordenada '{coord.Campo}': {ex.Message}");
            }
        }





                public void RegistrarDnaProjeto(string projectIdAcc, string ldpFileId, string hubId, string ldpName, string idioma, string padraoNomenclatura, int? normaId, int? pacoteOcrId, string mensagem)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                if (normaId == null || pacoteOcrId == null)
                {
                    var config = conn.QueryFirstOrDefault("SELECT NormaID, PacoteOCRID FROM Projetos WHERE Project_ID_ACC = @pid", new { pid = projectIdAcc });
                    normaId ??= config?.NormaID;
                    pacoteOcrId ??= config?.PacoteOCRID;
                }

                const string sql = @"
                    IF EXISTS (SELECT 1 FROM Metadados_Projetos WHERE Project_ID_ACC = @pid)
                    BEGIN
                        UPDATE Metadados_Projetos SET 
                            LDP_File_ID = @ldp, Idioma = @lang, Padrao_Nomenclatura = @padrao, 
                            Data_Update = GETDATE(), NormaID = @nid, PacoteOCRID = @pocr
                        WHERE Project_ID_ACC = @pid
                    END
                    ELSE
                    BEGIN
                        INSERT INTO Metadados_Projetos (Project_ID_ACC, LDP_File_ID, Nome_LDP, Idioma, Padrao_Nomenclatura, Data_Update, NormaID, PacoteOCRID)
                        VALUES (@pid, @ldp, @nome, @lang, @padrao, GETDATE(), @nid, @pocr)
                    END";

                conn.Execute(sql, new { pid = projectIdAcc, ldp = ldpFileId, nome = ldpName, lang = idioma, padrao = padraoNomenclatura, nid = normaId, pocr = pacoteOcrId });
            }
        }
                public void LimparCoordenadasAntigas(int pacoteId, string tipoFolha)
        {
            const string sql = "DELETE FROM [dbo].[OCR_Coordenadas] WHERE PacoteID = @pacoteId AND Tipo_Folha = @tipoFolha";
            using (IDbConnection db = new SqlConnection(_localConnectionString))
            {
                db.Execute(sql, new { pacoteId, tipoFolha });
            }
        }


                public void SalvarNormaCompleta(NormaDTO norma)
        {
            using var conn = new SqlConnection(_localConnectionString);
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                int idReal = norma.normaId;
                
                if (idReal <= 0)
                {
                    var idExistente = conn.QueryFirstOrDefault<int?>(
                        "SELECT NormaID FROM Normas_Coordenacao WHERE Nome_Norma = @Nome", 
                        new { Nome = norma.Nome_Norma }, trans);
                    
                    if (idExistente.HasValue) 
                    {
                        idReal = idExistente.Value;
                    }
                }

                bool existe = conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Normas_Coordenacao WHERE NormaID = @id", new { id = idReal }, trans) > 0;

                if (!existe) 
                {
                    const string sqlIns = "INSERT INTO Normas_Coordenacao (Nome_Norma, Descricao) OUTPUT INSERTED.NormaID VALUES (@Nome_Norma, @Descricao)";
                    idReal = conn.QuerySingle<int>(sqlIns, new { norma.Nome_Norma, norma.Descricao }, trans);
                } 
                else 
                {
                    const string sqlUpd = "UPDATE Normas_Coordenacao SET Nome_Norma = @Nome_Norma, Descricao = @Descricao WHERE NormaID = @normaId";
                    conn.Execute(sqlUpd, new { norma.Nome_Norma, norma.Descricao, normaId = idReal }, trans);
                }

                conn.Execute("DELETE FROM Normas_Mappings WHERE NormaID = @normaId", new { normaId = idReal }, trans);

                if (norma.mappings != null && norma.mappings.Any()) 
                {
                    var mapeamentosUnicos = norma.mappings
                        .GroupBy(m => m.sigla)
                        .Select(g => g.First())
                        .ToList();

                    const string sqlMapping = @"INSERT INTO Normas_Mappings (NormaID, Sigla, Pasta_Destino, Tipo_Documento, Ignorar) 
                                                VALUES (@NormaID, @Sigla, @Pasta, @Tipo, @Ignorar)";

                    foreach (var m in mapeamentosUnicos) 
                    {
                        conn.Execute(sqlMapping, new { 
                            NormaID = idReal, 
                            Sigla = m.sigla, 
                            Pasta = m.pasta_Destino, 
                            Tipo = m.tipo_Documento,
                            Ignorar = m.ignorar
                        }, trans);
                    }
                }

                trans.Commit();
            }
            catch (Exception)
            {
                trans.Rollback();
                throw;
            }
        }

        #region 👤 Módulo 05: Usuários

public UsuarioDTO ObterOuCriarUsuario(string nome, string email)
{
    using (var conn = new SqlConnection(_localConnectionString))
    {
        conn.Open();

        const string sqlSelect = @"
            SELECT UserID, Nome, Email, RoleSistema, Ativo, DataCadastro
            FROM [dbo].[Usuarios]
            WHERE Email = @email";

        var usuario = conn.QueryFirstOrDefault<UsuarioDTO>(sqlSelect, new { email });

        if (usuario != null)
        {
            return usuario;
        }

        const string sqlInsert = @"
            INSERT INTO [dbo].[Usuarios] (Nome, Email, RoleSistema, Ativo, DataCadastro)
            OUTPUT INSERTED.UserID, INSERTED.Nome, INSERTED.Email, INSERTED.RoleSistema, INSERTED.Ativo, INSERTED.DataCadastro
            VALUES (@nome, @email, @role, @ativo, GETDATE())";

        var novoUsuario = conn.QuerySingle<UsuarioDTO>(sqlInsert, new
        {
            nome,
            email,
            role = "Viewer", // role padrão para novo cadastro
            ativo = true
        });

        return novoUsuario;
    }
}

#endregion

        public Dictionary<string, Dictionary<string, string>> GetDadosCronogramaDoBanco(string projectIdAcc)
        {
            var dict = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT NA_DOCUMENT_NUMBER, 
                           PERCENT_PHYSICAL_COMPLETED, 
                           PERCENT_PREDICTED_PHYSICAL,
                           WBS_PERCENT, 
                           PLANNED_DATA_BASELINE, 
                           REPLANNED_DATE
                    FROM Cronograma_Projetos 
                    WHERE Project_ID_ACC = @pid";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@pid", projectIdAcc);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string docCode = reader["NA_DOCUMENT_NUMBER"]?.ToString();
                            if (string.IsNullOrWhiteSpace(docCode)) continue;

                            string key = Regex.Replace(docCode.Trim().ToUpper(), @"[^A-Z0-9]", "");
                            var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                            if (reader["PERCENT_PHYSICAL_COMPLETED"] != DBNull.Value)
                            {
                                decimal phys = Convert.ToDecimal(reader["PERCENT_PHYSICAL_COMPLETED"]);
                                vals["Texto2"] = phys.ToString("0.##", CultureInfo.InvariantCulture) + "%";
                            }

                            if (reader["PERCENT_PREDICTED_PHYSICAL"] != DBNull.Value)
                            {
                                decimal pred = Convert.ToDecimal(reader["PERCENT_PREDICTED_PHYSICAL"]);
                                vals["Texto3"] = pred.ToString("0.##", CultureInfo.InvariantCulture) + "%";
                            }

                            if (reader["WBS_PERCENT"] != DBNull.Value)
                                vals["WBS_PERCENT"] = reader["WBS_PERCENT"].ToString();

                            if (reader["PLANNED_DATA_BASELINE"] != DBNull.Value)
                            {
                                DateTime baseline = Convert.ToDateTime(reader["PLANNED_DATA_BASELINE"]);
                                vals["Planned_Date_Baseline"] = FormatarDataParaLDP(baseline);
                            }

                            if (reader["REPLANNED_DATE"] != DBNull.Value)
                            {
                                DateTime replanned = Convert.ToDateTime(reader["REPLANNED_DATE"]);
                                vals["Replanned_Date"] = FormatarDataParaLDP(replanned);
                            }

                            dict[key] = vals;
                        }
                    }
                }
            }
            return dict;
        }

                public async Task<int> SalvarAuditoriaAsync(Plant3DAuditoria auditoria)
        {
            using (var conn = new SqlConnection(_localConnectionString))
            {
                string sql = @"
                    INSERT INTO Plant3D_Auditorias (Project_ID_ACC, Data_Auditoria, Total_PID, Total_3D, Faltam_No_3D, Faltam_No_PID, Divergencias_Spec)
                    VALUES (@Project_ID_ACC, GETDATE(), @Total_PID, @Total_3D, @Faltam_No_3D, @Faltam_No_PID, @Divergencias_Spec);
                    SELECT CAST(SCOPE_IDENTITY() as int);";

                return await conn.QuerySingleAsync<int>(sql, auditoria);
            }
        }

        private string FormatarDataParaLDP(DateTime dt)
        {
            var culture = new CultureInfo("pt-BR");
            string dayName = culture.DateTimeFormat.GetAbbreviatedDayName(dt.DayOfWeek).ToUpper();
            if (dayName.Length > 3) dayName = dayName.Substring(0, 3);
            return $"{dayName} {dt:dd/MM/yy}";
        }

        #endregion

        #region 📄 Módulo 04: Metadados e OCR

        public void SalvarMetadado(string projectIdAcc, string numeroNA, string descricao)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    IF NOT EXISTS (SELECT 1 FROM Metadados_Projetos WHERE Project_ID_ACC = @projectIdAcc AND Numero_NA = @numeroNA)
                    BEGIN
                        INSERT INTO Metadados_Projetos (Project_ID_ACC, Numero_NA, Descricao_Corporativa)
                        VALUES (@projectIdAcc, @numeroNA, @descricao)
                    END
                    ELSE
                    BEGIN
                        UPDATE Metadados_Projetos 
                        SET Descricao_Corporativa = @descricao,
                            Data_Ultima_Atualizacao = GETDATE()
                        WHERE Project_ID_ACC = @projectIdAcc AND Numero_NA = @numeroNA
                    END";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@projectIdAcc", projectIdAcc);
                    cmd.Parameters.AddWithValue("@numeroNA", numeroNA);
                    cmd.Parameters.AddWithValue("@descricao", descricao);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public DataTable GetProjetosPendentes()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT P.Nome_Projeto, P.Project_ID_ACC 
                    FROM Projetos P
                    LEFT JOIN Metadados_Projetos M ON P.Project_ID_ACC = M.Project_ID_ACC
                    WHERE M.Project_ID_ACC IS NULL";

                using (var cmd = new SqlCommand(query, conn))
                {
                    var dt = new DataTable();
                    conn.Open();
                    using (var da = new SqlDataAdapter(cmd))
                    {
                        da.Fill(dt);
                    }
                    return dt;
                }
            }
        }

        public void SalvarDadosLdpUnificada(string projectId, Dictionary<int, object> rowData)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = @"
                    INSERT INTO [dbo].[Ldp_Unificada] (
                        [Project_ID_ACC], [Item], [Schedule_ID], [Discipline], 
                        [NA_Document_Number], [Client_Document_Number], [Document_Title], 
                        [Format], [No_Pages], [Planned_Date_Baseline], [Replanned_Date], [Emission_Date], 
                        [Transmittal_Number], [Issued_Purpose], [Revision], [Document_Status], 
                        [WBS_Percent], [Status_In_Flow], [Situation], [Date_Comments_Received], [Comment_Status], 
                        [Percent_Planned_Physical], [Percent_Physical_Completed], [Inclusion_Date], [CR], [CR_OS], 
                        [Area], [Discipline_Activity], [Doc_Type_Code], [Seq], [Planned_HH], [Actual_HH], [Remaining_HH], 
                        [Planned_Cost], [Actual_Cost], [Remaining_Cost], [Data_Sincronizacao]
                    ) VALUES (
                        @ProjectID, @Item, @SchID, @Disc, @NADoc, @CliDoc, @Title, @Format, @Pages, 
                        @DtPlan, @DtReplan, @DtEmiss, @Transmittal, @Purpose, @Rev, @Status, @WBS, @Flow, @Sit, 
                        @DtComm, @CommSt, @PctPlan, @PctReal, @DtInc, @CR, @CROS, @Area, @Activity, @DocType, @Seq, 
                        @HHPlan, @HHReal, @HHRem, @CostPlan, @CostReal, @CostRem, GETDATE()
                    )";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ProjectID", projectId);
                    cmd.Parameters.AddWithValue("@Item", GetString(rowData, 1));
                    cmd.Parameters.AddWithValue("@SchID", GetString(rowData, 2));
                    cmd.Parameters.AddWithValue("@Disc", GetString(rowData, 3));
                    cmd.Parameters.AddWithValue("@NADoc", GetString(rowData, 4));
                    cmd.Parameters.AddWithValue("@CliDoc", GetString(rowData, 5));
                    cmd.Parameters.AddWithValue("@Title", GetString(rowData, 6));
                    cmd.Parameters.AddWithValue("@Format", GetString(rowData, 7));
                    cmd.Parameters.AddWithValue("@Pages", GetInt(rowData, 8));
                    cmd.Parameters.AddWithValue("@DtPlan", TratarData(rowData, 9));
                    cmd.Parameters.AddWithValue("@DtReplan", TratarData(rowData, 10));
                    cmd.Parameters.AddWithValue("@DtEmiss", TratarData(rowData, 11));
                    cmd.Parameters.AddWithValue("@Transmittal", GetString(rowData, 12));
                    cmd.Parameters.AddWithValue("@Purpose", GetString(rowData, 13));
                    cmd.Parameters.AddWithValue("@Rev", GetString(rowData, 14));
                    cmd.Parameters.AddWithValue("@Status", GetString(rowData, 15));
                    cmd.Parameters.AddWithValue("@WBS", GetDecimal(rowData, 16));
                    cmd.Parameters.AddWithValue("@Flow", GetString(rowData, 17));
                    cmd.Parameters.AddWithValue("@Sit", GetString(rowData, 18));
                    cmd.Parameters.AddWithValue("@DtComm", GetString(rowData, 19));
                    cmd.Parameters.AddWithValue("@CommSt", GetString(rowData, 20));
                    cmd.Parameters.AddWithValue("@PctPlan", GetDecimal(rowData, 21));
                    cmd.Parameters.AddWithValue("@PctReal", GetDecimal(rowData, 22));
                    cmd.Parameters.AddWithValue("@DtInc", GetString(rowData, 23));
                    cmd.Parameters.AddWithValue("@CR", GetString(rowData, 24));
                    cmd.Parameters.AddWithValue("@CROS", GetString(rowData, 25));
                    cmd.Parameters.AddWithValue("@Area", GetString(rowData, 26));
                    cmd.Parameters.AddWithValue("@Activity", GetString(rowData, 27));
                    cmd.Parameters.AddWithValue("@DocType", GetString(rowData, 28));
                    cmd.Parameters.AddWithValue("@Seq", GetString(rowData, 29));
                    cmd.Parameters.AddWithValue("@HHPlan", GetDecimal(rowData, 30));
                    cmd.Parameters.AddWithValue("@HHReal", GetDecimal(rowData, 31));
                    cmd.Parameters.AddWithValue("@HHRem", GetDecimal(rowData, 32));
                    cmd.Parameters.AddWithValue("@CostPlan", GetDecimal(rowData, 33));
                    cmd.Parameters.AddWithValue("@CostReal", GetDecimal(rowData, 34));
                    cmd.Parameters.AddWithValue("@CostRem", GetDecimal(rowData, 35));

                    cmd.ExecuteNonQuery();
                }
            }
        }

        private object GetString(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string val = row[index].ToString().Trim();
            return string.IsNullOrEmpty(val) ? DBNull.Value : val;
        }

        private object GetInt(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string val = row[index].ToString().Trim();
            if (int.TryParse(val, out int result)) return result;
            return DBNull.Value;
        }

        private object GetDecimal(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string val = row[index].ToString().Trim().Replace("R$", "").Replace("%", "").Trim();
            if (decimal.TryParse(val, out decimal result)) return result;
            if (decimal.TryParse(val, new CultureInfo("pt-BR"), out decimal resBr)) return resBr;
            return DBNull.Value;
        }

        private object TratarData(Dictionary<int, object> row, int index)
        {
            if (!row.ContainsKey(index) || row[index] == null) return DBNull.Value;
            string valor = row[index].ToString().Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(valor) || valor == "-" || valor == "N/A" || valor == "TBD") return DBNull.Value;

            if (valor.Length > 8 && char.IsLetter(valor[0]))
            {
                int firstSpace = valor.IndexOf(' ');
                if (firstSpace != -1) valor = valor.Substring(firstSpace + 1);
            }

            if (DateTime.TryParse(valor, new CultureInfo("pt-BR"), DateTimeStyles.None, out DateTime data))
            {
                if (data.Year < 1900 || data.Year > 2100) return DBNull.Value;
                return data;
            }

            if (double.TryParse(valor, out double serial))
            {
                try {
                     var d = DateTime.FromOADate(serial);
                     if (d.Year < 1900 || d.Year > 2100) return DBNull.Value;
                     return d;
                } catch {}
            }

            return DBNull.Value;
        }

        public void SalvarDadosHoldControl(string projectId, Dictionary<int, object> rowData)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                
                string sql = @"
                    INSERT INTO [dbo].[tb_HoldControl] (
                        [Project_ID_ACC],
                        [Data_Sincronizacao],
                        [ITEM],
                        [N_HOLD],
                        [N_NA_DOCUMENTO],
                        [DISCIPLINA],
                        [TITULO_DOCUMENTO],
                        [REV],
                        [MOTIVO_HOLD],
                        [STATUS],
                        [RESPONSAVEL]
                    ) VALUES (
                        @Project_ID_ACC, 
                        GETDATE(), 
                        @Item, 
                        @NumeroHold, 
                        @DocumentoNeA, 
                        @Disciplina, 
                        @TituloDocumento, 
                        @Rev, 
                        @MotivoHold, 
                        @Status, 
                        @Responsavel
                    )";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Project_ID_ACC", projectId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Item", GetString(rowData, 1) ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NumeroHold", GetString(rowData, 2) ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DocumentoNeA", GetString(rowData, 3) ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Disciplina", GetString(rowData, 4) ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@TituloDocumento", GetString(rowData, 5) ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Rev", GetString(rowData, 6) ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@MotivoHold", GetString(rowData, 7) ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Status", GetString(rowData, 8) ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Responsavel", GetString(rowData, 9) ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void SalvarDadosQuality(string projectId, Dictionary<int, object> rowData)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                
                string sql = @"
                    INSERT INTO [dbo].[NCR NONCONFORMITY REPORT] (
                        [Cliente], [GRD], [CodigoNA], [CodigoCliente], [Descricao],
                        [Disciplina], [Revisao], [NumeroVias], [Mes], [DataEnvio],
                        [Emitente], [DataCriacao], [DataAtualizacao], [CR]
                    ) VALUES (
                        @Cliente, @GRD, @CodigoNA, @CodigoCliente, @Descricao,
                        @Disciplina, @Revisao, @NumeroVias, @Mes, @DataEnvio,
                        @Emitente, GETDATE(), GETDATE(), @CR
                    )";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Cliente", GetLimitedString(rowData, 1, 255));
                    cmd.Parameters.AddWithValue("@CR", GetLimitedString(rowData, 2, 100));
                    cmd.Parameters.AddWithValue("@GRD", GetLimitedString(rowData, 3, 100));
                    cmd.Parameters.AddWithValue("@CodigoNA", GetLimitedString(rowData, 4, 100));
                    cmd.Parameters.AddWithValue("@CodigoCliente", GetLimitedString(rowData, 5, 100));
                    cmd.Parameters.AddWithValue("@Descricao", GetLimitedString(rowData, 6, 2000));
                    cmd.Parameters.AddWithValue("@Disciplina", GetLimitedString(rowData, 7, 255));
                    cmd.Parameters.AddWithValue("@Revisao", GetLimitedString(rowData, 8, 50));
                    
                    string nViaTexto = GetLimitedString(rowData, 9, 50)?.ToString();
                    if (int.TryParse(nViaTexto, out int nViasInt)) {
                        cmd.Parameters.AddWithValue("@NumeroVias", nViasInt);
                    } else {
                        cmd.Parameters.AddWithValue("@NumeroVias", nViaTexto ?? (object)DBNull.Value);
                    }

                    string mesTexto = GetLimitedString(rowData, 10, 50)?.ToString();
                    byte? mesNumero = ConverterMesParaByte(mesTexto);
                    cmd.Parameters.AddWithValue("@Mes", mesNumero.HasValue ? (object)mesNumero.Value : DBNull.Value);

                    cmd.Parameters.AddWithValue("@DataEnvio", GetLimitedString(rowData, 11, 100));  
                    cmd.Parameters.AddWithValue("@Emitente", GetLimitedString(rowData, 12, 255));

                    cmd.ExecuteNonQuery();
                }
            }
        }

        private byte? ConverterMesParaByte(string mesTexto)
        {
            if (string.IsNullOrWhiteSpace(mesTexto)) return null;

            string mesProcurado = mesTexto.Trim().ToUpper();

            if (byte.TryParse(mesProcurado, out byte resultadoNumero))
            {
                return resultadoNumero;
            }

            if (mesProcurado.StartsWith("JAN")) return 1;
            if (mesProcurado.StartsWith("FEV") || mesProcurado.StartsWith("FEB")) return 2;
            if (mesProcurado.StartsWith("MAR")) return 3;
            if (mesProcurado.StartsWith("ABR") || mesProcurado.StartsWith("APR")) return 4;
            if (mesProcurado.StartsWith("MAI")) return 5;
            if (mesProcurado.StartsWith("JUN")) return 6;
            if (mesProcurado.StartsWith("JUL")) return 7;
            if (mesProcurado.StartsWith("AGO") || mesProcurado.StartsWith("AUG")) return 8;
            if (mesProcurado.StartsWith("SET") || mesProcurado.StartsWith("SEP")) return 9;
            if (mesProcurado.StartsWith("OUT") || mesProcurado.StartsWith("OCT")) return 10;
            if (mesProcurado.StartsWith("NOV")) return 11;
            if (mesProcurado.StartsWith("DEZ") || mesProcurado.StartsWith("DEC")) return 12;

            return null;
        }

        private object GetLimitedString(Dictionary<int, object> rowData, int index, int maxLength)
        {
            if (rowData != null && rowData.TryGetValue(index, out var value) && value != null)
            {
                string text = value.ToString().Trim();
                if (text.Length > maxLength)
                {
                    return text.Substring(0, maxLength);
                }
                return text;
            }
            return null;
        }

        /// <summary>
        /// Atualiza o status ativo na tabela Projetos e relaciona os metadados de Norma/OCR na Metadados_Projetos.
        /// </summary>
      public void AtualizarStatusProjeto(int projetoId, int status, int? normaId, int? pacoteOcrId)
{
    using var conn = new SqlConnection(_localConnectionString);
    conn.Open();

    // 1. Atualiza apenas o status de ativação na tabela Projetos
    const string sqlProjetos = @"
        UPDATE Projetos 
        SET Is_Ativo = @status 
        WHERE ProjetoID = @id";

    conn.Execute(sqlProjetos, new { id = projetoId, status });

    // 2. Atualiza NormaID e PacoteOCRID na tabela Metadados_Projetos
    const string sqlMetadados = @"
        UPDATE M
        SET M.NormaID = @nid, 
            M.PacoteOCRID = @pocr
        FROM Metadados_Projetos M
        INNER JOIN Projetos P ON M.Project_ID_ACC = P.Project_ID_ACC
        WHERE P.ProjetoID = @id";

    conn.Execute(sqlMetadados, new { id = projetoId, nid = normaId, pocr = pacoteOcrId });
}

public List<ProjectAdminDTO> ListarProjetosAdmin() 
{
    using var conn = new SqlConnection(_localConnectionString);
    
    string sql = @"
        SELECT 
            P.ProjetoID as projetoId, 
            P.Nome_Projeto as nome_Projeto, 
            P.Project_ID_ACC as project_ID_ACC, 
            P.Is_Ativo as is_Ativo, 
            M.NormaID as NormaID,
            M.PacoteOCRID as PacoteOCRID,
            C.ConfigLDPID as ConfigLDPID,
            C.Folder_ID_LDP as ldpFolderId,
            C.Folder_ID_Cronograma as cronFolderId
        FROM Projetos P
        LEFT JOIN Metadados_Projetos M ON P.Project_ID_ACC = M.Project_ID_ACC
        LEFT JOIN ConfiguracoesLDP C ON P.Project_ID_ACC = C.Project_ID_ACC"; 

    return conn.Query<ProjectAdminDTO>(sql).ToList();
}

public async Task<bool> ExisteDocumentoAsync(string nomeArquivoSemExtensao)
{
    var nomeUpper = nomeArquivoSemExtensao.Trim().ToUpper();

    const string sql = @"
        SELECT TOP 1 1
        FROM [DB_NA_CONNECT].[dbo].[Ldp_Unificada]
        WHERE UPPER(LTRIM(RTRIM(NA_Document_Number))) = @Nome
           OR UPPER(LTRIM(RTRIM(Client_Document_Number))) = @Nome";

    using (var conn = new SqlConnection(_connectionString))
    {
        var resultado = await conn.QueryFirstOrDefaultAsync<int?>(sql, new { Nome = nomeUpper });
        return resultado.HasValue;
    }
}

        public void ExcluirTemplate(int id)
        {
            using var conn = new SqlConnection(_localConnectionString);
            conn.Execute("DELETE FROM Normas_Templates WHERE TemplateID = @id", new { id });
        }

        public void SalvarPacoteTemplatesComArquivos(List<TemplateDTO> metadados, Microsoft.AspNetCore.Http.IFormFileCollection arquivos, int normaId)
        {
            using var conn = new SqlConnection(_localConnectionString);
            conn.Open();
            using var trans = conn.BeginTransaction();

            try 
            {
                conn.Execute("DELETE FROM Normas_Templates WHERE NormaID = @normaId", new { normaId }, trans);

                foreach (var meta in metadados) 
                {
                    var arquivoFisico = arquivos.FirstOrDefault(f => f.FileName == meta.Template_Nome);
                    
                    if (arquivoFisico != null) 
                    {
                        using var ms = new System.IO.MemoryStream();
                        arquivoFisico.CopyTo(ms);
                        byte[] fileBytes = ms.ToArray();

                        const string sqlBinary = @"
                            IF EXISTS (SELECT 1 FROM Api_Templates WHERE Codigo_Tipo = @cod)
                                UPDATE Api_Templates SET File_Bytes = @bytes, Extensao = @ext WHERE Codigo_Tipo = @cod
                            ELSE
                                INSERT INTO Api_Templates (Codigo_Tipo, Extensao, File_Bytes, Extension_Type_ACC)
                                VALUES (@cod, @ext, @bytes, @accType)";

                        conn.Execute(sqlBinary, new { 
                            cod = meta.Sigla_Doc,
                            ext = System.IO.Path.GetExtension(arquivoFisico.FileName),
                            bytes = fileBytes,
                            accType = "items:autodesk.bim360:File" 
                        }, trans);
                    }

                    const string sqlMeta = @"
                        INSERT INTO Normas_Templates (Sigla_Doc, Template_Nome, Tipo_Doc, NormaID) 
                        VALUES (@Sigla, @Nome, @Tipo, @NID)";
                    
                    conn.Execute(sqlMeta, new { 
                        Sigla = meta.Sigla_Doc, 
                        Nome = meta.Template_Nome, 
                        Tipo = meta.Tipo_Doc, 
                        NID = normaId 
                    }, trans);
                }

                trans.Commit();
            } 
            catch (Exception) 
            { 
                trans.Rollback(); 
                throw; 
            }
        }

        

        #endregion
    }
        public class ConfigLdpContext 
    {
        public string Folder_ID_LDP { get; set; }
        public string Folder_ID_Cronograma { get; set; }
        public string SheetName { get; set; }
        public int HeaderRow { get; set; }
        public string MappingJSON { get; set; }
        public string File_Name_Pattern { get; set; }
        public string Folder_MasterList_URN { get; set; }
    }
    public class UsuarioDTO
{
    public int UserID { get; set; }
    public string Nome { get; set; }
    public string Email { get; set; }
    public string RoleSistema { get; set; }
    public bool Ativo { get; set; }
    public DateTime DataCadastro { get; set; }
}
}