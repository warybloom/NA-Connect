using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using APS.WebApp.Models;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Implementação do preparo de UMA tabela de Qualidade por vez (seções 4/11/12 do pedido de
    /// preenchimento automático). Ordem de classificação de cada linha física:
    /// 1) totalmente vazia -&gt; ignorada; 2) só placeholders/ausências nos campos mapeados (mesmo com
    /// alguma célula solta fora do mapa) -&gt; ignorada; 3) tem pelo menos um conteúdo real -&gt; linha de
    /// negócio: campo textual obrigatório vazio recebe "FALTA INFORMAÇÃO" (nunca bloqueia a linha),
    /// campo tipado (data/inteiro) com texto inválido rejeita SÓ aquela linha (nunca a aba inteira).
    /// </summary>
    public sealed class QualidadePreflightService : IQualidadePreflightService
    {
        private readonly ILogger<QualidadePreflightService> _logger;

        public QualidadePreflightService(ILogger<QualidadePreflightService> logger)
        {
            _logger = logger;
        }

        public ResultadoTabelaQualidade PrepararTabela(ExcelPackage package, QualidadeTabelaDefinicao definicao, string projectId)
        {
            var resultado = new ResultadoTabelaQualidade
            {
                Aba = definicao.NomeAba,
                TabelaDestino = definicao.TabelaDestino
            };

            _logger.LogInformation(
                "[QUALIDADE][PREFLIGHT][INICIO] Aba={Aba} | TabelaDestino={TabelaDestino} | Project_ID_ACC={ProjectIdAcc}",
                definicao.NomeAba, definicao.TabelaDestino, projectId);

            var ws = QualidadeAbaLocator.LocalizarAbaUnica(package, definicao.NomeAba, out string erroLocalizacao);
            if (ws == null)
            {
                resultado.Sucesso = false;
                resultado.CodigoResultado = QualidadeCodigosResultado.ErroEstrutural;
                resultado.Mensagem = erroLocalizacao;
                resultado.Erros.Add(erroLocalizacao);

                _logger.LogError(
                    "[QUALIDADE][PREFLIGHT][ERRO] Aba={Aba} | Motivo=ABA_AUSENTE | {Mensagem}",
                    definicao.NomeAba, erroLocalizacao);

                return resultado;
            }

            QualidadeCabecalhoResolvido cabecalho;
            try
            {
                cabecalho = QualidadeHeaderResolver.Resolver(ws, definicao.NomeAba, definicao.Aliases, definicao.CamposMinimosCabecalho, _logger);
            }
            catch (QualidadeLayoutException ex)
            {
                resultado.Sucesso = false;
                resultado.CodigoResultado = QualidadeCodigosResultado.ErroEstrutural;
                resultado.Mensagem = $"{ex.CodigoResultado}: {ex.Message}";
                resultado.Erros.Add(resultado.Mensagem);

                _logger.LogError(
                    "[QUALIDADE][PREFLIGHT][ERRO] Aba={Aba} | Motivo={Motivo} | {Mensagem}",
                    ws.Name, ex.CodigoResultado, ex.Message);

                return resultado;
            }

            resultado.HeaderRow = cabecalho.HeaderRow;
            resultado.CamposMapeados = cabecalho.Mapa.Keys.OrderBy(k => k).ToList();
            resultado.CamposAusentes = definicao.Aliases.Keys.Except(cabecalho.Mapa.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();

            _logger.LogInformation(
                "[QUALIDADE][PREFLIGHT][ABA] Aba={Aba} | HeaderRow={HeaderRow} | CamposMapeados=[{Mapeados}] | CamposAusentes=[{Ausentes}]",
                ws.Name, cabecalho.HeaderRow, string.Join(", ", resultado.CamposMapeados), string.Join(", ", resultado.CamposAusentes));

            foreach (var campo in cabecalho.Mapa)
            {
                string cabecalhoRealEncontrado = ws.Cells[cabecalho.HeaderRow, campo.Value].Text?.Trim();
                bool obrigatorio = definicao.CamposObrigatoriosNaLinha.Contains(campo.Key);

                _logger.LogInformation(
                    "[QUALIDADE][MAPA_CABECALHOS] Aba={Aba} | CampoLogico={Campo} | CabecalhoEncontrado={CabecalhoEncontrado} | ColunaFisica={ColunaFisica} | ColunaSql={ColunaSql} | Obrigatorio={Obrigatorio}",
                    ws.Name, campo.Key, cabecalhoRealEncontrado, campo.Value, campo.Key, obrigatorio ? "sim" : "nao");
            }

            var faltandoObrigatorios = definicao.CamposObrigatoriosNaLinha.Where(c => !cabecalho.Mapa.ContainsKey(c)).ToList();
            if (faltandoObrigatorios.Count > 0)
            {
                resultado.Sucesso = false;
                resultado.CodigoResultado = QualidadeCodigosResultado.ErroEstrutural;
                resultado.Mensagem = $"Campos obrigatórios não mapeados no cabeçalho: [{string.Join(", ", faltandoObrigatorios)}].";
                resultado.Erros.Add(resultado.Mensagem);

                _logger.LogError(
                    "[QUALIDADE][PREFLIGHT][ERRO] Aba={Aba} | Motivo=CAMPOS_OBRIGATORIOS_NAO_MAPEADOS | Campos=[{Campos}]",
                    ws.Name, string.Join(", ", faltandoObrigatorios));

                return resultado;
            }

            PrepararLinhas(ws, definicao, cabecalho, projectId, resultado);

            bool temAlertas = resultado.LinhasComPreenchimentoAutomatico > 0;
            resultado.Sucesso = true;

            if (resultado.LinhasValidas == 0)
            {
                resultado.CodigoResultado = QualidadeCodigosResultado.SemLinhasValidas;
                resultado.Mensagem = "SEM_LINHAS_VALIDAS_DADOS_ANTERIORES_PRESERVADOS";
                resultado.Tabela = null;

                _logger.LogWarning(
                    "[QUALIDADE][PREFLIGHT] Aba={Aba} | {CodigoResultado} — nenhum DELETE será executado, snapshot anterior preservado.",
                    ws.Name, resultado.CodigoResultado);
            }
            else
            {
                resultado.SucessoComAlertas = temAlertas;
                resultado.CodigoResultado = temAlertas ? QualidadeCodigosResultado.SucessoComAlertas : QualidadeCodigosResultado.Sucesso;
                resultado.Mensagem = temAlertas
                    ? $"{resultado.LinhasValidas} linha(s) preparada(s), {resultado.LinhasComPreenchimentoAutomatico} com preenchimento automático ({resultado.CamposPreenchidosAutomaticamente} campo(s))."
                    : $"{resultado.LinhasValidas} linha(s) preparada(s).";
            }

            int linhasValidasSemAjuste = resultado.LinhasValidas - resultado.LinhasComPreenchimentoAutomatico;

            _logger.LogInformation(
                "[QUALIDADE][RESUMO_ABA] Aba={Aba} | LinhasFisicas={LinhasFisicas} | LinhasVazias={LinhasVazias} | LinhasPlaceholder={LinhasPlaceholder} | " +
                "LinhasValidasSemAjuste={LinhasValidasSemAjuste} | LinhasValidasComPreenchimentoAutomatico={LinhasComAjuste} | " +
                "CamposPreenchidosAutomaticamente={CamposPreenchidos} | LinhasRejeitadas={LinhasRejeitadas} | Resultado={Resultado}",
                ws.Name, resultado.LinhasFisicas, resultado.LinhasVazias, resultado.LinhasPlaceholder,
                linhasValidasSemAjuste, resultado.LinhasComPreenchimentoAutomatico, resultado.CamposPreenchidosAutomaticamente,
                resultado.LinhasRejeitadas, resultado.CodigoResultado);

            return resultado;
        }

        private void PrepararLinhas(
            ExcelWorksheet ws,
            QualidadeTabelaDefinicao definicao,
            QualidadeCabecalhoResolvido cabecalho,
            string projectId,
            ResultadoTabelaQualidade resultado)
        {
            var dt = new DataTable();
            dt.Columns.Add("Project_ID_ACC", typeof(string));
            dt.Columns.Add("Data_Sincronizacao", typeof(DateTime));

            foreach (var campo in cabecalho.Mapa.Keys)
            {
                Type tipo = definicao.CamposData.Contains(campo) ? typeof(DateTime)
                    : (definicao.CamposInteiros.Contains(campo) || definicao.CamposMes.Contains(campo)) ? typeof(int)
                    : typeof(string);
                dt.Columns.Add(campo, tipo);
            }

            int startRow = cabecalho.HeaderRow + 1;
            int endRow = ws.Dimension?.End.Row ?? 0;

            resultado.LinhasFisicas = Math.Max(0, endRow - startRow + 1);

            DateTime syncTime = DateTime.Now;

            for (int row = startRow; row <= endRow; row++)
            {
                var celulas = cabecalho.Mapa.ToDictionary(
                    kv => kv.Key,
                    kv => ws.Cells[row, kv.Value],
                    StringComparer.OrdinalIgnoreCase);

                // 1) Linha completamente vazia (nenhuma célula mapeada com qualquer texto) — ignorada,
                // nunca preenchida com "FALTA INFORMAÇÃO".
                bool linhaTotalmenteVazia = celulas.Values.All(c => string.IsNullOrWhiteSpace(c.Text));
                if (linhaTotalmenteVazia)
                {
                    resultado.LinhasVazias++;
                    _logger.LogDebug("[QUALIDADE][LINHA_IGNORADA] Aba={Aba} | Linha={Linha} | Motivo=VAZIA", ws.Name, row);
                    continue;
                }

                // 2) Linha placeholder/só-formatada: nenhuma célula mapeada tem conteúdo REAL (blank ou
                // token tipo "-"/"N/A"), mesmo que alguma célula fora do mapa tenha algo — ignorada,
                // nunca preenchida.
                bool nenhumConteudoReal = celulas.Values.All(c => QualidadeNormalizador.EhValorAusente(c.Text));
                if (nenhumConteudoReal)
                {
                    resultado.LinhasPlaceholder++;
                    _logger.LogDebug("[QUALIDADE][LINHA_IGNORADA] Aba={Aba} | Linha={Linha} | Motivo=PLACEHOLDER", ws.Name, row);
                    continue;
                }

                // 3) Linha de negócio real — preenche campos textuais obrigatórios vazios, valida os
                // demais tipos.
                var valoresPorCampo = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                var motivosRejeicao = new List<string>();
                var camposPreenchidosNaLinha = new List<Models.QualidadeCampoPreenchidoAutomaticamente>();

                foreach (var campo in cabecalho.Mapa)
                {
                    var cell = celulas[campo.Key];

                    if (definicao.CamposMes.Contains(campo.Key))
                    {
                        QualidadeParsers.TentarParseMes(cell.Text, out int? mes, out bool naoReconhecido);
                        valoresPorCampo[campo.Key] = (object)mes ?? DBNull.Value;

                        if (naoReconhecido)
                        {
                            _logger.LogWarning(
                                "[QUALIDADE][CAMPO_CONVERSAO_NAO_RECONHECIDA] Aba={Aba} | Linha={Linha} | Campo={Campo} | ValorBruto={ValorBruto} | Acao=GRAVADO_COMO_NULL",
                                ws.Name, row, campo.Key, cell.Text);
                        }
                    }
                    else if (definicao.CamposData.Contains(campo.Key))
                    {
                        if (QualidadeParsers.TentarParseData(cell, out DateTime? data, out string erroData))
                        {
                            valoresPorCampo[campo.Key] = (object)data ?? DBNull.Value;
                        }
                        else
                        {
                            motivosRejeicao.Add($"{campo.Key}: {erroData}");
                        }
                    }
                    else if (definicao.CamposInteiros.Contains(campo.Key))
                    {
                        string texto = cell.Text?.Trim();
                        if (QualidadeParsers.TentarParseInteiro(texto, out int? inteiro, out string erroInteiro))
                        {
                            valoresPorCampo[campo.Key] = (object)inteiro ?? DBNull.Value;
                        }
                        else
                        {
                            motivosRejeicao.Add($"{campo.Key}: {erroInteiro}");
                        }
                    }
                    else
                    {
                        string valor = cell.Text?.Trim();
                        bool obrigatorio = definicao.CamposObrigatoriosNaLinha.Contains(campo.Key);

                        if (QualidadeNormalizador.EhValorAusente(valor))
                        {
                            if (obrigatorio)
                            {
                                valoresPorCampo[campo.Key] = QualidadeCabecalhos.ValorCampoObrigatorioAusente;

                                camposPreenchidosNaLinha.Add(new Models.QualidadeCampoPreenchidoAutomaticamente
                                {
                                    Aba = ws.Name,
                                    NumeroLinha = row,
                                    CampoLogico = campo.Key,
                                    ColunaSql = campo.Key,
                                    ValorAplicado = QualidadeCabecalhos.ValorCampoObrigatorioAusente
                                });

                                _logger.LogWarning(
                                    "[QUALIDADE][CAMPO_PREENCHIDO_AUTOMATICAMENTE] Aba={Aba} | Linha={Linha} | Campo={Campo} | ColunaSql={ColunaSql} | ValorAplicado={ValorAplicado} | Acao=LINHA_MANTIDA",
                                    ws.Name, row, campo.Key, campo.Key, QualidadeCabecalhos.ValorCampoObrigatorioAusente);
                            }
                            else
                            {
                                // Campo opcional vazio permanece NULL — nunca poluído com "FALTA INFORMAÇÃO".
                                valoresPorCampo[campo.Key] = DBNull.Value;
                            }
                        }
                        else
                        {
                            valoresPorCampo[campo.Key] = valor;
                        }
                    }
                }

                if (motivosRejeicao.Count > 0)
                {
                    resultado.LinhasRejeitadas++;
                    string detalhe = $"Linha {row}: [{string.Join("; ", motivosRejeicao)}]";
                    resultado.Erros.Add(detalhe);

                    _logger.LogWarning(
                        "[QUALIDADE][LINHA_REJEITADA] Aba={Aba} | {Detalhe} | Acao=LINHA_EXCLUIDA_DEMAIS_MANTIDAS",
                        ws.Name, detalhe);

                    continue;
                }

                var dr = dt.NewRow();
                dr["Project_ID_ACC"] = projectId;
                dr["Data_Sincronizacao"] = syncTime;
                foreach (var campo in cabecalho.Mapa.Keys)
                {
                    dr[campo] = valoresPorCampo.TryGetValue(campo, out var valor) ? valor : DBNull.Value;
                }
                dt.Rows.Add(dr);

                if (camposPreenchidosNaLinha.Count > 0)
                {
                    resultado.LinhasComPreenchimentoAutomatico++;
                    resultado.CamposPreenchidosAutomaticamente += camposPreenchidosNaLinha.Count;
                    resultado.CamposPreenchidosDetalhe.AddRange(camposPreenchidosNaLinha);
                }
            }

            resultado.LinhasValidas = dt.Rows.Count;
            resultado.Tabela = dt;
        }
    }
}
