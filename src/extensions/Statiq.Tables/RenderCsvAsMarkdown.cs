﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Statiq.Common;

namespace Statiq.Tables
{
    /// <summary>
    /// Converts CSV content to Markdown tables.
    /// </summary>
    /// <remarks>
    /// This module reads the content of each input document as CSV and outputs an Markdown table
    /// containing the CSV content. The input CSV content must use <c>,</c> as separator and enclose
    /// every value in <c>"</c>. The output table has the format
    ///
    /// +--------------+-------------+
    /// | Test value 1 | TestValue 2 |
    /// +--------------+-------------+
    /// | Test value 2 | TestValue 3 |
    /// +--------------+-------------+
    /// </remarks>
    /// <category>Content</category>
    public class RenderCsvAsMarkdown : ParallelModule
    {
        private bool _firstLineHeader = false;

        /// <summary>
        /// Treats the first line of input content as a header and generates <c>&lt;th&gt;</c> tags in the output table.
        /// </summary>
        /// <returns>The current module instance.</returns>
        public RenderCsvAsMarkdown WithHeader()
        {
            _firstLineHeader = true;
            return this;
        }

        protected override async Task<IEnumerable<IDocument>> ExecuteInputAsync(IDocument input, IExecutionContext context)
        {
            IEnumerable<IEnumerable<string>> records;
            using (Stream stream = input.GetContentStream())
            {
                records = CsvFile.GetAllRecords(stream);
            }

            StringBuilder builder = new StringBuilder();

            int columnCount = records.First().Count();

            int[] columnSize = new int[columnCount];

            foreach (IEnumerable<string> row in records)
            {
                for (int i = 0; i < row.Count(); i++)
                {
                    string cell = row.ElementAt(i);
                    columnSize[i] = Math.Max(columnSize[i], cell.Length);
                }
            }

            bool firstLine = true;
            WriteLine(builder, columnSize);
            foreach (IEnumerable<string> row in records)
            {
                builder.Append("|");
                for (int i = 0; i < columnSize.Length; i++)
                {
                    builder.Append(" ");
                    builder.Append(row.ElementAt(i));
                    builder.Append(' ', columnSize[i] - row.ElementAt(i).Length + 1);
                    builder.Append("|");
                }
                builder.AppendLine();
                WriteLine(builder, columnSize, _firstLineHeader && firstLine);
                firstLine = false;
            }

            return input.Clone(await context.GetContentProviderAsync(builder.ToString())).Yield();
        }

        private static void WriteLine(StringBuilder builder, int[] columnSize, bool isHeader = false)
        {
            foreach (int column in columnSize)
            {
                builder.Append("+");
                builder.Append(isHeader ? '=' : '-', column + 2);
            }
            builder.AppendLine("+");
        }
    }
}
