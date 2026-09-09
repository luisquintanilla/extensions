// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Extensions.DataIngestion;

internal static class DocumentExtractionBridgeFixture
{
    internal const string ProviderMarkdown =
        "# EXTRACTION_ONLY_MARKDOWN\n\nDuplicate provider rendering.";

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
                        "Use `VectorStoreWriter<TRecord>` with [links](https://example.test).")
                    {
                        Kind = DocumentBlockKind.Paragraph,
                    },
                    new DocumentBlock(
                        "public static void Main()\n{\n    Console.WriteLine(\"``` inside code\");\n}")
                    {
                        Kind = DocumentBlockKind.Code,
                    },
                    new DocumentTable(
                        2,
                        2,
                        [
                            new DocumentTableCell(
                                0,
                                0,
                                [
                                    new DocumentBlock("Region"),
                                    new DocumentTable(
                                        1,
                                        1,
                                        [
                                            new DocumentTableCell(
                                                0,
                                                0,
                                                [new DocumentBlock("Nested value")]),
                                        ]),
                                    new DocumentImage
                                    {
                                        Content = new byte[] { 9, 8, 7 },
                                        MediaType = "image/png",
                                        Caption = "Nested chart",
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
                                [new DocumentBlock("$12M")]),
                        ]),
                    new DocumentImage
                    {
                        Content = new byte[] { 1, 2, 3, 4 },
                        MediaType = "image/png",
                    },
                    new DocumentBlock("provider-specific note")
                    {
                        Kind = new("provider-note"),
                    },
                ],
                ProviderMarkdown),
            new DocumentPage(
                2,
                [
                    new DocumentBlock("Appendix")
                    {
                        Kind = DocumentBlockKind.Title,
                    },
                    new DocumentBlock("Retention policy is unchanged.")
                    {
                        Kind = DocumentBlockKind.Paragraph,
                    },
                ]),
        ])
        {
            Usage = new()
            {
                PagesProcessed = 2,
                TotalTokenCount = 42,
            },
            RawRepresentation = new object(),
            AdditionalProperties = new()
            {
                ["provider.result"] = "not-mapped",
            },
        };
}
