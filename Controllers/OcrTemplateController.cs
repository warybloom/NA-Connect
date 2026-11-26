using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

[ApiController]
[Route("api/admin/ocr")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OcrTemplateController(IOcrTemplateRepository repository, ILdpColumnCatalog catalog, ILogger<OcrTemplateController> logger) : ControllerBase
{
    public sealed record CreatePackageRequest(string? Nome_Pacote);
    public sealed record SaveFieldsRequest(IReadOnlyList<string>? Campos);
    public sealed record SaveMappingsRequest(IReadOnlyList<FormatMapping>? Mapeamentos);

    [HttpGet("pacotes")]
    [RequirePermission("settings.ocr.view", "settings.projects.view", AnyProject = true)]
    public async Task<IActionResult> Packages(CancellationToken cancellationToken) =>
        Ok((await repository.GetPackagesAsync(cancellationToken)).Select(package => new { id = package.Id, nome = package.Name, criadoEm = package.CreatedAtUtc }));

    [HttpPost("pacotes")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> CreatePackage([FromBody] CreatePackageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var package = await repository.CreatePackageAsync(request?.Nome_Pacote ?? string.Empty, cancellationToken);
            return Created($"/api/admin/ocr/pacotes/{package.Id}", new { id = package.Id, nome = package.Name, criadoEm = package.CreatedAtUtc });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("pacotes/{packageId:int}")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> RenamePackage(int packageId, [FromBody] CreatePackageRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var package = await repository.RenamePackageAsync(packageId, request?.Nome_Pacote ?? string.Empty, cancellationToken);
            return Ok(new { id = package.Id, nome = package.Name, criadoEm = package.CreatedAtUtc });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>
    /// Catálogo DINÂMICO: TODAS as colunas reais de dbo.Ldp_Unificada (metadados do SQL Server), com rótulo amigável quando houver
    /// e marca de "recomendado". Nenhum campo é escondido.
    /// </summary>
    [HttpGet("campos")]
    [RequirePermission("settings.ocr.view", "settings.projects.view", AnyProject = true)]
    public async Task<IActionResult> Fields(CancellationToken cancellationToken) =>
        Ok((await catalog.GetColumnsAsync(cancellationToken)).Select(c => new
        {
            campo = c.Name, rotulo = c.Label, tipo = c.DataType, ordem = c.Ordinal, recomendado = c.Recommended, disponivel = c.Available,
        }));

    /// <summary>Campos auditados do TEMPLATE (comuns a todos os formatos). migracaoPendente = config.PacoteOcrCampo ainda não existe.</summary>
    [HttpGet("pacotes/{packageId:int}/campos")]
    [RequirePermission("settings.ocr.view", "settings.projects.view", AnyProject = true)]
    public async Task<IActionResult> TemplateFields(int packageId, CancellationToken cancellationToken)
    {
        var fields = await repository.GetTemplateFieldsAsync(packageId, cancellationToken);
        if (fields != null) return Ok(new { migracaoPendente = false, campos = fields.Where(f => f.Active).OrderBy(f => f.Order).Select(f => f.Field) });
        var regions = await repository.GetRegionsAsync(packageId, null, cancellationToken);
        return Ok(new { migracaoPendente = true, campos = regions.Select(r => r.TargetField).Distinct(StringComparer.OrdinalIgnoreCase) });
    }

    [HttpPut("pacotes/{packageId:int}/campos")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> SaveTemplateFields(int packageId, [FromBody] SaveFieldsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var columns = await catalog.GetColumnsAsync(cancellationToken);
            var fields = (request?.Campos ?? Array.Empty<string>()).Select(f => LdpColumnCatalogRules.Canonical(f, columns)).ToArray();
            var saved = await repository.SaveTemplateFieldsAsync(packageId, fields, cancellationToken);
            return Ok(new { migracaoPendente = false, campos = saved.Where(f => f.Active).OrderBy(f => f.Order).Select(f => f.Field) });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DocumentQualitySchemaPendingException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 547) { return NotFound(new { message = "Template não encontrado." }); }
    }

    /// <summary>Mapeamento de formatos do template + valores reais de Format na LDP + perfis calibrados do template.</summary>
    [HttpGet("pacotes/{packageId:int}/formatos")]
    [RequirePermission("settings.ocr.view", "settings.projects.view", AnyProject = true)]
    public async Task<IActionResult> FormatMappings(int packageId, CancellationToken cancellationToken)
    {
        var regions = await repository.GetRegionsAsync(packageId, null, cancellationToken);
        return Ok(new
        {
            migracaoPendente = await repository.GetTemplateFieldsAsync(packageId, cancellationToken) == null,
            mapeamentos = await repository.GetFormatMappingsAsync(packageId, cancellationToken),
            valoresLdp = await repository.GetLdpFormatValuesAsync(packageId, cancellationToken),
            perfis = regions.GroupBy(r => r.SheetType, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { tipoFolha = g.Key, referenceWidth = g.First().ReferenceWidth, referenceHeight = g.First().ReferenceHeight }),
        });
    }

    [HttpPut("pacotes/{packageId:int}/formatos")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> SaveFormatMappings(int packageId, [FromBody] SaveMappingsRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(new { mapeamentos = await repository.SaveFormatMappingsAsync(packageId, request?.Mapeamentos ?? Array.Empty<FormatMapping>(), cancellationToken) }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DocumentQualitySchemaPendingException ex) { return Conflict(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 547) { return NotFound(new { message = "Template não encontrado." }); }
    }

    [HttpGet("coordenadas")]
    [RequirePermission("settings.ocr.view", "settings.projects.view", AnyProject = true)]
    public async Task<IActionResult> Regions([FromQuery] int pacoteId, [FromQuery] string? tipoFolha, CancellationToken cancellationToken)
    {
        if (pacoteId <= 0) return BadRequest(new { message = "Template inválido." });
        var regions = await repository.GetRegionsAsync(pacoteId, tipoFolha, cancellationToken);
        return Ok(regions.Select(region => new
        {
            id = region.Id, pacoteId = region.PackageId, tipoFolha = region.SheetType, campoDestino = region.TargetField,
            x = region.X, y = region.Y, width = region.Width, height = region.Height, pagina = region.Page,
            referenceWidth = region.ReferenceWidth, referenceHeight = region.ReferenceHeight,
        }));
    }

    [HttpPost("salvar-coordenadas")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> SaveRegions([FromQuery] int pacoteId, [FromQuery] string tipoFolha,
        [FromBody] IReadOnlyList<SaveOcrTemplateRegion> regions, CancellationToken cancellationToken)
    {
        if (pacoteId <= 0) return BadRequest(new { message = "Template inválido." });
        try
        {
            // Cada campo de região tem de ser uma coluna REAL da LDP (validado no catálogo; nome canônico gravado).
            var columns = await catalog.GetColumnsAsync(cancellationToken);
            regions = (regions ?? Array.Empty<SaveOcrTemplateRegion>()).Select(r => r with { CampoDestino = LdpColumnCatalogRules.Canonical(r.CampoDestino, columns) }).ToArray();
            await repository.ReplaceRegionsAsync(pacoteId, tipoFolha, regions, cancellationToken);
            return Ok(new { message = "Coordenadas salvas.", total = regions.Count });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (SqlException ex) when (ex.Number == 547)
        {
            logger.LogWarning(ex, "Template OCR inexistente: {PackageId}.", pacoteId);
            return NotFound(new { message = "Template não encontrado." });
        }
    }

    [HttpDelete("limpar-coordenadas")]
    [RequirePermission("settings.projects.edit")]
    public async Task<IActionResult> ClearRegions([FromQuery] int pacoteId, [FromQuery] string tipoFolha, CancellationToken cancellationToken)
    {
        await repository.ClearRegionsAsync(pacoteId, tipoFolha, cancellationToken);
        return NoContent();
    }
}
