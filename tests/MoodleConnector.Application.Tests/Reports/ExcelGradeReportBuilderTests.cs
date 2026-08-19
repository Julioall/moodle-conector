using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Infrastructure.Reports;

namespace MoodleConnector.Application.Tests.Reports;

public sealed class ExcelGradeReportBuilderTests
{
    [Fact]
    public void BuildWorkbook_CriaApenasResumoComUltimoAcessoEFormatacao()
    {
        var students = new[]
        {
            new CourseGradeReportStudentRow("student-1", "Ana & João", new DateTimeOffset(2026, 8, 17, 12, 30, 0, TimeSpan.Zero), 85m, 100m, 85m, "85,00", "com_nota"),
            new CourseGradeReportStudentRow("student-2", "Bruno", null, null, 10m, null, null, "sem_nota"),
            new CourseGradeReportStudentRow("student-3", "Carlos", null, 39m, 100m, 90m, "39,00", "com_nota"),
            new CourseGradeReportStudentRow("student-4", "Diana", null, 40m, 100m, 10m, "40,00", "com_nota"),
            new CourseGradeReportStudentRow("student-5", "Elisa", new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero), 60m, 100m, 20m, "60,00", "com_nota"),
        };
        var units = new[]
        {
            new ExcelGradeUnit("101", "Introducao / Fundamentos", students),
            new ExcelGradeUnit("102", "Introducao / Fundamentos", students),
        };

        var reportCreatedAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var bytes = ExcelGradeReportBuilder.BuildWorkbook("Turma A", reportCreatedAt, units);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var workbookXml = ReadEntry(archive, "xl/workbook.xml");
        var summaryXml = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        var stylesXml = ReadEntry(archive, "xl/styles.xml");
        var summaryDocument = XDocument.Parse(summaryXml);

        Assert.Contains("name=\"Resumo\"", workbookXml);
        Assert.DoesNotContain("Introducao Fundamentos", workbookXml);
        Assert.Contains("Introducao / Fundamentos", summaryXml);
        Assert.DoesNotContain("definedNames", workbookXml);
        Assert.Contains("Nome Completo", summaryXml);
        Assert.Contains("Último acesso", summaryXml);
        Assert.Contains("20/08/2026 09:00", summaryXml);
        Assert.Contains("3 dias", summaryXml);
        Assert.Contains("Hoje", summaryXml);
        Assert.Contains("Métrica usada nas cores", summaryXml);
        Assert.Contains("Nota total do curso", summaryXml);
        Assert.Contains("&lt; 40 vermelho | 40 a &lt; 60 amarelo | &gt;= 60 verde", summaryXml);
        Assert.Contains("Ana &amp; João", summaryXml);
        Assert.Contains("3 dias", summaryXml);
        Assert.Contains("<c r=\"C5\" s=\"3\"><v>85</v>", summaryXml);
        Assert.Contains("<c r=\"C7\" s=\"5\"><v>39</v>", summaryXml);
        Assert.Contains("<c r=\"C8\" s=\"4\"><v>40</v>", summaryXml);
        Assert.Contains("<c r=\"C9\" s=\"3\"><v>60</v>", summaryXml);
        Assert.Contains("numFmtId=\"164\"", stylesXml);
        Assert.Null(archive.GetEntry("xl/worksheets/sheet2.xml"));
        Assert.Null(archive.GetEntry("xl/worksheets/sheet3.xml"));
        Assert.Equal(1, archive.Entries.Count(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)));
        Assert.Equal("sheetPr", summaryDocument.Root!.Elements().First().Name.LocalName);
        Assert.Equal("sheetData", summaryDocument.Root.Elements().Single(element => element.Name.LocalName == "sheetData").Name.LocalName);
    }

    [Fact]
    public void BuildZip_EntregaUmArquivoPorTurma()
    {
        var workbook = ExcelGradeReportBuilder.BuildWorkbook("Turma A", DateTimeOffset.UtcNow, []);
        var zip = ExcelGradeReportBuilder.BuildZip([
            ("turma-a.xlsx", workbook),
            ("turma-b.xlsx", workbook),
        ]);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("turma-a.xlsx"));
        Assert.NotNull(archive.GetEntry("turma-b.xlsx"));
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
