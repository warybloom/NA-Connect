import { useMemo } from 'react';
import { CheckCircle2, XCircle, AlertTriangle, ScrollText } from 'lucide-react';

// ================================================================
// 📦 TIPOS E PARSER
// ================================================================
export interface EtapaLog {
  id: number;
  titulo: string;
  status: 'sucesso' | 'erro' | 'alerta';
  mensagem: string;
  timestamp?: string;
}

function parsearLog(conteudoBruto: string): EtapaLog[] {
  if (!conteudoBruto) return [];

  const linhas = conteudoBruto.split('\n');
  const etapas: EtapaLog[] = [];

  linhas.forEach((linha, index) => {
    const linhaLimpa = linha.trim();
    if (!linhaLimpa) return; // Ignora linhas em branco

    const timestamp = extrairHora(linhaLimpa);
    
    // 1. Detecta ERROS
    if (linhaLimpa.includes('❌') || /erro|error|fail|exception|falha|failed|fatal|unhandled/i.test(linhaLimpa)) {
      etapas.push({
        id: index,
        titulo: extrairTitulo(linhaLimpa, "Erro na Execução"),
        status: 'erro',
        mensagem: extrairMensagemCompleta(linhaLimpa),
        timestamp
      });
    } 
    // 2. Detecta ALERTAS / AVISOS
    else if (linhaLimpa.includes('⚠️') || /warn|alerta|warning|atencao|atenção/i.test(linhaLimpa)) {
      etapas.push({
        id: index,
        titulo: extrairTitulo(linhaLimpa, "Aviso / Alerta"),
        status: 'alerta',
        mensagem: extrairMensagemCompleta(linhaLimpa),
        timestamp
      });
    }
    // 3. SUCESSO / INFORMAÇÃO
    else {
      etapas.push({
        id: index,
        titulo: extrairTitulo(linhaLimpa, "Processamento"),
        status: 'sucesso',
        mensagem: extrairMensagemCompleta(linhaLimpa),
        timestamp
      });
    }
  });

  return etapas;
}

// Helper: Extrai horário sem alterar o texto base
function extrairHora(texto: string): string | undefined {
  const match = texto.match(/\b\d{2}:\d{2}(:\d{2})?\b/);
  return match ? match[0] : undefined;
}

// Helper: Título inteligente sem truncar palavras abruptamente
function extrairTitulo(texto: string, fallback: string): string {
  // Limpa emojis e colchetes iniciais de log para achar o nome da etapa
  let limpo = texto
    .replace(/[❌⚠️✅🏗️🏁]/g, '')
    .replace(/^\[.*?\]/g, '')
    .replace(/^\[.*?\]/g, '')
    .replace(/\b\d{2}:\d{2}(:\d{2})?\b/g, '')
    .replace(/^(INFO|ERROR|WARN|DEBUG|TRACE):?/i, '')
    .trim();

  if (!limpo) return fallback;

  // Se a linha contiver " - " ou ": ", usamos apenas o primeiro segmento como Título
  if (limpo.includes(' - ')) {
    return limpo.split(' - ')[0].trim();
  }
  if (limpo.includes(': ')) {
    return limpo.split(': ')[0].trim();
  }

  // Caso não tenha separadores claros, retorna a linha inteira como título/resumo
  return limpo;
}

// Helper: Garante que a MENSAGEM COMPLETA seja retornada
function extrairMensagemCompleta(texto: string): string {
  return texto
    .replace(/[❌⚠️✅🏗️🏁]/g, '')                     // Remove emojis de marcação
    .replace(/^\[.*?\]/g, '')                         // Remove timestamps/tags [ex: 14:32]
    .replace(/^\[.*?\]/g, '') 
    .replace(/^(INFO|ERROR|WARN|DEBUG|TRACE):?/i, '')  // Remove prefixos de nível de log
    .trim();
}

// ================================================================
// 🎨 COMPONENTE PRINCIPAL
// ================================================================
interface Props {
  conteudoLog: string;
}

export function VisualizadorLogResumido({ conteudoLog }: Props) {
  const etapas = useMemo(() => parsearLog(conteudoLog), [conteudoLog]);

  if (!conteudoLog) {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-2 text-muted-foreground">
        <ScrollText className="w-6 h-6" />
        <span className="text-xs font-medium">Aguardando dados de execução...</span>
      </div>
    );
  }

  if (etapas.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-2 text-muted-foreground p-6 text-center">
        <AlertTriangle className="w-6 h-6 text-amber-400" />
        <span className="text-xs font-medium">
          Nenhuma etapa foi identificada no padrão deste log.
        </span>
      </div>
    );
  }

  return (
    <div className="h-full w-full overflow-y-auto space-y-2.5 pr-2 min-w-0">
      {etapas.map((etapa) => (
        <div
          key={etapa.id}
          className={`p-3.5 rounded-lg border flex items-start gap-3 transition-all ${
            etapa.status === 'sucesso'
              ? 'bg-emerald-500/5 border-emerald-500/20 text-emerald-300'
              : etapa.status === 'erro'
              ? 'bg-rose-500/5 border-rose-500/20 text-rose-300'
              : 'bg-amber-500/5 border-amber-500/20 text-amber-300'
          }`}
        >
          {/* Ícone Indicador */}
          <div className="mt-0.5 shrink-0">
            {etapa.status === 'sucesso' && <CheckCircle2 className="w-4 h-4 text-emerald-400" />}
            {etapa.status === 'erro' && <XCircle className="w-4 h-4 text-rose-400" />}
            {etapa.status === 'alerta' && <AlertTriangle className="w-4 h-4 text-amber-400" />}
          </div>

          {/* Conteúdo da Etapa */}
          <div className="flex-1 min-w-0">
            <div className="flex items-center justify-between gap-2 mb-1">
              <h4 className="text-xs font-bold uppercase tracking-wider text-foreground whitespace-normal break-words">
                {etapa.titulo}
              </h4>
              {etapa.timestamp && (
                <span className="text-[10px] font-mono text-muted-foreground shrink-0">
                  {etapa.timestamp}
                </span>
              )}
            </div>
            
            {/* Exibe a mensagem completa se for diferente do título */}
            {etapa.mensagem !== etapa.titulo && (
              <p className="text-xs text-muted-foreground leading-relaxed whitespace-pre-wrap break-all">
                {etapa.mensagem}
              </p>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}