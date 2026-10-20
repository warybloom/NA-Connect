using System;
using System.Collections.Generic;

namespace APS.WebApp.Services.OperationalAudit.Safety
{
    /// <summary>
    /// Detecta, a partir do TEXTO de um comando SQL, se ele é (ou contém) uma operação de escrita (seção 18
    /// da correção da Fase 2: INSERT/UPDATE/DELETE/MERGE/TRUNCATE/ALTER/CREATE/DROP, ou EXEC de procedure —
    /// tratado como escrita por padrão, já que não há como confirmar de fora que uma procedure é
    /// somente-leitura). Puro — só inspeciona texto, nunca executa nada.
    ///
    /// Não é um parser SQL completo (deliberadamente — seção 6 da correção de Checkpoint 1): é um scanner
    /// lexical que (a) separa o lote em instruções por ponto-e-vírgula fora de strings/comentários/colchetes,
    /// (b) para cada instrução, reconhece o verbo principal — pulando por cima do prólogo de um CTE
    /// (<c>WITH nome [(...)] AS (...), ...</c>) quando presente — e (c) bloqueia o lote inteiro se QUALQUER
    /// instrução tiver um verbo de escrita. Um <c>SELECT 1; DELETE FROM Foo;</c> é bloqueado mesmo que a
    /// primeira instrução seja inofensiva — não basta inspecionar só a primeira palavra do texto inteiro.
    ///
    /// Esta é uma camada de defesa EM PROFUNDIDADE (falha explícita e imediata no código), nunca a única
    /// proteção — a proteção real e definitiva é a permissão de banco da <c>AuditSourceConnection</c>
    /// (db_datareader, seção 4). Um comando que passe por aqui sem ser sinalizado ainda pode ser rejeitado
    /// pelo SQL Server por falta de permissão; o inverso nunca deveria acontecer.
    /// </summary>
    public static class SqlWriteVerbGuard
    {
        private static readonly string[] VerbosDeEscrita =
        {
            "INSERT", "UPDATE", "DELETE", "MERGE", "TRUNCATE", "ALTER", "CREATE", "DROP", "EXEC", "EXECUTE"
        };

        /// <summary>
        /// Procedures explicitamente conhecidas como somente-leitura (catálogo de sistema) — única exceção
        /// ao tratamento padrão de EXEC como escrita.
        /// </summary>
        private static readonly string[] ProceduresDeLeituraConhecidas =
        {
            "SP_HELP", "SP_COLUMNS", "SP_TABLES"
        };

        public static bool EhComandoDeEscrita(string? sqlText)
        {
            if (string.IsNullOrWhiteSpace(sqlText)) return false;

            foreach (string instrucao in SepararInstrucoes(sqlText))
            {
                if (InstrucaoEhDeEscrita(instrucao)) return true;
            }
            return false;
        }

