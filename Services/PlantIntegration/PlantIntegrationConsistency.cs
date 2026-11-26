using System.Globalization;

namespace APS.WebApp.Services.PlantIntegration;

// Every figure here comes from completed DCF versions and the same saved filter
// policy used by inventory, audit and measurement. No guessed P&ID associations.
public sealed partial class PlantIntegrationService
{
    public sealed record LineCoverage(string Tag, bool Modelled3d,
        int ExpectedValves, int ActualValves, int ExpectedInstruments, int ActualInstruments,
        int ExpectedEquipment, int ActualEquipment, bool AssociationsVerified,
        decimal? ValvesPercent, decimal? InstrumentsPercent, decimal? OverallPercent,
        IReadOnlyList<string> Issues);
    public sealed record ConsistencyResponse(string Status, string Message, string? RevisionToken,
        string TagLineColumn, int TotalLines, int FullyModelled, int Incomplete,
        int Unverified, IReadOnlyList<LineCoverage> Lines)
    {
        // Aggregates reflect the complete filtered project; Lines itself is paginated.
        public int VerifiedLines { get; init; }
        public int WeightedEvaluated { get; init; }
        public int ValvesPending { get; init; }
        public int InstrumentsPending { get; init; }
    }

    private static string? CleanIdentifier(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('?')) return null;
        return text.Trim().ToUpperInvariant();
    }
    private static string[] RelatedLineTags(PlantRow row, string column)
    {
        if (!row.Values.TryGetValue(column, out var value) || string.IsNullOrWhiteSpace(value)) return [];
        return value.Split([';', '|', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanIdentifier).Where(x => x is not null).Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    // Never certify percentages if even one eligible component cannot be linked to a line.
    // Checking for the existence of a TagLine column in SOME rows is insufficient:
    // missing, blank and '?' values may conceal components on any measured line.
    private static bool HasCompleteLineRelations(IReadOnlyList<PlantRow> rows, string column) =>
        rows.All(row => RelatedLineTags(row, column).Length > 0);

    private static decimal? Proportion(int expected, int actual)
    {
        if (expected < 0) return null;
        return expected == 0 ? 100m : Math.Min(100m, 100m * actual / expected);
    }

    public async Task<ConsistencyResponse> GetConsistencyAsync(
        string projectIdAcc, Guid dashboardId, string? phaseId, int limit, int offset, CancellationToken ct)
    {
        var standards = await GetStandardsAsync(projectIdAcc, ct);
        var key = string.IsNullOrWhiteSpace(standards.TagLineColumn) ? "TagLine" : standards.TagLineColumn.Trim();
        var history = await HistoryAsync(dashboardId, ct);
        var revision = Revision(history);
        if (history.Pnid.Base is null || history.Piping.Base is null)
            return new("no-baseline", "Aguardando versões-base P&ID e 3D.", revision,key,0,0,0,0,[]);
        var mappings = await GetConfigurationAsync(projectIdAcc, ct);
        var line = mappings.FirstOrDefault(x => x.Id.Equals("line",StringComparison.OrdinalIgnoreCase));
        var components = mappings.Where(x=>x.Id is "valve" or "inst" or "equip").ToArray();
        if (line is null)
            return new("no-mapping","Cadastre o DE/PARA da categoria Linhas.",revision,key,0,0,0,0,[]);
        if (!components.Any(x => x.Id=="valve") || !components.Any(x => x.Id=="inst"))
            return new("no-association","É necessário configurar as categorias de Válvulas e Instrumentos.",revision,key,0,0,0,0,[]);

        var savedFilters=(await GetFiltersAsync(projectIdAcc,ct)).Filters;
        var drawingsPid=DrawingIndex(history.Pnid);
        var drawingsPiping=DrawingIndex(history.Piping);
        List<PlantRow> Included(PartState state, CategoryMapping map, string side, IReadOnlyDictionary<string,PlantRow> drawings) =>
            EffectiveRows(state.Rows.Values.Where(x=>x.Table.Equals(side=="pid"?map.PidTable:map.PipingTable,StringComparison.OrdinalIgnoreCase)),
                side,savedFilters.GetValueOrDefault(map.Id,[]),drawings);

        var linePid=Included(history.Pnid,line,"pid",drawingsPid);
        var linePiping=Included(history.Piping,line,"piping",drawingsPiping);
        var expectedLineTags=linePid.Select(x=>CleanIdentifier(Tag(line,x,true)))
            .Where(x=>x is not null).Select(x=>x!).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        if (expectedLineTags.Length==0)
            return new("no-association","As linhas P&ID ainda não possuem TAG identificável.",revision,key,0,0,0,0,[]);

        var pipedLines=linePiping.Select(x=>CleanIdentifier(Tag(line,x,false)))
            .Where(x=>x is not null).Select(x=>x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = new Dictionary<string,Dictionary<string,List<string>>>(StringComparer.OrdinalIgnoreCase);
        var actual=new Dictionary<string,Dictionary<string,List<string>>>(StringComparer.OrdinalIgnoreCase);
        bool hasPidRelations=true,hasPipingRelations=true;

        foreach (var map in components)
        {
            var pid=Included(history.Pnid,map,"pid",drawingsPid);
            var pip=Included(history.Piping,map,"piping",drawingsPiping);
            // ALL eligible rows must have a usable line association. A single
            // missing/blank/invalid TagLine could otherwise hide an unmatched
            // component and incorrectly certify a partially modelled line.
            if (!HasCompleteLineRelations(pid,key)) hasPidRelations=false;
            if (!HasCompleteLineRelations(pip,key)) hasPipingRelations=false;
            foreach(var (side,rows,destination) in new[]{
                (Side:"pid",Rows:pid,Target:expected),(Side:"piping",Rows:pip,Target:actual)})
            {
                var isPid=side=="pid";
                foreach(var row in rows)
                {
                    var identifier=CleanIdentifier(Tag(map,row,isPid));
                    if(identifier is null) continue;  // not comparable, do not invent a match
                    foreach(var lineTag in RelatedLineTags(row,key))
                    {
                        if(!destination.TryGetValue(lineTag,out var byCat))
                            destination[lineTag]=byCat=new(StringComparer.OrdinalIgnoreCase);
                        if(!byCat.TryGetValue(map.Id,out var ids))
                            byCat[map.Id]=ids=[];
                        ids.Add(identifier);
                    }
                }
            }
        }
        // Incomplete source identification makes the percentage unknown, rather
        // than marking a line with zero expected objects as 100%.
        if (!hasPidRelations || !hasPipingRelations)
            return new("no-association",
                $"Há componentes P&ID e/ou 3D sem {key} válido; a cobertura de todas as linhas não pode ser comprovada.",
                revision,key,expectedLineTags.Length,0,0,expectedLineTags.Length,[]);

        var weights = await GetLineWeightsAsync(projectIdAcc,phaseId,ct);
        var weightMap=weights.Weights.ToDictionary(x=>x.Key,x=>x.Percent,StringComparer.OrdinalIgnoreCase);
        bool hasWeights=weightMap.Count>0;
        var all=new List<LineCoverage>(expectedLineTags.Length);
        foreach(var tag in expectedLineTags)
        {
            var has3d=pipedLines.Contains(tag);
            var p=expected.GetValueOrDefault(tag,new(StringComparer.OrdinalIgnoreCase));
            var t=actual.GetValueOrDefault(tag,new(StringComparer.OrdinalIgnoreCase));
            (int expectedCount,int actualCount) Coverage(string id)
            {
                var ids=p.GetValueOrDefault(id,[]);
                var comparable=t.GetValueOrDefault(id,[]).GroupBy(x=>x,StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x=>x.Key,x=>x.Count(),StringComparer.OrdinalIgnoreCase);
                int found=0;
                foreach(var group in ids.GroupBy(x=>x,StringComparer.OrdinalIgnoreCase))
                    found+=Math.Min(group.Count(),comparable.GetValueOrDefault(group.Key,0));
                return(ids.Count,found);
            }
            var (pv,av)=Coverage("valve");
            var (pi,ai)=Coverage("inst");
            var (pe,ae)=Coverage("equip");
            var vPct=Proportion(pv,av);
            var iPct=Proportion(pi,ai);
            bool verified=has3d && (pv>0||pi>0||pe>0) && hasPidRelations && hasPipingRelations;
            var issues=new List<string>();
            if(!has3d)issues.Add("Linha sem correspondente 3D");
            if(pv>av)issues.Add($"Faltam {pv-av} válvula(s)");
            if(pi>ai)issues.Add($"Faltam {pi-ai} instrumento(s)");
            if(pe>ae)issues.Add($"Faltam {pe-ae} equipamento(s)");
            if(pv==0&&pi==0&&pe==0)issues.Add("Sem componentes esperados associados no P&ID");
            decimal? score=null;
            if(verified && hasWeights)
            {
                var m=has3d?100m:0m;
                var v=vPct??0m;var ins=iPct??0m;
                var eq=Proportion(pe,ae)??0m;
                score=Math.Round((
                    weightMap.GetValueOrDefault("modeling")*m+
                    weightMap.GetValueOrDefault("valves")*v+
                    weightMap.GetValueOrDefault("instruments")*ins+
                    weightMap.GetValueOrDefault("equipment")*eq)/100m,2);
                if(weightMap.Keys.Any(x=>x is "tieIns" or "materials" or "supports"))score=null;
            }
            all.Add(new(tag,has3d,pv,av,pi,ai,pe,ae,verified,vPct,iPct,score,issues));
        }
        var verifiedCount=all.Count(x=>x.AssociationsVerified);
        var full=all.Count(x=>x.AssociationsVerified&&x.OverallPercent==100m);
        return new(verifiedCount==0?"no-association":"ready",
            verifiedCount==0?"Não há vínculos suficientes para um percentual comprovado.":"Somente linhas com relações identificadas recebem cálculo ponderado.",
            revision,key,all.Count,full,
            all.Count(x=>x.AssociationsVerified&&x.OverallPercent is <100m),
            all.Count-verifiedCount,all.Skip(offset).Take(limit).ToArray())
        {
            VerifiedLines = verifiedCount,
            WeightedEvaluated = all.Count(x => x.AssociationsVerified && x.OverallPercent.HasValue),
            ValvesPending = all.Count(x => x.AssociationsVerified && x.ExpectedValves > x.ActualValves),
            InstrumentsPending = all.Count(x => x.AssociationsVerified && x.ExpectedInstruments > x.ActualInstruments)
        };
    }
}
