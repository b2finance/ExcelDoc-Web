using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using ExcelDoc.Server.Controllers;
using ExcelDoc.Server.Localization;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Repositories.Interfaces;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace ExcelDoc.Server.Tests;

public sealed class PlanilhasControllerTests
{
    [Fact]
    public async Task DownloadExample_GeneratesAndImportsWorkbookForSelectedProfile()
    {
        var header = MappingItem(
            1, "Header", TipoColecao.Header,
            Field("DocDate", 2, TipoCampo.DateTime, "Data de Lançamento"),
            Field("ImportFileNum", 8),
            Field("Testando", 41),
            Field("NovoCampo", 42, TipoCampo.Int),
            Field("NovoCampo1", 43, TipoCampo.Double),
            Field("NovoCampo2", 44, TipoCampo.Boolean),
            Field("NovoCampo3", 45, active: false));
        var lines = MappingItem(2, "DocumentLines", TipoColecao.Line, Field("U_Line", 47));
        var batches = MappingItem(3, "BatchNumbers", TipoColecao.Line,
            Field("U_Batch", 48, description: "Novo campo de lote"));
        batches.FK_IdPerfilMapeamentoItemPai = lines.Id;
        var installments = MappingItem(4, "DocumentInstallments", TipoColecao.Line, Field("U_Installment", 49));
        var empty = MappingItem(5, "SerialNumbers", TipoColecao.Line);
        var profile = new PerfilMapeamento
        {
            Id = 1,
            Documento = new Documento(),
            Itens = [header, lines, batches, installments, empty]
        };
        var otherProfile = new PerfilMapeamento
        {
            Id = 2,
            Itens = [MappingItem(6, "Header", TipoColecao.Header, Field("U_OtherProfile", 46))]
        };
        var tempDirectory = Directory.CreateTempSubdirectory("exceldoc-example-");
        var filledFilePath = Path.Combine(tempDirectory.FullName, "Preenchida.xlsx");
        try
        {
            var builder = WebApplication.CreateSlimBuilder(
                new WebApplicationOptions { ContentRootPath = tempDirectory.FullName });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllers().AddApplicationPart(typeof(PlanilhasController).Assembly);
            builder.Services.AddSingleton<IMessageService, StubMessageService>();
            builder.Services.AddSingleton<IPerfilMapeamentoRepository>(
                new StubPerfilMapeamentoRepository { Perfis = [profile, otherProfile] });
            await using var app = builder.Build();
            app.MapControllers().AllowAnonymous();
            await app.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var unscopedDownload = await client.GetAsync("/api/planilhas/exemplo");
            Assert.Equal(HttpStatusCode.BadRequest, unscopedDownload.StatusCode);
            var problem = await unscopedDownload.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Equal(MessageKeys.MappingProfileRequired, problem?.Detail);

            using var unknownProfileDownload = await client.GetAsync("/api/planilhas/exemplo?perfilId=999");
            Assert.Equal(HttpStatusCode.NotFound, unknownProfileDownload.StatusCode);

            using var response = await client.GetAsync("/api/planilhas/exemplo?perfilId=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Planilha Exemplo.xlsx", response.Content.Headers.ContentDisposition?.FileNameStar);
            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            using var workbook = new XLWorkbook(stream);
            var sheet = Assert.Single(workbook.Worksheets);
            Assert.Equal("Principal", sheet.Name);
            Assert.Equal("#", sheet.Cell("A1").GetString());
            Assert.Equal("#", sheet.Cell("A2").GetString());
            Assert.Equal("DocDate", sheet.Cell("B1").GetString());
            Assert.Equal("Data de Lançamento", sheet.Cell("B2").GetString());
            Assert.Equal("ImportFileNum", sheet.Cell("H1").GetString());
            Assert.True(sheet.Column("AN").IsEmpty());
            Assert.Equal("Testando", sheet.Cell("AO1").GetString());
            Assert.True(sheet.Cell("AO2").IsEmpty());
            Assert.Equal("NovoCampo", sheet.Cell("AP1").GetString());
            Assert.Equal("NovoCampo1", sheet.Cell("AQ1").GetString());
            Assert.Equal("NovoCampo2", sheet.Cell("AR1").GetString());
            Assert.Equal("NovoCampo3", sheet.Cell("AS1").GetString());
            Assert.True(sheet.Column("AT").IsEmpty());
            Assert.Equal("U_Line", sheet.Cell("AU1").GetString());
            Assert.Equal("U_Batch", sheet.Cell("AV1").GetString());
            Assert.Equal("Novo campo de lote", sheet.Cell("AV2").GetString());
            Assert.Equal("U_Installment", sheet.Cell("AW1").GetString());
            Assert.Equal([1, 2], sheet.RowsUsed().Select(row => row.RowNumber()));
            Assert.All(sheet.Range("A1:AW2").Cells(), cell =>
            {
                var border = cell.Style.Border;
                Assert.Equal(XLBorderStyleValues.Thin, border.TopBorder);
                Assert.Equal(XLBorderStyleValues.Thin, border.BottomBorder);
                Assert.Equal(XLBorderStyleValues.Thin, border.LeftBorder);
                Assert.Equal(XLBorderStyleValues.Thin, border.RightBorder);
            });
            Assert.Equal(XLBorderStyleValues.None, sheet.Cell("A3").Style.Border.TopBorder);
            Assert.Equal((int)XLPredefinedFormat.DateTime.DayMonthYear4WithSlashes,
                sheet.Cell("B3").Style.NumberFormat.NumberFormatId);
            Assert.Equal((int)XLPredefinedFormat.Number.Text, sheet.Cell("AO3").Style.NumberFormat.NumberFormatId);
            Assert.Equal((int)XLPredefinedFormat.Number.Integer, sheet.Cell("AP3").Style.NumberFormat.NumberFormatId);
            Assert.Equal((int)XLPredefinedFormat.Number.Precision2, sheet.Cell("AQ3").Style.NumberFormat.NumberFormatId);

            sheet.Cell("A3").Value = 1;
            sheet.Cell("B3").Value = new DateTime(2026, 11, 10, 14, 30, 0);
            sheet.Cell("AO3").Value = "00123";
            sheet.Cell("AP3").Value = 42;
            sheet.Cell("AQ3").Value = 2.675432;
            sheet.Cell("AR3").Value = "Sim";
            sheet.Cell("AS3").Value = "Inativo";
            workbook.SaveAs(filledFilePath);

            var messages = new StubMessageService();
            var rows = await new ExcelReaderService(messages).ReadRowsAsync(filledFilePath);
            var group = Assert.Single(new AgrupamentoService(messages).AgruparPorIdExcel(rows));
            Assert.Equal(1, group.IdExcel);
            Assert.Equal(3, Assert.Single(group.Rows).RowNumber);
            var payload = new JsonBuilderService(new PayloadBuilderService(messages))
                .BuildDocumentPayload(profile, group.Rows);
            Assert.Equal("2026-11-10T14:30:00", payload["DocDate"]);
            Assert.Equal("00123", payload["Testando"]);
            Assert.Equal(42, payload["NovoCampo"]);
            Assert.Equal(2.675432, payload["NovoCampo1"]);
            Assert.Equal(true, payload["NovoCampo2"]);
            Assert.False(payload.ContainsKey("NovoCampo3"));
        }
        finally
        {
            File.Delete(filledFilePath);
            tempDirectory.Delete();
        }
    }

