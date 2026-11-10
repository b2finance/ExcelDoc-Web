using ClosedXML.Excel;
using ExcelDoc.Server.Localization;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Repositories.Interfaces;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Security;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExcelDoc.Server.Controllers;

[ApiController]
[Route("api/planilhas")]
[Authorize(Roles = AuthRoles.All)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PlanilhasController(
    IWebHostEnvironment environment,
    IPerfilMapeamentoRepository perfilMapeamentoRepository,
    IMessageService messageService) : ControllerBase
{
    private const string ExampleSpreadsheetFileName = "Planilha Exemplo.xlsx";
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("exemplo/padrao")]
    public IActionResult DownloadStandardExample()
    {
        var filePath = Path.Combine(environment.ContentRootPath, "Planilha", ExampleSpreadsheetFileName);

        return System.IO.File.Exists(filePath)
            ? PhysicalFile(filePath, ExcelContentType, ExampleSpreadsheetFileName)
            : NotFound();
    }

    [HttpGet("exemplo")]
    public async Task<IActionResult> DownloadExample([FromQuery] int perfilId, CancellationToken cancellationToken)
    {
        if (perfilId <= 0)
        {
            return BadRequest(new ProblemDetails { Detail = messageService.Get(MessageKeys.MappingProfileRequired) });
        }

        try
        {
            var perfil = await perfilMapeamentoRepository.GetForExecutionAsync(perfilId, cancellationToken);
            if (perfil is null)
            {
                return NotFound(new ProblemDetails { Detail = messageService.Get(MessageKeys.MappingProfileNotFound) });
            }

            var missingMapping = perfil.Itens.FirstOrDefault(item => item.Mapeamento is null);
            if (missingMapping is not null)
            {
                return UnprocessableEntity(new ProblemDetails
                {
                    Detail = messageService.Get(MessageKeys.MappingIdNotFound, missingMapping.FK_IdMapeamento)
                });
            }

            using var workbook = new XLWorkbook();
            workbook.CustomProperties.Add(ExcelReaderService.UseRawNumericValuesProperty, true);
            var worksheet = workbook.AddWorksheet("Principal");
            WriteColumn(worksheet.Column(1), "#", "#", (int)XLPredefinedFormat.Number.General);

            var campos = perfil.Itens
                .SelectMany(item => item.Mapeamento.Campos)
                .Where(campo => !string.IsNullOrWhiteSpace(campo.NomeCampo));
            var usedColumns = new HashSet<int> { 1 };

            foreach (var campo in campos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var indice = campo.IndiceColuna;
                if (indice is < 2 or > XLHelper.MaxColumnNumber)
                {
                    return UnprocessableEntity(new ProblemDetails
                    {
                        Detail = messageService.Get(
                            MessageKeys.ExampleSpreadsheetColumnOutOfRange, campo.NomeCampo, indice, XLHelper.MaxColumnNumber)
                    });
                }

                if (!usedColumns.Add(indice))
                {
                    return UnprocessableEntity(new ProblemDetails
                    {
                        Detail = messageService.Get(MessageKeys.ExampleSpreadsheetDuplicateColumn, indice)
                    });
                }

                WriteColumn(worksheet.Column(indice), campo.NomeCampo, campo.DescricaoCampo, GetNumberFormatId(campo.TipoCampo));
            }

            var headerBorders = worksheet.Range(1, 1, 2, usedColumns.Max()).Style.Border;
            headerBorders.OutsideBorder = XLBorderStyleValues.Thin;
            headerBorders.InsideBorder = XLBorderStyleValues.Thin;

            using var output = new MemoryStream();
            workbook.SaveAs(output);
            return File(output.ToArray(), ExcelContentType, ExampleSpreadsheetFileName);
        }
        catch (SapSessionExpiredException ex)
        {
            return Unauthorized(new ProblemDetails { Detail = ex.Message });
        }
        catch (SapServiceLayerException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: messageService.Get(MessageKeys.SapServiceLayerOperationFailed));
        }
    }

    private static void WriteColumn(IXLColumn column, string name, string? description, int numberFormatId)
    {
        column.Style.NumberFormat.NumberFormatId = numberFormatId;

        var header = column.Cell(1);
        header.Value = name;
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#475569");

        var descriptionCell = column.Cell(2);
        if (!string.IsNullOrWhiteSpace(description))
        {
            descriptionCell.Value = description;
        }

        descriptionCell.Style.Font.Bold = true;
        descriptionCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
        descriptionCell.Style.Alignment.WrapText = true;
        column.AdjustToContents(1, 2, 6, 48);
    }

    private static int GetNumberFormatId(TipoCampo tipo) => tipo switch
    {
        TipoCampo.String => (int)XLPredefinedFormat.Number.Text,
        TipoCampo.Int => (int)XLPredefinedFormat.Number.Integer,
        TipoCampo.Double => (int)XLPredefinedFormat.Number.Precision2,
        TipoCampo.DateTime => (int)XLPredefinedFormat.DateTime.DayMonthYear4WithSlashes,
        _ => (int)XLPredefinedFormat.Number.General
    };
}