        public static void GarantirSomenteLeitura(string? sqlText)
        {
            if (EhComandoDeEscrita(sqlText))
            {
                throw new ForbiddenOperationException(
                    $"[OperationalAudit] Comando bloqueado na conexão somente-leitura (AuditSourceConnection): '{Resumir(sqlText)}'.");
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Avaliação de UMA instrução (já separada do lote).
        // ---------------------------------------------------------------------------------------------

        private static bool InstrucaoEhDeEscrita(string instrucao)
        {
            var (primeiroToken, indexApos) = LerProximoToken(instrucao, 0);
            if (primeiroToken.Length == 0) return false;

            string verbo = primeiroToken.ToUpperInvariant();

            if (verbo == "WITH")
            {
                int posDepoisDoCte = PularDefinicoesCte(instrucao, indexApos);
                var (verboReal, indexAposVerboReal) = LerProximoToken(instrucao, posDepoisDoCte);
                return AvaliarVerbo(verboReal, instrucao, indexAposVerboReal);
            }

            return AvaliarVerbo(primeiroToken, instrucao, indexApos);
        }

        private static bool AvaliarVerbo(string tokenBruto, string instrucao, int indexApos)
        {
            if (tokenBruto.Length == 0) return false;
            string verbo = tokenBruto.ToUpperInvariant();

            if (verbo is "EXEC" or "EXECUTE")
            {
                var (procTokenBruto, _) = LerProximoToken(instrucao, indexApos);
                string procName = procTokenBruto.Trim('[', ']').ToUpperInvariant();

                foreach (var conhecida in ProceduresDeLeituraConhecidas)
                {
                    if (procName == conhecida) return false;
                }
                return true; // EXEC de qualquer outra procedure é tratado como escrita por padrão.
            }

            foreach (var proibido in VerbosDeEscrita)
            {
                if (verbo == proibido) return true;
            }
            return false;
        }

        /// <summary>
        /// Posicionado logo após o "WITH" de um CTE, avança por uma ou mais definições
        /// <c>nome [(col1, col2, ...)] AS ( corpo )</c> separadas por vírgula, e devolve o índice do verbo
        /// DML real que vem depois (SELECT/INSERT/UPDATE/DELETE/MERGE — um CTE pode preceder qualquer um
        /// destes em T-SQL, não só SELECT). Se a sintaxe não bater com o esperado em algum ponto (CTE mal
        /// formado), devolve a posição atual sem avançar mais — comportamento conservador de "não travar",
        /// nunca de "fingir que reconheceu um verbo que não está lá".
        /// </summary>
        private static int PularDefinicoesCte(string texto, int pos)
        {
            int i = pos;
            int n = texto.Length;

            while (true)
            {
                i = PularBrancosEComentarios(texto, i);

                var (nomeCte, indexAposNome) = LerProximoToken(texto, i);
                if (nomeCte.Length == 0) return i;
                i = indexAposNome;

                i = PularBrancosEComentarios(texto, i);
                if (i < n && texto[i] == '(')
                {
                    // lista de colunas explícita do CTE: WITH cte (Col1, Col2) AS (...)
                    i = PularParenteses(texto, i);
                    i = PularBrancosEComentarios(texto, i);
                }

                var (tokenAs, indexAposAs) = LerProximoToken(texto, i);
                if (!string.Equals(tokenAs, "AS", StringComparison.OrdinalIgnoreCase)) return i;
                i = indexAposAs;

                i = PularBrancosEComentarios(texto, i);
                if (i >= n || texto[i] != '(') return i;
                i = PularParenteses(texto, i); // corpo do CTE

                i = PularBrancosEComentarios(texto, i);
                if (i < n && texto[i] == ',')
                {
                    i++; // mais um CTE segue
                    continue;
                }
                return i; // nenhum CTE a mais — i aponta para o verbo DML real
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Scanner lexical de baixo nível — usado tanto para separar instruções quanto para pular
        // parênteses balanceados. Nunca interpreta o SIGNIFICADO de nada além de string/colchete/comentário.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Separa o lote em instruções por ";" fora de strings ('...'), identificadores entre colchetes ([...]) e comentários (-- e /* */).</summary>
        private static List<string> SepararInstrucoes(string texto)
        {
            var instrucoes = new List<string>();
            int inicio = 0;
            int i = 0;
            int n = texto.Length;

            while (i < n)
            {
                if (EhInicioDeConstrucaoIgnoravel(texto, i))
                {
                    i = AvancarSobreConstrucaoIgnoravel(texto, i);
                    continue;
                }
                if (texto[i] == ';')
                {
                    instrucoes.Add(texto[inicio..i]);
                    i++;
                    inicio = i;
                    continue;
                }
                i++;
            }

            if (inicio < n) instrucoes.Add(texto[inicio..n]);
            return instrucoes;
        }

        /// <summary>Pula um bloco parentizado balanceado a partir de "texto[i] == '('" (inclusive), respeitando strings/colchetes/comentários por dentro, e devolve o índice logo após o ")" que fecha.</summary>
        private static int PularParenteses(string texto, int i)
        {
            int n = texto.Length;
            int profundidade = 0;

            while (i < n)
            {
                if (EhInicioDeConstrucaoIgnoravel(texto, i))
                {
                    i = AvancarSobreConstrucaoIgnoravel(texto, i);
                    continue;
                }
                if (texto[i] == '(') { profundidade++; i++; continue; }
                if (texto[i] == ')')
                {
                    i++;
                    profundidade--;
                    if (profundidade == 0) return i;
                    continue;
                }
                i++;
            }
            return n;
        }

        /// <summary>
        /// Pula espaços em branco e comentários (linha/bloco), em qualquer combinação e quantidade — nunca
        /// pula strings/colchetes (diferente de <see cref="EhInicioDeConstrucaoIgnoravel"/>): um "'" ou "["
        /// aqui é uma posição legítima de parada, não algo a atravessar.
        /// </summary>
        private static int PularBrancosEComentarios(string texto, int i)
        {
            int n = texto.Length;
            while (i < n)
            {
                if (char.IsWhiteSpace(texto[i])) { i++; continue; }
                bool comentarioDeLinha = texto[i] == '-' && i + 1 < n && texto[i + 1] == '-';
                bool comentarioDeBloco = texto[i] == '/' && i + 1 < n && texto[i + 1] == '*';
                if (comentarioDeLinha || comentarioDeBloco)
                {
                    i = AvancarSobreConstrucaoIgnoravel(texto, i);
                    continue;
                }
                break;
            }
            return i;
        }

        private static bool EhInicioDeConstrucaoIgnoravel(string texto, int i)
        {
            int n = texto.Length;
            char c = texto[i];
            if (c == '\'' || c == '[') return true;
            if (c == '-' && i + 1 < n && texto[i + 1] == '-') return true;
            if (c == '/' && i + 1 < n && texto[i + 1] == '*') return true;
            return false;
        }

        /// <summary>Assume que EhInicioDeConstrucaoIgnoravel(texto, i) é true; devolve o índice logo após a string/colchete/comentário que começa em i.</summary>
        private static int AvancarSobreConstrucaoIgnoravel(string texto, int i)
        {
            int n = texto.Length;
            char c = texto[i];

            if (c == '\'')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (texto[j] == '\'')
                    {
                        if (j + 1 < n && texto[j + 1] == '\'') { j += 2; continue; } // '' = aspas simples escapada
                        return j + 1;
                    }
                    j++;
                }
                return n;
            }

            if (c == '[')
            {
                int fecha = texto.IndexOf(']', i + 1);
                return fecha < 0 ? n : fecha + 1;
            }

            if (c == '-') // "--" já confirmado por EhInicioDeConstrucaoIgnoravel
            {
                int nl = texto.IndexOf('\n', i);
                return nl < 0 ? n : nl + 1;
            }

            // "/*" já confirmado por EhInicioDeConstrucaoIgnoravel
            int fim = texto.IndexOf("*/", i + 2, StringComparison.Ordinal);
            return fim < 0 ? n : fim + 2;
        }

        /// <summary>
        /// Avança sobre espaços em branco e comentários (linha/bloco), em qualquer combinação e quantidade,
        /// e devolve o próximo token alfanumérico (identificador/palavra-chave) ou identificador entre
        /// colchetes, junto com o índice logo após ele.
        /// </summary>
        private static (string Token, int IndexAfter) LerProximoToken(string texto, int inicio)
        {
            int i = PularBrancosEComentarios(texto, inicio);
            int n = texto.Length;

            int inicioToken = i;
            if (i < n && (char.IsLetter(texto[i]) || texto[i] == '_'))
            {
                while (i < n && (char.IsLetterOrDigit(texto[i]) || texto[i] == '_')) i++;
            }
            else if (i < n && texto[i] == '[')
            {
                int fecha = texto.IndexOf(']', i + 1);
                i = fecha < 0 ? n : fecha + 1;
            }
            return (texto[inicioToken..i], i);
        }

        private static string Resumir(string? sqlText)
        {
            if (string.IsNullOrEmpty(sqlText)) return string.Empty;
            string umaLinha = sqlText.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return umaLinha.Length <= 120 ? umaLinha : umaLinha[..120] + "...";
        }
    }
}
