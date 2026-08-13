using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForgeSync.Modules.Piping.Models;

namespace ForgeSync.Modules.Piping.Services
{
    public class PipingDataProcessor
    {
        /// <summary>
        /// Lê os JSONs extraídos do SQLite do Plant 3D e consolida em uma lista unificada
        /// seguindo a documentação de Data Dictionary da Autodesk.
        /// </summary>
        public List<UnifiedPipingItem> ProcessPipingData(string jsonDirectoryPath)
        {
            // 1. Deserialização dos JSONs
            var engineeringItems = LoadJson<EngineeringItem>(Path.Combine(jsonDirectoryPath, "EngineeringItems.json"));
            var lineGroups = LoadJson<P3dLineGroup>(Path.Combine(jsonDirectoryPath, "P3dLineGroup.json"));
            var relationships = LoadJson<P3dLineGroupPartRelationship>(Path.Combine(jsonDirectoryPath, "P3dLineGroupPartRelationship.json"));
            var pipeRunComponents = LoadJson<PipeRunComponent>(Path.Combine(jsonDirectoryPath, "PipeRunComponent.json"));
            
            // Tabelas especializadas (usadas como flags para identificação rápida)
            var valves = LoadJson<Valve>(Path.Combine(jsonDirectoryPath, "Valve.json"));
            var equipments = LoadJson<Equipment>(Path.Combine(jsonDirectoryPath, "Equipment.json"));
            var supports = LoadJson<Support>(Path.Combine(jsonDirectoryPath, "Support.json"));
            var instruments = LoadJson<Instrument>(Path.Combine(jsonDirectoryPath, "Instrument.json"));

            // 2. Criação de Dicionários para Lookup O(1) - Performance Extrema
            var lineGroupDict = lineGroups.ToDictionary(lg => lg.PnPID, lg => lg);
            var pipeRunCompDict = pipeRunComponents.ToDictionary(prc => prc.PnPID, prc => prc);
            
            // HashSets para busca rápida de categorias (Flags booleanas)
            var valveSet = new HashSet<int>(valves.Select(v => v.PnPID));
            var equipmentSet = new HashSet<int>(equipments.Select(e => e.PnPID));
            var supportSet = new HashSet<int>(supports.Select(s => s.PnPID));
            var instrumentSet = new HashSet<int>(instruments.Select(i => i.PnPID));

            // Dicionário de Mapeamento: PartId -> LineGroup
            var partToLineGroupDict = new Dictionary<int, P3dLineGroup>();
            foreach (var rel in relationships)
            {
                if (lineGroupDict.TryGetValue(rel.LineGroupId, out var lineGroup))
                {
                    partToLineGroupDict[rel.PartId] = lineGroup;
                }
            }

            var unifiedItems = new List<UnifiedPipingItem>();

            // 3. Montagem do Objeto Unificado (O "De/Para" definitivo)
            foreach (var item in engineeringItems)
            {
                var unifiedItem = new UnifiedPipingItem
                {
                    PnPID = item.PnPID,
                    Class = item.Class,
                    Tag = item.Tag,
                    ShortDescription = item.ShortDescription,
                    Material = item.Material,
                    Manufacturer = item.Manufacturer
                };

                // Anexar dados da Linha (se houver relacionamento)
                if (partToLineGroupDict.TryGetValue(item.PnPID, out var lineGroup))
                {
                    unifiedItem.LineNumber = lineGroup.Number ?? lineGroup.Tag;
                    unifiedItem.LineSpec = lineGroup.NominalSpec;
                }

                // Anexar dados físicos de tubulação (se for um PipeRunComponent)
                if (pipeRunCompDict.TryGetValue(item.PnPID, out var pipeRunComp))
                {
                    unifiedItem.ItemCode = pipeRunComp.ItemCode;
                    unifiedItem.Length = pipeRunComp.Length;
                    unifiedItem.Weight = pipeRunComp.Weight;
                    unifiedItem.SpoolNumber = pipeRunComp.SpoolNumber;
                }

                // Flags de Categoria Baseado na Herança de Tabelas do Plant 3D
                unifiedItem.IsValve = valveSet.Contains(item.PnPID);
                unifiedItem.IsEquipment = equipmentSet.Contains(item.PnPID);
                unifiedItem.IsSupport = supportSet.Contains(item.PnPID);
                unifiedItem.IsInstrument = instrumentSet.Contains(item.PnPID);

                unifiedItems.Add(unifiedItem);
            }

            return unifiedItems;
        }

        private List<T> LoadJson<T>(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<T>();

            string jsonString = File.ReadAllText(filePath);
            
            // Tratamento caso o JSON seja um array root ou esteja dentro de um envelope
            try
            {
                return JsonSerializer.Deserialize<List<T>>(jsonString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<T>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro crítico ao processar {filePath}: {ex.Message}");
                throw;
            }
        }
    }
}