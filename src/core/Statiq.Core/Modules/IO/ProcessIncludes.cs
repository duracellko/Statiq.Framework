﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Statiq.Common;

namespace Statiq.Core
{
    /// <summary>
    /// Processes include statements to include files from the file system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This module will look for include statements in the content of each document and
    /// will replace them with the content of the requested file from the file system.
    /// Include statements take the form <c>^"folder/file.ext"</c>. The given path will be
    /// converted to a <see cref="FilePath"/> and can be absolute or relative. If relative,
    /// it should be relative to the document source. You can escape the include syntax by
    /// prefixing the <c>^</c> with a forward slash <c>\</c>.
    /// </para>
    /// <para>
    /// You can also use the <see cref="IncludeShortcode"/> shortcode to include content.
    /// </para>
    /// </remarks>
    /// <category>Input/Output</category>
    public class ProcessIncludes : ParallelModule
    {
        private bool _recursion = true;

        /// <summary>
        /// Specifies whether the include processing should be recursive. If <c>true</c> (which
        /// is the default), then include statements will also be processed in the content of
        /// included files recursively.
        /// </summary>
        /// <param name="recursion"><c>true</c> if included content should be recursively processed.</param>
        /// <returns>The current module instance.</returns>
        public ProcessIncludes WithRecursion(bool recursion = true)
        {
            _recursion = recursion;
            return this;
        }

        protected override async Task<IEnumerable<IDocument>> ExecuteInputAsync(IDocument input, IExecutionContext context)
        {
            string content = await ProcessIncludesAsync(await input.GetContentStringAsync(), input.Source, context);
            return content == null ? input.Yield() : input.Clone(await context.GetContentProviderAsync(content)).Yield();
        }

        // Returns null if the content wasn't modified
        private async Task<string> ProcessIncludesAsync(string content, FilePath source, IExecutionContext context)
        {
            bool modified = false;

            int start = 0;
            while (start >= 0)
            {
                start = content.IndexOf("^\"", start, StringComparison.Ordinal);
                if (start >= 0)
                {
                    // Check if the include is escaped
                    if (start > 0 && content[start - 1] == '\\')
                    {
                        modified = true;
                        content = content.Remove(start - 1, 1);
                        start++;
                    }
                    else
                    {
                        // This is a valid include
                        int end = content.IndexOf('\"', start + 2);
                        if (end > 0)
                        {
                            modified = true;

                            // Get the correct included path
                            FilePath includedPath = new FilePath(content.Substring(start + 2, end - (start + 2)));
                            if (includedPath.IsRelative)
                            {
                                if (source == null)
                                {
                                    throw new ExecutionException($"Cannot include file at relative path {includedPath.FullPath} because document source is null");
                                }
                                includedPath = source.ChangeFileName(includedPath);
                            }

                            // Get and read the file content
                            IFile includedFile = context.FileSystem.GetFile(includedPath);
                            string includedContent = string.Empty;
                            if (!includedFile.Exists)
                            {
                                context.LogWarning($"Included file {includedFile.Path.FullPath} does not exist");
                            }
                            else
                            {
                                includedContent = await includedFile.ReadAllTextAsync();
                            }

                            // Recursively process include statements
                            if (_recursion)
                            {
                                string nestedContent = await ProcessIncludesAsync(includedContent, includedPath, context);
                                if (nestedContent != null)
                                {
                                    includedContent = nestedContent;
                                }
                            }

                            // Do the replacement
                            content = content.Remove(start, end - start + 1).Insert(start, includedContent);
                            start += includedContent.Length;
                        }
                    }
                }
            }

            return modified ? content : null;
        }
    }
}