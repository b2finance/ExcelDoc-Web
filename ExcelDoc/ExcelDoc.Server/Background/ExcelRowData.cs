namespace ExcelDoc.Server.Background
{
    public class ExcelRowData
    {
        public int RowNumber { get; set; }

        public IReadOnlyDictionary<int, string?> Values { get; set; } = new Dictionary<int, string?>();

        public IReadOnlyDictionary<int, double>? NumericValues { get; set; }
    }
}
