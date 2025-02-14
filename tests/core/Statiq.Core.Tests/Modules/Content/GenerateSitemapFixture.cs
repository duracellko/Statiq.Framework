﻿using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;
using Statiq.Common;
using Statiq.Testing;

namespace Statiq.Core.Tests.Modules.Contents
{
    [TestFixture]
    public class GenerateSitemapFixture : BaseFixture
    {
        public class ExecuteTests : GenerateSitemapFixture
        {
            [TestCase("www.example.org", null, "http://www.example.org/sub/testfile")]
            [TestCase(null, "http://www.example.com", "http://www.example.com")]
            [TestCase("www.example.org", "http://www.example.com/{0}", "http://www.example.com/sub/testfile.html")]
            public async Task SitemapGeneratedWithSitemapItem(string hostname, string formatterString, string expected)
            {
                // Given
                TestExecutionContext context = new TestExecutionContext();
                context.Settings[Keys.LinkHideExtensions] = "true";
                if (!string.IsNullOrWhiteSpace(hostname))
                {
                    context.Settings[Keys.Host] = hostname;
                }

                TestDocument doc = new TestDocument(new FilePath("sub/testfile.html"), "Test");
                IDocument[] inputs = { doc };

                AddMetadata m = new AddMetadata(
                    Keys.SitemapItem,
                    Config.FromDocument(d => new SitemapItem(d.Destination.FullPath)));

                Func<string, string> formatter = null;

                if (!string.IsNullOrWhiteSpace(formatterString))
                {
                    formatter = f => string.Format(formatterString, f);
                }

                GenerateSitemap sitemap = new GenerateSitemap(formatter);

                // When
                TestDocument result = await ExecuteAsync(doc, context, m, sitemap).SingleAsync();

                // Then
                result.Content.ShouldContain($"<loc>{expected}</loc>");
            }

            [TestCase("www.example.org", null, "http://www.example.org/sub/testfile")]
            [TestCase(null, "http://www.example.com", "http://www.example.com")]
            [TestCase("www.example.org", "http://www.example.com/{0}", "http://www.example.com/sub/testfile.html")]
            public async Task SitemapGeneratedWithSitemapItemAsString(string hostname, string formatterString, string expected)
            {
                // Given
                TestExecutionContext context = new TestExecutionContext();
                context.Settings[Keys.LinkHideExtensions] = "true";
                if (!string.IsNullOrWhiteSpace(hostname))
                {
                    context.Settings[Keys.Host] = hostname;
                }

                TestDocument doc = new TestDocument(new FilePath("sub/testfile.html"), "Test");
                IDocument[] inputs = { doc };

                AddMetadata m = new AddMetadata(
                    Keys.SitemapItem,
                    Config.FromDocument(d => d.Destination.FullPath));

                Func<string, string> formatter = null;

                if (!string.IsNullOrWhiteSpace(formatterString))
                {
                    formatter = f => string.Format(formatterString, f);
                }

                GenerateSitemap sitemap = new GenerateSitemap(formatter);

                // When
                TestDocument result = await ExecuteAsync(doc, context, m, sitemap).SingleAsync();

                // Then
                result.Content.ShouldContain($"<loc>{expected}</loc>");
            }

            [TestCase("www.example.org", null, "http://www.example.org/sub/testfile")]
            [TestCase(null, "http://www.example.com", "http://www.example.com")]
            [TestCase("www.example.org", "http://www.example.com{0}", "http://www.example.com/sub/testfile")]
            public async Task SitemapGeneratedWhenNoSitemapItem(string hostname, string formatterString, string expected)
            {
                // Given
                TestExecutionContext context = new TestExecutionContext();
                context.Settings[Keys.LinkHideExtensions] = "true";
                if (!string.IsNullOrWhiteSpace(hostname))
                {
                    context.Settings[Keys.Host] = hostname;
                }

                TestDocument doc = new TestDocument(new FilePath("sub/testfile.html"), "Test");

                Func<string, string> formatter = null;

                if (!string.IsNullOrWhiteSpace(formatterString))
                {
                    formatter = f => string.Format(formatterString, f);
                }

                GenerateSitemap sitemap = new GenerateSitemap(formatter);

                // When
                TestDocument result = await ExecuteAsync(doc, context, sitemap).SingleAsync();

                // Then
                result.Content.ShouldContain($"<loc>{expected}</loc>");
            }
        }
    }
}
