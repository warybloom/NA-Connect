using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NA_Connect.Services.Ldp
{
    /// <summary>
    /// Persistência do Novo Motor de LDP (fundação) — schema ldp.* proposto em
    /// Migrations/ConfiguracaoLdp/*.sql, AINDA NÃO EXECUTADO. Toda chamada aqui falha com uma
    /// SqlException explícita ("Invalid object name 'ldp.Template'") até a migration ser aplicada
    /// manualmente — nunca finge sucesso. Serviço isolado de DatabaseService.cs de propósito ("novo
    /// entra ao lado"): nenhum código existente foi tocado para este módulo existir.
    ///
    /// Padrão adotado: "substituir a árvore inteira" (replace-on-save), não CRUD granular por
    /// sub-linha — para dados deste porte (poucos templates, poucas abas/campos por template) é mais
    /// simples e igualmente correto: cada Salvar apaga e reinsere as abas/campos/linhas de um
    /// template/mapeamento/espelho/controle inteiro dentro de UMA transação.
    /// </summary>
    public class LdpConfigDatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<LdpConfigDatabaseService> _logger;

        public LdpConfigDatabaseService(IConfiguration configuration, ILogger<LdpConfigDatabaseService> logger)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
        }

        #region Template / Aba / Campo

        public List<LdpTemplateDto> GetTemplates()
        {
            var templates = new Dictionary<int, LdpTemplateDto>();
            var abasPorId = new Dictionary<int, LdpTemplateAbaDto>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using (var cmd = new SqlCommand("SELECT TemplateId, Nome_Template, Descricao, Origem_Criacao, Ativo FROM ldp.Template ORDER BY Nome_Template", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var t = new LdpTemplateDto
                    {
                        TemplateId = reader.GetInt32(0),
                        NomeTemplate = reader.GetString(1),
                        Descricao = reader.IsDBNull(2) ? null : reader.GetString(2),
                        OrigemCriacao = reader.GetString(3),
                        Ativo = reader.GetBoolean(4),
                    };
                    templates[t.TemplateId] = t;
                }
            }

            using (var cmd = new SqlCommand("SELECT TemplateAbaId, TemplateId, Nome_Aba, Ordem, Ativo FROM ldp.TemplateAba ORDER BY Ordem", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var aba = new LdpTemplateAbaDto
                    {
                        TemplateAbaId = reader.GetInt32(0),
                        NomeAba = reader.GetString(2),
                        Ordem = reader.GetInt32(3),
                        Ativo = reader.GetBoolean(4),
                    };
                    int templateId = reader.GetInt32(1);
                    abasPorId[aba.TemplateAbaId] = aba;
                    if (templates.TryGetValue(templateId, out var t)) t.Abas.Add(aba);
                }
            }

            using (var cmd = new SqlCommand("SELECT TemplateCampoId, TemplateAbaId, Nome_Exibido, Chave_Campo, Tipo_Dado, Obrigatorio, Ordem, Origem, Ativo FROM ldp.TemplateCampo ORDER BY Ordem", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var campo = new LdpTemplateCampoDto
                    {
                        TemplateCampoId = reader.GetInt32(0),
                        NomeExibido = reader.GetString(2),
                        ChaveCampo = reader.GetString(3),
                        TipoDado = reader.GetString(4),
                        Obrigatorio = reader.GetBoolean(5),
                        Ordem = reader.GetInt32(6),
                        Origem = reader.GetString(7),
                        Ativo = reader.GetBoolean(8),
                    };
                    int abaId = reader.GetInt32(1);
                    if (abasPorId.TryGetValue(abaId, out var aba)) aba.Campos.Add(campo);
                }
            }

            return new List<LdpTemplateDto>(templates.Values);
        }

        public int CreateTemplate(string nomeTemplate, string descricao, string origemCriacao)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "INSERT INTO ldp.Template (Nome_Template, Descricao, Origem_Criacao) OUTPUT INSERTED.TemplateId VALUES (@nome, @desc, @origem)", conn);
            cmd.Parameters.AddWithValue("@nome", nomeTemplate);
            cmd.Parameters.AddWithValue("@desc", string.IsNullOrEmpty(descricao) ? (object)DBNull.Value : descricao);
            cmd.Parameters.AddWithValue("@origem", origemCriacao ?? "MANUAL");
            return (int)cmd.ExecuteScalar();
        }

        /// <summary>Substitui abas/campos inteiros do template (delete + reinsert numa transação).</summary>
        public void ReplaceTemplateTree(int templateId, LdpTemplateDto tree)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand(
                    "UPDATE ldp.Template SET Nome_Template=@nome, Descricao=@desc, Ativo=@ativo WHERE TemplateId=@id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@nome", tree.NomeTemplate);
                    cmd.Parameters.AddWithValue("@desc", string.IsNullOrEmpty(tree.Descricao) ? (object)DBNull.Value : tree.Descricao);
                    cmd.Parameters.AddWithValue("@ativo", tree.Ativo);
                    cmd.Parameters.AddWithValue("@id", templateId);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SqlCommand(
                    "DELETE FROM ldp.TemplateCampo WHERE TemplateAbaId IN (SELECT TemplateAbaId FROM ldp.TemplateAba WHERE TemplateId=@id)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", templateId);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SqlCommand("DELETE FROM ldp.TemplateAba WHERE TemplateId=@id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", templateId);
                    cmd.ExecuteNonQuery();
                }

                foreach (var aba in tree.Abas)
                {
                    int abaId;
                    using (var cmd = new SqlCommand(
                        "INSERT INTO ldp.TemplateAba (TemplateId, Nome_Aba, Ordem, Ativo) OUTPUT INSERTED.TemplateAbaId VALUES (@tid, @nome, @ordem, @ativo)", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@tid", templateId);
                        cmd.Parameters.AddWithValue("@nome", aba.NomeAba);
                        cmd.Parameters.AddWithValue("@ordem", aba.Ordem);
                        cmd.Parameters.AddWithValue("@ativo", aba.Ativo);
                        abaId = (int)cmd.ExecuteScalar();
                    }

                    foreach (var campo in aba.Campos)
                    {
                        using var cmd = new SqlCommand(
                            @"INSERT INTO ldp.TemplateCampo
                                (TemplateAbaId, Nome_Exibido, Chave_Campo, Tipo_Dado, Obrigatorio, Ordem, Origem, Ativo)
                              VALUES (@abaId, @nome, @chave, @tipo, @obrig, @ordem, @origem, @ativo)", conn, tx);
                        cmd.Parameters.AddWithValue("@abaId", abaId);
                        cmd.Parameters.AddWithValue("@nome", campo.NomeExibido);
                        cmd.Parameters.AddWithValue("@chave", campo.ChaveCampo);
                        cmd.Parameters.AddWithValue("@tipo", campo.TipoDado);
                        cmd.Parameters.AddWithValue("@obrig", campo.Obrigatorio);
                        cmd.Parameters.AddWithValue("@ordem", campo.Ordem);
                        cmd.Parameters.AddWithValue("@origem", campo.Origem);
                        cmd.Parameters.AddWithValue("@ativo", campo.Ativo);
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void DeleteTemplate(int templateId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("DELETE FROM ldp.Template WHERE TemplateId=@id", conn);
            cmd.Parameters.AddWithValue("@id", templateId);
            cmd.ExecuteNonQuery();
        }

        #endregion

        #region Mapeamento de Cronograma

        public List<LdpCronogramaMapeamentoDto> GetMapeamentos(int templateId)
        {
            var mapeamentos = new Dictionary<int, LdpCronogramaMapeamentoDto>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using (var cmd = new SqlCommand("SELECT CronogramaMapeamentoId, TemplateId, Nome_Mapeamento, Ativo FROM ldp.CronogramaMapeamento WHERE TemplateId=@tid", conn))
            {
                cmd.Parameters.AddWithValue("@tid", templateId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var m = new LdpCronogramaMapeamentoDto
                    {
                        CronogramaMapeamentoId = reader.GetInt32(0),
                        TemplateId = reader.GetInt32(1),
                        NomeMapeamento = reader.GetString(2),
                        Ativo = reader.GetBoolean(3),
                    };
                    mapeamentos[m.CronogramaMapeamentoId] = m;
                }
            }

            if (mapeamentos.Count == 0) return new List<LdpCronogramaMapeamentoDto>(mapeamentos.Values);

            using (var cmd = new SqlCommand(
                @"SELECT c.CronogramaMapeamentoCampoId, c.CronogramaMapeamentoId, c.TemplateCampoId, c.Coluna_Cronograma_Origem, c.Ordem
                  FROM ldp.CronogramaMapeamentoCampo c
                  JOIN ldp.CronogramaMapeamento m ON m.CronogramaMapeamentoId = c.CronogramaMapeamentoId
                  WHERE m.TemplateId=@tid", conn))
            {
                cmd.Parameters.AddWithValue("@tid", templateId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var campo = new LdpCronogramaMapeamentoCampoDto
                    {
                        CronogramaMapeamentoCampoId = reader.GetInt32(0),
                        TemplateCampoId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        ColunaCronogramaOrigem = reader.GetString(3),
                        Ordem = reader.GetInt32(4),
                    };
                    int mapId = reader.GetInt32(1);
                    if (mapeamentos.TryGetValue(mapId, out var m)) m.Campos.Add(campo);
                }
            }

            return new List<LdpCronogramaMapeamentoDto>(mapeamentos.Values);
        }

        public int CreateMapeamento(int templateId, string nome)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "INSERT INTO ldp.CronogramaMapeamento (TemplateId, Nome_Mapeamento) OUTPUT INSERTED.CronogramaMapeamentoId VALUES (@tid, @nome)", conn);
            cmd.Parameters.AddWithValue("@tid", templateId);
            cmd.Parameters.AddWithValue("@nome", nome);
            return (int)cmd.ExecuteScalar();
        }

        public void ReplaceMapeamentoCampos(int mapeamentoId, List<LdpCronogramaMapeamentoCampoDto> campos)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand("DELETE FROM ldp.CronogramaMapeamentoCampo WHERE CronogramaMapeamentoId=@id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", mapeamentoId);
                    cmd.ExecuteNonQuery();
                }

                foreach (var campo in campos)
                {
                    if (campo.TemplateCampoId is null) continue; // linha ainda sem destino escolhido — não persiste De/Para incompleto
                    using var cmd = new SqlCommand(
                        @"INSERT INTO ldp.CronogramaMapeamentoCampo
                            (CronogramaMapeamentoId, TemplateCampoId, Coluna_Cronograma_Origem, Ordem)
                          VALUES (@mapId, @campoId, @coluna, @ordem)", conn, tx);
                    cmd.Parameters.AddWithValue("@mapId", mapeamentoId);
                    cmd.Parameters.AddWithValue("@campoId", campo.TemplateCampoId.Value);
                    cmd.Parameters.AddWithValue("@coluna", campo.ColunaCronogramaOrigem);
                    cmd.Parameters.AddWithValue("@ordem", campo.Ordem);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void DeleteMapeamento(int mapeamentoId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("DELETE FROM ldp.CronogramaMapeamento WHERE CronogramaMapeamentoId=@id", conn);
            cmd.Parameters.AddWithValue("@id", mapeamentoId);
            cmd.ExecuteNonQuery();
        }

        #endregion

        #region Excel Espelho

        public List<LdpExcelEspelhoDto> GetEspelhos(int templateId)
        {
            var espelhos = new Dictionary<int, LdpExcelEspelhoDto>();
            var abasPorId = new Dictionary<int, LdpExcelEspelhoAbaDto>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using (var cmd = new SqlCommand("SELECT ExcelEspelhoId, TemplateId, Nome_Modelo, Arquivo_Referencia, Ativo FROM ldp.ExcelEspelho WHERE TemplateId=@tid", conn))
            {
                cmd.Parameters.AddWithValue("@tid", templateId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var e = new LdpExcelEspelhoDto
                    {
                        ExcelEspelhoId = reader.GetInt32(0),
                        TemplateId = reader.GetInt32(1),
                        NomeModelo = reader.GetString(2),
                        ArquivoReferencia = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Ativo = reader.GetBoolean(4),
                    };
                    espelhos[e.ExcelEspelhoId] = e;
                }
            }

            if (espelhos.Count == 0) return new List<LdpExcelEspelhoDto>(espelhos.Values);

            using (var cmd = new SqlCommand(
                @"SELECT a.ExcelEspelhoAbaId, a.ExcelEspelhoId, a.TemplateAbaId, a.Nome_Aba_Fisica, a.Linha_Cabecalho, a.Coluna_Inicial, a.Ordem
                  FROM ldp.ExcelEspelhoAba a
                  JOIN ldp.ExcelEspelho e ON e.ExcelEspelhoId = a.ExcelEspelhoId
                  WHERE e.TemplateId=@tid", conn))
            {
                cmd.Parameters.AddWithValue("@tid", templateId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var aba = new LdpExcelEspelhoAbaDto
                    {
                        ExcelEspelhoAbaId = reader.GetInt32(0),
                        TemplateAbaId = reader.GetInt32(2),
                        NomeAbaFisica = reader.GetString(3),
                        LinhaCabecalho = reader.GetInt32(4),
                        ColunaInicial = reader.GetInt32(5),
                        Ordem = reader.GetInt32(6),
                    };
                    int espelhoId = reader.GetInt32(1);
                    abasPorId[aba.ExcelEspelhoAbaId] = aba;
                    if (espelhos.TryGetValue(espelhoId, out var e)) e.Abas.Add(aba);
                }
            }

            using (var cmd = new SqlCommand(
                @"SELECT c.ExcelEspelhoCampoId, c.ExcelEspelhoAbaId, c.TemplateCampoId, c.Ordem
                  FROM ldp.ExcelEspelhoCampo c
                  JOIN ldp.ExcelEspelhoAba a ON a.ExcelEspelhoAbaId = c.ExcelEspelhoAbaId
                  JOIN ldp.ExcelEspelho e ON e.ExcelEspelhoId = a.ExcelEspelhoId
                  WHERE e.TemplateId=@tid", conn))
            {
                cmd.Parameters.AddWithValue("@tid", templateId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var campo = new LdpExcelEspelhoCampoDto
                    {
                        ExcelEspelhoCampoId = reader.GetInt32(0),
                        TemplateCampoId = reader.GetInt32(2),
                        Ordem = reader.GetInt32(3),
                    };
                    int abaId = reader.GetInt32(1);
                    if (abasPorId.TryGetValue(abaId, out var aba)) aba.Campos.Add(campo);
                }
            }

            return new List<LdpExcelEspelhoDto>(espelhos.Values);
        }

        public int CreateEspelho(int templateId, string nomeModelo, string arquivoReferencia)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "INSERT INTO ldp.ExcelEspelho (TemplateId, Nome_Modelo, Arquivo_Referencia) OUTPUT INSERTED.ExcelEspelhoId VALUES (@tid, @nome, @arq)", conn);
            cmd.Parameters.AddWithValue("@tid", templateId);
            cmd.Parameters.AddWithValue("@nome", nomeModelo);
            cmd.Parameters.AddWithValue("@arq", string.IsNullOrEmpty(arquivoReferencia) ? (object)DBNull.Value : arquivoReferencia);
            return (int)cmd.ExecuteScalar();
        }

        public void ReplaceEspelhoAbas(int espelhoId, List<LdpExcelEspelhoAbaDto> abas)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand(
                    "DELETE FROM ldp.ExcelEspelhoCampo WHERE ExcelEspelhoAbaId IN (SELECT ExcelEspelhoAbaId FROM ldp.ExcelEspelhoAba WHERE ExcelEspelhoId=@id)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", espelhoId);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqlCommand("DELETE FROM ldp.ExcelEspelhoAba WHERE ExcelEspelhoId=@id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", espelhoId);
                    cmd.ExecuteNonQuery();
                }

                foreach (var aba in abas)
                {
                    int abaId;
                    using (var cmd = new SqlCommand(
                        @"INSERT INTO ldp.ExcelEspelhoAba (ExcelEspelhoId, TemplateAbaId, Nome_Aba_Fisica, Linha_Cabecalho, Coluna_Inicial, Ordem)
                          OUTPUT INSERTED.ExcelEspelhoAbaId
                          VALUES (@id, @tabaId, @nome, @linha, @coluna, @ordem)", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@id", espelhoId);
                        cmd.Parameters.AddWithValue("@tabaId", aba.TemplateAbaId);
                        cmd.Parameters.AddWithValue("@nome", aba.NomeAbaFisica);
                        cmd.Parameters.AddWithValue("@linha", aba.LinhaCabecalho);
                        cmd.Parameters.AddWithValue("@coluna", aba.ColunaInicial);
                        cmd.Parameters.AddWithValue("@ordem", aba.Ordem);
                        abaId = (int)cmd.ExecuteScalar();
                    }

                    foreach (var campo in aba.Campos)
                    {
                        using var cmd = new SqlCommand(
                            "INSERT INTO ldp.ExcelEspelhoCampo (ExcelEspelhoAbaId, TemplateCampoId, Ordem) VALUES (@abaId, @campoId, @ordem)", conn, tx);
                        cmd.Parameters.AddWithValue("@abaId", abaId);
                        cmd.Parameters.AddWithValue("@campoId", campo.TemplateCampoId);
                        cmd.Parameters.AddWithValue("@ordem", campo.Ordem);
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void UpdateEspelhoArquivoReferencia(int espelhoId, string arquivoReferencia)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("UPDATE ldp.ExcelEspelho SET Arquivo_Referencia=@arq WHERE ExcelEspelhoId=@id", conn);
            cmd.Parameters.AddWithValue("@arq", arquivoReferencia);
            cmd.Parameters.AddWithValue("@id", espelhoId);
            cmd.ExecuteNonQuery();
        }

        public void DeleteEspelho(int espelhoId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("DELETE FROM ldp.ExcelEspelho WHERE ExcelEspelhoId=@id", conn);
            cmd.Parameters.AddWithValue("@id", espelhoId);
            cmd.ExecuteNonQuery();
        }

        #endregion

        #region Excel Controle

        public List<LdpExcelControleDto> GetControles(int templateId)
        {
            var controles = new Dictionary<int, LdpExcelControleDto>();
            var abasPorId = new Dictionary<int, LdpExcelControleAbaDto>();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            using (var cmd = new SqlCommand("SELECT ExcelControleId, TemplateId, Nome_Configuracao, Ativo FROM ldp.ExcelControle WHERE TemplateId=@tid", conn))
            {
                cmd.Parameters.AddWithValue("@tid", templateId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var c = new LdpExcelControleDto
                    {
                        ExcelControleId = reader.GetInt32(0),
                        TemplateId = reader.GetInt32(1),
                        NomeConfiguracao = reader.GetString(2),
                        Ativo = reader.GetBoolean(3),
                    };
                    controles[c.ExcelControleId] = c;
                }
            }

            if (controles.Count == 0) return new List<LdpExcelControleDto>(controles.Values);

            using (var cmd = new SqlCommand(
                @"SELECT a.ExcelControleAbaId, a.ExcelControleId, a.TemplateAbaId, a.Nome_Aba_Exibido, a.Ordem
                  FROM ldp.ExcelControleAba a
                  JOIN ldp.ExcelControle c ON c.ExcelControleId = a.ExcelControleId
                  WHERE c.TemplateId=@tid", conn))
            {
                cmd.Parameters.AddWithValue("@tid", templateId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var aba = new LdpExcelControleAbaDto
                    {
                        ExcelControleAbaId = reader.GetInt32(0),
                        TemplateAbaId = reader.GetInt32(2),
                        NomeAbaExibido = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Ordem = reader.GetInt32(4),
                    };
                    int controleId = reader.GetInt32(1);
                    abasPorId[aba.ExcelControleAbaId] = aba;
                    if (controles.TryGetValue(controleId, out var c)) c.Abas.Add(aba);
                }
            }

            using (var cmd = new SqlCommand(
                @"SELECT c.ExcelControleCampoId, c.ExcelControleAbaId, c.TemplateCampoId, c.Nome_Exibido_Override, c.Ordem
                  FROM ldp.ExcelControleCampo c
                  JOIN ldp.ExcelControleAba a ON a.ExcelControleAbaId = c.ExcelControleAbaId
                  JOIN ldp.ExcelControle ctl ON ctl.ExcelControleId = a.ExcelControleId
                  WHERE ctl.TemplateId=@tid", conn))
            {
                cmd.Parameters.AddWithValue("@tid", templateId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var campo = new LdpExcelControleCampoDto
                    {
                        ExcelControleCampoId = reader.GetInt32(0),
                        TemplateCampoId = reader.GetInt32(2),
                        NomeExibidoOverride = reader.IsDBNull(3) ? null : reader.GetString(3),
                        Ordem = reader.GetInt32(4),
                    };
                    int abaId = reader.GetInt32(1);
                    if (abasPorId.TryGetValue(abaId, out var aba)) aba.Campos.Add(campo);
                }
            }

            return new List<LdpExcelControleDto>(controles.Values);
        }

        public int CreateControle(int templateId, string nomeConfiguracao)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                "INSERT INTO ldp.ExcelControle (TemplateId, Nome_Configuracao) OUTPUT INSERTED.ExcelControleId VALUES (@tid, @nome)", conn);
            cmd.Parameters.AddWithValue("@tid", templateId);
            cmd.Parameters.AddWithValue("@nome", nomeConfiguracao);
            return (int)cmd.ExecuteScalar();
        }

        public void ReplaceControleAbas(int controleId, List<LdpExcelControleAbaDto> abas)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand(
                    "DELETE FROM ldp.ExcelControleCampo WHERE ExcelControleAbaId IN (SELECT ExcelControleAbaId FROM ldp.ExcelControleAba WHERE ExcelControleId=@id)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", controleId);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqlCommand("DELETE FROM ldp.ExcelControleAba WHERE ExcelControleId=@id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", controleId);
                    cmd.ExecuteNonQuery();
                }

                foreach (var aba in abas)
                {
                    int abaId;
                    using (var cmd = new SqlCommand(
                        @"INSERT INTO ldp.ExcelControleAba (ExcelControleId, TemplateAbaId, Nome_Aba_Exibido, Ordem)
                          OUTPUT INSERTED.ExcelControleAbaId
                          VALUES (@id, @tabaId, @nome, @ordem)", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@id", controleId);
                        cmd.Parameters.AddWithValue("@tabaId", aba.TemplateAbaId);
                        cmd.Parameters.AddWithValue("@nome", string.IsNullOrEmpty(aba.NomeAbaExibido) ? (object)DBNull.Value : aba.NomeAbaExibido);
                        cmd.Parameters.AddWithValue("@ordem", aba.Ordem);
                        abaId = (int)cmd.ExecuteScalar();
                    }

                    foreach (var campo in aba.Campos)
                    {
                        using var cmd = new SqlCommand(
                            @"INSERT INTO ldp.ExcelControleCampo (ExcelControleAbaId, TemplateCampoId, Nome_Exibido_Override, Ordem)
                              VALUES (@abaId, @campoId, @override, @ordem)", conn, tx);
                        cmd.Parameters.AddWithValue("@abaId", abaId);
                        cmd.Parameters.AddWithValue("@campoId", campo.TemplateCampoId);
                        cmd.Parameters.AddWithValue("@override", string.IsNullOrEmpty(campo.NomeExibidoOverride) ? (object)DBNull.Value : campo.NomeExibidoOverride);
                        cmd.Parameters.AddWithValue("@ordem", campo.Ordem);
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void DeleteControle(int controleId)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("DELETE FROM ldp.ExcelControle WHERE ExcelControleId=@id", conn);
            cmd.Parameters.AddWithValue("@id", controleId);
            cmd.ExecuteNonQuery();
        }

        #endregion

        #region Estrutura do Projeto (Projeto Único x Projeto por Fase)

        public LdpProjetoConfiguracaoDto GetProjetoConfiguracao(string projectIdAcc)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            LdpProjetoConfiguracaoDto config = null;
            using (var cmd = new SqlCommand(
                @"SELECT Project_ID_ACC, ModoEstrutura, ModoGeracaoFase, TemplateId, CronogramaMapeamentoId, ExcelEspelhoId, ExcelControleId
                  FROM ldp.ProjetoConfiguracao WHERE Project_ID_ACC=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", projectIdAcc);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    config = new LdpProjetoConfiguracaoDto
                    {
                        ProjectIdAcc = reader.GetString(0),
                        ModoEstrutura = reader.GetString(1),
                        ModoGeracaoFase = reader.IsDBNull(2) ? null : reader.GetString(2),
                        TemplateId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                        CronogramaMapeamentoId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        ExcelEspelhoId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        ExcelControleId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    };
                }
            }

            if (config == null) return null;

            var fasesPorId = new Dictionary<int, LdpProjetoFaseDto>();
            using (var cmd = new SqlCommand(
                @"SELECT ProjetoFaseId, Codigo, Nome, Ordem, Ativa, OperadorLogico,
                         TemplateIdOverride, CronogramaMapeamentoIdOverride, ExcelEspelhoIdOverride, ExcelControleIdOverride
                  FROM ldp.ProjetoFase WHERE Project_ID_ACC=@id ORDER BY Ordem", conn))
            {
                cmd.Parameters.AddWithValue("@id", projectIdAcc);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var fase = new LdpProjetoFaseDto
                    {
                        ProjetoFaseId = reader.GetInt32(0),
                        Codigo = reader.GetString(1),
                        Nome = reader.GetString(2),
                        Ordem = reader.GetInt32(3),
                        Ativa = reader.GetBoolean(4),
                        OperadorLogico = reader.GetString(5),
                        TemplateIdOverride = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        CronogramaMapeamentoIdOverride = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        ExcelEspelhoIdOverride = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        ExcelControleIdOverride = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    };
                    fasesPorId[fase.ProjetoFaseId] = fase;
                    config.Fases.Add(fase);
                }
            }

            if (fasesPorId.Count > 0)
            {
                using var cmd = new SqlCommand(
                    @"SELECT r.ProjetoFaseRegraId, r.ProjetoFaseId, r.CampoOrigem, r.Operador, r.ValorComparacao, r.Ordem, r.Ativa
                      FROM ldp.ProjetoFaseRegra r
                      JOIN ldp.ProjetoFase f ON f.ProjetoFaseId = r.ProjetoFaseId
                      WHERE f.Project_ID_ACC=@id ORDER BY r.Ordem", conn);
                cmd.Parameters.AddWithValue("@id", projectIdAcc);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var regra = new LdpProjetoFaseRegraDto
                    {
                        ProjetoFaseRegraId = reader.GetInt32(0),
                        CampoOrigem = reader.GetString(2),
                        Operador = reader.GetString(3),
                        ValorComparacao = reader.GetString(4),
                        Ordem = reader.GetInt32(5),
                        Ativa = reader.GetBoolean(6),
                    };
                    int faseId = reader.GetInt32(1);
                    if (fasesPorId.TryGetValue(faseId, out var fase)) fase.Regras.Add(regra);
                }
            }

            return config;
        }

        /// <summary>Upsert de ldp.ProjetoConfiguracao + substituição completa de Fases/Regras (mesmo padrão replace-on-save).</summary>
        public void SalvarProjetoConfiguracao(LdpProjetoConfiguracaoDto dto)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                using (var cmd = new SqlCommand(
                    @"MERGE ldp.ProjetoConfiguracao AS destino
                      USING (SELECT @id AS Project_ID_ACC) AS origem
                      ON destino.Project_ID_ACC = origem.Project_ID_ACC
                      WHEN MATCHED THEN UPDATE SET
                          ModoEstrutura = @modoEstrutura, ModoGeracaoFase = @modoGeracaoFase,
                          TemplateId = @templateId, CronogramaMapeamentoId = @mapeamentoId,
                          ExcelEspelhoId = @espelhoId, ExcelControleId = @controleId,
                          Data_Atualizacao = SYSUTCDATETIME()
                      WHEN NOT MATCHED THEN INSERT
                          (Project_ID_ACC, ModoEstrutura, ModoGeracaoFase, TemplateId, CronogramaMapeamentoId, ExcelEspelhoId, ExcelControleId)
                          VALUES (@id, @modoEstrutura, @modoGeracaoFase, @templateId, @mapeamentoId, @espelhoId, @controleId);", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", dto.ProjectIdAcc);
                    cmd.Parameters.AddWithValue("@modoEstrutura", dto.ModoEstrutura);
                    cmd.Parameters.AddWithValue("@modoGeracaoFase", string.IsNullOrEmpty(dto.ModoGeracaoFase) ? (object)DBNull.Value : dto.ModoGeracaoFase);
                    cmd.Parameters.AddWithValue("@templateId", dto.TemplateId.HasValue ? (object)dto.TemplateId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@mapeamentoId", dto.CronogramaMapeamentoId.HasValue ? (object)dto.CronogramaMapeamentoId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@espelhoId", dto.ExcelEspelhoId.HasValue ? (object)dto.ExcelEspelhoId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@controleId", dto.ExcelControleId.HasValue ? (object)dto.ExcelControleId.Value : DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SqlCommand(
                    "DELETE FROM ldp.ProjetoFaseRegra WHERE ProjetoFaseId IN (SELECT ProjetoFaseId FROM ldp.ProjetoFase WHERE Project_ID_ACC=@id)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", dto.ProjectIdAcc);
                    cmd.ExecuteNonQuery();
                }
                using (var cmd = new SqlCommand("DELETE FROM ldp.ProjetoFase WHERE Project_ID_ACC=@id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", dto.ProjectIdAcc);
                    cmd.ExecuteNonQuery();
                }

                foreach (var fase in dto.Fases)
                {
                    int faseId;
                    using (var cmd = new SqlCommand(
                        @"INSERT INTO ldp.ProjetoFase
                            (Project_ID_ACC, Codigo, Nome, Ordem, Ativa, OperadorLogico,
                             TemplateIdOverride, CronogramaMapeamentoIdOverride, ExcelEspelhoIdOverride, ExcelControleIdOverride)
                          OUTPUT INSERTED.ProjetoFaseId
                          VALUES (@id, @codigo, @nome, @ordem, @ativa, @operadorLogico,
                                  @templateOverride, @mapeamentoOverride, @espelhoOverride, @controleOverride)", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@id", dto.ProjectIdAcc);
                        cmd.Parameters.AddWithValue("@codigo", fase.Codigo);
                        cmd.Parameters.AddWithValue("@nome", fase.Nome);
                        cmd.Parameters.AddWithValue("@ordem", fase.Ordem);
                        cmd.Parameters.AddWithValue("@ativa", fase.Ativa);
                        cmd.Parameters.AddWithValue("@operadorLogico", fase.OperadorLogico);
                        cmd.Parameters.AddWithValue("@templateOverride", fase.TemplateIdOverride.HasValue ? (object)fase.TemplateIdOverride.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@mapeamentoOverride", fase.CronogramaMapeamentoIdOverride.HasValue ? (object)fase.CronogramaMapeamentoIdOverride.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@espelhoOverride", fase.ExcelEspelhoIdOverride.HasValue ? (object)fase.ExcelEspelhoIdOverride.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@controleOverride", fase.ExcelControleIdOverride.HasValue ? (object)fase.ExcelControleIdOverride.Value : DBNull.Value);
                        faseId = (int)cmd.ExecuteScalar();
                    }

                    foreach (var regra in fase.Regras)
                    {
                        using var cmd = new SqlCommand(
                            @"INSERT INTO ldp.ProjetoFaseRegra (ProjetoFaseId, CampoOrigem, Operador, ValorComparacao, Ordem, Ativa)
                              VALUES (@faseId, @campo, @operador, @valor, @ordem, @ativa)", conn, tx);
                        cmd.Parameters.AddWithValue("@faseId", faseId);
                        cmd.Parameters.AddWithValue("@campo", regra.CampoOrigem);
                        cmd.Parameters.AddWithValue("@operador", regra.Operador);
                        cmd.Parameters.AddWithValue("@valor", regra.ValorComparacao);
                        cmd.Parameters.AddWithValue("@ordem", regra.Ordem);
                        cmd.Parameters.AddWithValue("@ativa", regra.Ativa);
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void DeleteProjetoConfiguracao(string projectIdAcc)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("DELETE FROM ldp.ProjetoConfiguracao WHERE Project_ID_ACC=@id", conn);
            cmd.Parameters.AddWithValue("@id", projectIdAcc);
            cmd.ExecuteNonQuery();
        }

        #endregion
    }
}
