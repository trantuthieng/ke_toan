using ClosedXML.Excel;

var file = args.Length > 0 ? args[0] : "test.xlsx";
var maxRows = args.Length > 1 ? int.Parse(args[1]) : 9999;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var wb = new XLWorkbook(file);
foreach (var ws in wb.Worksheets)
{
    Console.WriteLine($"\n=== Sheet: {ws.Name} ===");
    var range = ws.RangeUsed();
    if (range == null) { Console.WriteLine("(empty)"); continue; }
    Console.WriteLine($"Rows: {range.RowCount()}, Cols: {range.ColumnCount()}");
    for (int r = 1; r <= Math.Min(range.RowCount(), maxRows); r++)
    {
        var cells = new List<string>();
        for (int c = 1; c <= range.ColumnCount(); c++)
        {
            var cell = ws.Cell(r, c);
            var val = cell.GetFormattedString();
            if (string.IsNullOrEmpty(val) && cell.IsMerged())
                val = cell.MergedRange().FirstCell().GetFormattedString();
            cells.Add(val);
        }
        Console.WriteLine($"R{r}: {string.Join(" | ", cells)}");
    }
}
