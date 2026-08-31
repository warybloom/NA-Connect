#nullable disable
using System.Collections.Generic;

namespace NA_Connect.Models 
{
using System.Text.Json.Serialization;



public class SaveLdpConfigDTO
{
    public int ProjetoId { get; set; }
    public string NomeConfig { get; set; }
    public string FolderIdLdp { get; set; }
    public string FolderIdCronograma { get; set; }
    public string SheetName { get; set; }
    public int HeaderRow { get; set; }
    public string MappingJSON { get; set; } // Aqui vai o JSON: {"Document_Code": "B", "Revision": "E"...}
}
public class UpdateProjectPayload
{
    public bool is_Ativo { get; set; }
    public int? NormaID { get; set; }      // ✅ Unificado conforme sua sugestão
    public int? PacoteOCRID { get; set; }
}
public class ProjetoDetalhesDTO
{
    public int ProjetoId { get; set; }
    public string Nome_Projeto { get; set; }
    public string Project_ID_ACC { get; set; }
    public string Account_ID_ACC { get; set; } // O nosso HubId
    public int? NormaID { get; set; }
    public int? ConfigLDPID { get; set; }
}
public class UpdateProjectStatusDTO
{
    public bool is_Ativo { get; set; } // O Switch do React envia true/false
    public int? NormaID { get; set; }  // ✅ Unificado
    public int? PacoteOCRID { get; set; }
}
public class NormaDTO
{
    public int normaId { get; set; }
    public string Nome_Norma { get; set; }
    public string Descricao { get; set; }
    
    // ✅ Garanta que o nome da lista seja 'mappings' em minúsculo
    public List<MappingDTO> mappings { get; set; } = new List<MappingDTO>();
}
public class UpdateConfigPayload
{
    public string projectIdAcc { get; set; }
    public int? NormaID { get; set; }      // ✅ Alterado: Sem o underline
    public int? PacoteOCRID { get; set; }
}

public class MappingDTO
{
    // ✅ Use nomes em minúsculo para bater com o seu loop foreach
    public string sigla { get; set; }
    public string pasta_Destino { get; set; }
    public string tipo_Documento { get; set; }
    public bool ignorar { get; set; }
}

    public class TemplateDTO 
    {
        // Padronizando para PascalCase para evitar erros de mapeamento no Dapper
        public int? TemplateID { get; set; }
        public string Sigla_Doc { get; set; }
        public string Template_Nome { get; set; }
        public string Tipo_Doc { get; set; } 
        public int? NormaID { get; set; } 
    }
}