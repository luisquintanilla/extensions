// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Extensions.DataIngestion;

internal static class DocumentExtractionBridgeFixture
{
    internal const string PageOneMarkdown =
        "# Quarterly *Report*\n\nThis provider rendering must not replace canonical elements.";

    internal static DocumentExtractionResult Create()
        => new(
        [
            new DocumentPage(
                1,
                [
                    new DocumentBlock("Quarterly *Report*")
                    {
                        Kind = DocumentBlockKind.Title,
                        Confidence = 0.99,
                        RawRepresentation = new object(),
                    },
                    new DocumentBlock(
                        "Use `VectorStoreWriter<TRecord>` with [links](https://example.test), "
                        + "<tags>, and _literal underscores_.")
                    {
                        Kind = DocumentBlockKind.Paragraph,
                        Confidence = 0.97,
                    },
                    new DocumentBlock(
                        string.Join(
                            "\n",
                            [
                                "public static void Main()",
                                "{",
                                "    Console.WriteLine(\"``` inside code\");",
                                "    Console.WriteLine(\"line 2\");",
                                "    Console.WriteLine(\"line 3\");",
                                "    Console.WriteLine(\"line 4\");",
                                "}",
                            ]))
                    {
                        Kind = DocumentBlockKind.Code,
                    },
                    new DocumentTable(
                        rowCount: 2,
                        columnCount: 2,
                        cells:
                        [
                            new DocumentTableCell(
                                0,
                                0,
                                [
                                    new DocumentBlock("Region"),
                                    new DocumentBlock("region-id")
                                    {
                                        Kind = DocumentBlockKind.Code,
                                    },
                                ])
                            {
                                Kind = DocumentTableCellKind.RowHeader,
                                RowSpan = 2,
                            },
                            new DocumentTableCell(
                                0,
                                1,
                                [new DocumentBlock("Revenue")])
                            {
                                Kind = DocumentTableCellKind.ColumnHeader,
                            },
                            new DocumentTableCell(
                                1,
                                1,
                                [new DocumentBlock("$12M")])
                            {
                                Kind = DocumentTableCellKind.Content,
                            },
                        ]),
                    new DocumentImage
                    {
                        Content = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                        MediaType = "image/png",
                    },
                    new DocumentBlock("Provider-specific note")
                    {
                        Kind = new("provider-note"),
                    },
                ],
                PageOneMarkdown),
            new DocumentPage(
                2,
                [
                    new DocumentBlock("Appendix")
                    {
                        Kind = DocumentBlockKind.Title,
                    },
                    new DocumentBlock(
                        "Methodology uses `SearchAsync<TInput>` and preserves [page 2] references.")
                    {
                        Kind = DocumentBlockKind.Paragraph,
                    },
                ],
                "# Appendix\n\nProvider Markdown for page 2."),
        ]);
}