    [Fact]
    public async Task DownloadExample_GeneratesIdentifierOnlyWhenMappingsAreEmpty()
    {
        var controller = CreateController(new PerfilMapeamento
        {
            Id = 1,
            Itens = [MappingItem(1, "Header", TipoColecao.Header)]
        });

        var result = Assert.IsType<FileContentResult>(await controller.DownloadExample(1, default));
        using var stream = new MemoryStream(result.FileContents);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Principal");
        Assert.Equal("#", sheet.Cell("A1").GetString());
        Assert.Equal("#", sheet.Cell("A2").GetString());
        Assert.Equal(1, sheet.LastColumnUsed()!.ColumnNumber());
    }

    [Theory]
    [InlineData(1, MessageKeys.ExampleSpreadsheetColumnOutOfRange)]
    [InlineData(41, MessageKeys.ExampleSpreadsheetDuplicateColumn)]
    [InlineData(XLHelper.MaxColumnNumber + 1, MessageKeys.ExampleSpreadsheetColumnOutOfRange)]
    public async Task DownloadExample_ReportsInvalidProfileColumns(int column, string expectedMessage)
    {
        var controller = CreateController(new PerfilMapeamento
        {
            Id = 1,
            Itens =
            [
                MappingItem(1, "Header", TipoColecao.Header, Field("HeaderField", 41)),
                MappingItem(2, "DocumentLines", TipoColecao.Line, Field("LineField", column))
            ]
        });

        var result = Assert.IsType<UnprocessableEntityObjectResult>(await controller.DownloadExample(1, default));
        Assert.Equal(expectedMessage, Assert.IsType<ProblemDetails>(result.Value).Detail);
    }

    [Fact]
    public void DownloadStandardExample_ReturnsOriginalTemplate()
    {
        var result = Assert.IsType<PhysicalFileResult>(CreateController().DownloadStandardExample());
        Assert.Equal("Planilha Exemplo.xlsx", result.FileDownloadName);
        Assert.True(File.Exists(result.FileName));
    }

    private static PlanilhasController CreateController(params PerfilMapeamento[] perfis) =>
        new(new TestWebHostEnvironment(), new StubPerfilMapeamentoRepository { Perfis = perfis }, new StubMessageService());

    private static PerfilMapeamentoItem MappingItem(
        int id, string collectionName, TipoColecao collectionType, params MapeamentoCampo[] fields) =>
        new()
        {
            Id = id,
            Colecao = new Colecao { NomeColecao = collectionName, TipoColecao = collectionType },
            Mapeamento = new Mapeamento { Campos = fields }
        };

    private static MapeamentoCampo Field(
        string name, int column, TipoCampo type = TipoCampo.String, string description = "", bool active = true) =>
        new() { NomeCampo = name, IndiceColuna = column, TipoCampo = type, DescricaoCampo = description, Ativo = active };

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
