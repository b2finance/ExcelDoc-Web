using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Sap;
using ExcelDoc.Server.Services;
using ExcelDoc.Server.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExcelDoc.Server.Tests;

public sealed class SapDatabaseInitializerTests
{
    private sealed class TestSapClient(bool isSuperuser) : ISapServiceLayerClient
    {
        public int Checks { get; private set; }
        public Task<bool> IsSuperuserAsync(SapSessionContext session, CancellationToken cancellationToken = default) { Checks++; return Task.FromResult(isSuperuser); }
        public Task<string> GetInstallationNumberAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SapSessionContext> LoginAsync(string serviceLayerUrl, string database, string userName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> PostProcessamentoAsync(SapSessionContext session, string endpoint, object payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, int>> GetNFModelsAsync(SapSessionContext session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task InitializeAsync_RequiresSapSuperuserEvenForAdministrator()
    {
        var client = new TestSapClient(false);
        var initializer = new SapDatabaseInitializer(NullLogger<SapDatabaseInitializer>.Instance, new SapSchemaInstaller(null!), new InMemoryUdtStore(), client);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.InitializeAsync(new SapSessionContext { Database = "test" }));
        Assert.Contains("superusuário", exception.Message);
        Assert.Equal(1, client.Checks);
    }

    [Fact]
    public async Task InitializeAsync_UpgradesDefaultMappingsFromUpdatedSpreadsheetWithoutDuplicates()
    {
        var store = new InMemoryUdtStore();
        store.Seed(
            SapUdtSchema.Schema,
            new Dictionary<string, object?>
            {
                ["SchemaVersion"] = 2,
                ["SeedVersion"] = 3
            });
        store.Seed(
            SapUdtSchema.Colecao,
            new Dictionary<string, object?>
            {
                ["NomeColecao"] = "BatchNumbers",
                ["Descricao"] = "Lotes vinculados a uma DocumentLine.",
                ["TipoColecao"] = (int)TipoColecao.Line,
                ["IsPadrao"] = "Y"
            });
        store.Seed(
            SapUdtSchema.Mapeamento,
            new Dictionary<string, object?>
            {
                ["Nome"] = "Mapeamento Padrão - BatchNumbers",
                ["ColecaoId"] = 1,
                ["IsPadrao"] = "Y",
                ["DataCriacao"] = "2026-01-01T00:00:00Z"
            });
        store.Seed(
            SapUdtSchema.MapeamentoCampo,
            new Dictionary<string, object?>
            {
                ["IndiceColuna"] = 47,
                ["NomeCampo"] = "BatchNumber",
                ["DescricaoCampo"] = "Número do Lote",
                ["TipoCampo"] = (int)TipoCampo.String,
                ["Formato"] = null,
                ["Ativo"] = "Y",
                ["MapeamentoId"] = 1
            });
        store.Seed(
            SapUdtSchema.MapeamentoCampo,
            new Dictionary<string, object?>
            {
                ["IndiceColuna"] = 48,
                ["NomeCampo"] = "Quantity",
                ["DescricaoCampo"] = "Quantidade do Lote",
                ["TipoCampo"] = (int)TipoCampo.Double,
                ["Formato"] = null,
                ["Ativo"] = "Y",
                ["MapeamentoId"] = 1
            });
        var initializer = new SapDatabaseInitializer(
            NullLogger<SapDatabaseInitializer>.Instance,
            new SapSchemaInstaller(null!),
            store,
            new TestSapClient(true));
        var session = new SapSessionContext
        {
            ServiceLayerBaseUrl = "https://sap.example.test:50000/b1s/v1/",
            Database = "SBOTEST"
        };

        await initializer.InitializeAsync(session);
        await initializer.InitializeAsync(session);

        var mapping = Assert.Single(
            store.Rows(SapUdtSchema.Mapeamento),
            row => Equals(
                row["Nome"],
                "Mapeamento Padrão - BatchNumbers"));
        var batchFields = store.Rows(SapUdtSchema.MapeamentoCampo)
            .Where(row => Equals(row["MapeamentoId"], mapping["Code"]))
            .OrderBy(row => row["IndiceColuna"])
            .ToList();

        Assert.Collection(
            batchFields,
            field =>
            {
                Assert.Equal("BatchNumber", field["NomeCampo"]);
                Assert.Equal("Número do lote", field["DescricaoCampo"]);
                Assert.Equal(29, field["IndiceColuna"]);
                Assert.Equal((int)TipoCampo.String, field["TipoCampo"]);
            },
            field =>
            {
                Assert.Equal("Quantity", field["NomeCampo"]);
                Assert.Equal("Quantidade do lote", field["DescricaoCampo"]);
                Assert.Equal(30, field["IndiceColuna"]);
                Assert.Equal((int)TipoCampo.Double, field["TipoCampo"]);
            });

        var allFields = store.Rows(SapUdtSchema.MapeamentoCampo);
        Assert.Equal(38, allFields.Count);
        Assert.DoesNotContain(allFields, field => Equals(field["NomeCampo"], "LineTotal"));
        Assert.DoesNotContain(allFields, field => Equals(field["NomeCampo"], "InstallmentId"));
        Assert.Contains(
            allFields,
            field =>
                Equals(field["NomeCampo"], "TaxableAmount") &&
                Equals(field["IndiceColuna"], 31));
        Assert.Contains(
            allFields,
            field =>
                Equals(field["NomeCampo"], "WTLiable") &&
                Equals(field["IndiceColuna"], 19) &&
                Equals(field["TipoCampo"], (int)TipoCampo.Boolean));
        Assert.Contains(
            allFields,
            field =>
                Equals(field["NomeCampo"], "U_IB_QrCodePix") &&
                Equals(field["IndiceColuna"], 37));

        var installationState = Assert.Single(store.Rows(SapUdtSchema.Schema));
        Assert.Equal(4, installationState["SeedVersion"]);
    }

    private sealed class InMemoryUdtStore : ISapUdtStore
    {
        private static readonly Regex IntegerFilter = new(
            @"^\(?U_(?<field>\w+) eq (?<value>-?\d+)\)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly Dictionary<string, List<Dictionary<string, object?>>> _tables =
            new(StringComparer.Ordinal);

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(string tableName) =>
            GetTable(tableName);

        public void Seed(
            string tableName,
            IReadOnlyDictionary<string, object?> fields)
        {
            _ = AddAsync(tableName, fields).GetAwaiter().GetResult();
        }

        public Task<IReadOnlyList<SapUdtRecord>> QueryAsync(
            string tableName,
            string? filter = null,
            string? orderBy = null,
            int? top = null,
            int? skip = null,
            string? select = null,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Dictionary<string, object?>> rows = GetTable(tableName);
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var match = IntegerFilter.Match(filter);
                if (!match.Success)
                {
                    throw new NotSupportedException($"Filtro não suportado no teste: {filter}");
                }

                var field = match.Groups["field"].Value;
                var value = int.Parse(match.Groups["value"].Value);
                rows = rows.Where(row => Convert.ToInt32(row.GetValueOrDefault(field)) == value);
            }

            if (skip.HasValue)
            {
                rows = rows.Skip(skip.Value);
            }

            if (top.HasValue)
            {
                rows = rows.Take(top.Value);
            }

            return Task.FromResult<IReadOnlyList<SapUdtRecord>>(
                rows.Select(ToRecord).ToList());
        }

        public Task<SapUdtRecord?> GetByIdAsync(
            string tableName,
            int id,
            CancellationToken cancellationToken = default)
        {
            var row = GetTable(tableName)
                .SingleOrDefault(value => Equals(value["Code"], id));
            return Task.FromResult(row is null ? null : ToRecord(row));
        }

        public Task<int> CountAsync(
            string tableName,
            string? filter = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GetTable(tableName).Count);

        public Task<int> AddAsync(
            string tableName,
            IReadOnlyDictionary<string, object?> fields,
            string? name = null,
            CancellationToken cancellationToken = default)
        {
            var table = GetTable(tableName);
            var id = table.Count == 0
                ? 1
                : table.Max(row => Convert.ToInt32(row["Code"])) + 1;
            var row = new Dictionary<string, object?>(fields, StringComparer.Ordinal)
            {
                ["Code"] = id,
                ["Name"] = name ?? id.ToString()
            };
            table.Add(row);
            return Task.FromResult(id);
        }

        public Task UpdateAsync(
            string tableName,
            int id,
            IReadOnlyDictionary<string, object?> fields,
            CancellationToken cancellationToken = default)
        {
            var row = GetTable(tableName).Single(value => Equals(value["Code"], id));
            foreach (var field in fields)
            {
                row[field.Key] = field.Value;
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string tableName,
            int id,
            CancellationToken cancellationToken = default)
        {
            GetTable(tableName).RemoveAll(row => Equals(row["Code"], id));
            return Task.CompletedTask;
        }

        private List<Dictionary<string, object?>> GetTable(string tableName)
        {
            if (!_tables.TryGetValue(tableName, out var table))
            {
                table = [];
                _tables[tableName] = table;
            }

            return table;
        }

        private static SapUdtRecord ToRecord(
            IReadOnlyDictionary<string, object?> row)
        {
            var serialized = row.ToDictionary(
                field => field.Key is "Code" or "Name"
                    ? field.Key
                    : SapUdtSchema.Field(field.Key),
                field => field.Value,
                StringComparer.Ordinal);
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(serialized));
            return new SapUdtRecord(document.RootElement);
        }
    }
}
