#nullable disable
using System;
using System.Text.Json.Serialization; 
using System.Threading.Tasks;
using System.Xml.Serialization;
namespace APS.WebApp.Models
{
    // ==========================================
    // MODELOS DE BANCO DE DADOS (FORGESYNC)
    // ==========================================
    public class Plant3DConfig
    {
        public int ConfigID { get; set; }
        public string Project_ID_ACC { get; set; }
        public string Nome_Projeto { get; set; } // ✅ Adicionado
        public string Folder_ID_DCF { get; set; }
        public string Mapeamento_JSON { get; set; } // ✅ Adicionado para suportar o Upsert
        public string? Regras_Tag_JSON { get; set; }
        
        // Mantenha os IDs de Template se você já os utiliza no seu banco
        public int? Template_Valve_ID { get; set; }
        public int? Template_BOM_ID { get; set; }
        public int? Template_Inst_ID { get; set; }
        public int? Template_Line_ID { get; set; }

        public string Piping_File_Path { get; set; } // ✅ Adicionado
    public string Pid_File_Path { get; set; }    // ✅ Adicionado
    }
    
// ✅ ADICIONE ESTA LINHA NO TOPO

// No ficheiro onde definiu o DTO (ex: Plant3DModels.cs ou um novo)

namespace APS.WebApp.Models 
{
    public class ProjectLookupDTO // 👈 Garanta que o 'public' está aqui
    {
        [JsonPropertyName("projectId")]
        public string? Project_ID_ACC { get; set; }
        
        [JsonPropertyName("projectName")]
        public string? Nome_Projeto { get; set; }
    }
}
// Adicione estas classes junto com os outros DTOs
public class TagRule
{
    public string Categoria { get; set; }
    public string Delimitador { get; set; }
    public List<TagPart> Partes { get; set; } = new List<TagPart>();
}

public class TagPart
{
    public string Id { get; set; }
    public string Name { get; set; }
}
    public class Plant3DAuditoria
    {
        public int AuditoriaID { get; set; }
        public string Project_ID_ACC { get; set; }
        public DateTime Data_Auditoria { get; set; }
        public int Total_PID { get; set; }
        public int Total_3D { get; set; }
        public int Faltam_No_3D { get; set; }
        public int Faltam_No_PID { get; set; }
        public int Divergencias_Spec { get; set; }
    }

    public class Plant3DAtivo
    {
        public long AtivoID { get; set; }
        public int AuditoriaID { get; set; }
        public string Tag { get; set; }
        public string Categoria { get; set; }
        public string Desenho_PID { get; set; }
        public string Desenho_3D { get; set; }
        public string Spec_PID { get; set; }
        public string Spec_3D { get; set; }
        public string Status_Cruzamento { get; set; }
        // Na sua classe Plant3DAtivo existente, adicione esta linha:
public string? Tag_Fatiado_JSON { get; set; }

        // ✅ Colunas Dinâmicas (Wide Table) adicionadas com sucesso!
        public string Atributo_01 { get; set; }
        public string Atributo_02 { get; set; }
        public string Atributo_03 { get; set; }
        public string Atributo_04 { get; set; }
        public string Atributo_05 { get; set; }
        public string Atributo_06 { get; set; }
        public string Atributo_07 { get; set; }
        public string Atributo_08 { get; set; }
        public string Atributo_09 { get; set; }
        public string Atributo_10 { get; set; }
        public string Atributo_11 { get; set; }
        public string Atributo_12 { get; set; }
        public string Atributo_13 { get; set; }
        public string Atributo_14 { get; set; }
        public string Atributo_15 { get; set; }
        public string Atributo_16 { get; set; }
        public string Atributo_17 { get; set; }
        public string Atributo_18 { get; set; }
        public string Atributo_19 { get; set; }
        public string Atributo_20 { get; set; }
    }

    // ==========================================
    // MODELOS DE EXTRAÇÃO DO SQLITE (DCF)
    // ==========================================
    public class InstrumentoDTO
    {
        public string Tag { get; set; }
        public string Type { get; set; }
        public string SubArea { get; set; }
        public int PnPID { get; set; }
        public string Title { get; set; }
        public string DwgName { get; set; }
        public string LineEquipment_Calc { get; set; } // Lógica concatenada Q,X,Y,Z
    }

    public class ValveDTO
    {
        public string Tag { get; set; }
        public string Code { get; set; }
        public string SubArea { get; set; }
        public int PnPID { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string ClassName { get; set; }
        public string DwgName { get; set; }
    }

    public class LinhaDTO
    {
        public string Tag { get; set; }
        public string NominalSize { get; set; }
        public string ServiceColor { get; set; }
        public string SubArea { get; set; }
        public string LineNumber { get; set; }
        public string NominalSpec { get; set; }
        public string DwgName { get; set; }
        public string PipeLineTo { get; set; }
        public string PipeLineFrom { get; set; }
    }

    public class EquipamentoDTO
    {
        public string Tag { get; set; }
        public string Area { get; set; }
        public string EquipmentType { get; set; }
        public string DwgName { get; set; }
    }
}