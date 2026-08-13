using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using APS.WebApp.Models;
using ForgeSync.Modules.Piping.Models;

using ForgeSync.Modules.Piping.Models;

namespace ForgeSync.Modules.Plant3D.Services
{
    public class PlantAuditEngine
    {
        /// <summary>
        /// Executa o cruzamento de dados entre P&ID e 3D baseado no mapeamento dinâmico do projeto.
        /// </summary>
        public List<Plant3DAtivo> ExecuteAudit(
            List<UnifiedPipingItem> pidData, 
            List<UnifiedPipingItem> p3dData, 
            string mappingJson, 
            int auditoriaId)
        {
            // 1. Parse do Mapeamento Dinâmico
            var mapping = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(mappingJson);
            
            var result = new List<Plant3DAtivo>();

            // 2. Processamento por Categoria (Equipamentos, Válvulas, etc.)
            foreach (var categoria in mapping.Keys)
            {
                string pidKey = mapping[categoria]["pid_col"];
                string p3dKey = mapping[categoria]["p3d_col"];

                // Filtramos os dados da categoria específica
                var currentPidItems = FilterByCategory(pidData, categoria);
                var currentP3dItems = FilterByCategory(p3dData, categoria);

                // Criamos um dicionário do 3D para busca rápida O(1)
                // A chave do dicionário é o valor da coluna mapeada (ex: a TAG)
                var p3dLookup = currentP3dItems
                    .Where(x => !string.IsNullOrEmpty(GetPropertyValue(x, p3dKey)))
                    .ToDictionary(x => GetPropertyValue(x, p3dKey), x => x);

                foreach (var pidItem in currentPidItems)
                {
                    string tagPid = GetPropertyValue(pidItem, pidKey);
                    
                    var auditEntry = new Plant3DAtivo
                    {
                        AuditoriaID = auditoriaId,
                        Tag = tagPid,
                        Categoria = categoria.ToUpper(),
                        Desenho_PID = pidItem.ShortDescription, // Ou coluna de desenho vinda do JSON
                        Spec_PID = pidItem.LineSpec
                    };

                    // 3. O "MATCH" LÓGICO
                    if (p3dLookup.TryGetValue(tagPid, out var p3dMatch))
                    {
                        auditEntry.Desenho_3D = p3dMatch.LineNumber;
                        auditEntry.Spec_3D = p3dMatch.LineSpec;
                        
                        // Verificação de Consistência de Dados (Ex: Spec diferente)
                        if (pidItem.LineSpec != p3dMatch.LineSpec)
                            auditEntry.Status_Cruzamento = "DIVERGENTE_SPEC";
                        else
                            auditEntry.Status_Cruzamento = "OK";
                            
                        // Remove do lookup para sabermos o que sobrou apenas no 3D
                        p3dLookup.Remove(tagPid);
                    }
                    else
                    {
                        auditEntry.Status_Cruzamento = "FALTANDO_NO_3D";
                    }

                    result.Add(auditEntry);
                }

                // 4. Sobras do 3D (Itens que existem no modelo mas não no P&ID)
                foreach (var remaining3d in p3dLookup.Values)
                {
                    result.Add(new Plant3DAtivo
                    {
                        AuditoriaID = auditoriaId,
                        Tag = GetPropertyValue(remaining3d, p3dKey),
                        Categoria = categoria.ToUpper(),
                        Desenho_3D = remaining3d.LineNumber,
                        Spec_3D = remaining3d.LineSpec,
                        Status_Cruzamento = "SOBRA_NO_3D"
                    });
                }
            }

            return result;
        }

        // Auxiliar para filtrar por categoria baseado nas flags do objeto
        private List<UnifiedPipingItem> FilterByCategory(List<UnifiedPipingItem> items, string category)
        {
            return category.ToLower() switch
            {
                "equipamentos" => items.Where(i => i.IsEquipment).ToList(),
                "valvulas" => items.Where(i => i.IsValve).ToList(),
                "instrumentos" => items.Where(i => i.IsInstrument).ToList(),
                _ => items.ToList(),
            };
        }

        // Auxiliar de Reflection para pegar valor da propriedade dinamicamente pelo nome da coluna
        private string GetPropertyValue(UnifiedPipingItem item, string propName)
        {
            return item.GetType().GetProperty(propName)?.GetValue(item, null)?.ToString() ?? "";
        }
    }
}