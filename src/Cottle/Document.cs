using System;
using System.IO;
using Cottle.Documents;

namespace Cottle
{
    public static class Document
    {
        /// <summary>
        /// Get empty document instance.
        /// </summary>
        public static IDocument Empty => EmptyDocument.Instance;

        /// <summary>
        /// Create default document from given Cottle template (as a text reader). Default document will be rendered
        /// using a C# template interpreter and offers a good compromise between cost and performance.
        /// </summary>
        /// <param name="template">Cottle template</param>
        /// <param name="configuration">Template engine configuration or null to use default configuration</param>
        /// <returns>Document or error details</returns>
        public static DocumentResult CreateDefault(TextReader template, DocumentConfiguration configuration = default)
        {
            return Document.Create(template, configuration, statement => new EvaluatedDocument(statement));
        }

        /// <summary>
        /// Create default document from given Cottle template (as a string). Default document will be rendered using a
        /// C# template interpreter and offers a good compromise between cost and performance.
        /// </summary>
        /// <param name="template">Cottle template</param>
        /// <param name="configuration">Template engine configuration or null to use default configuration</param>
        /// <returns>Document or error details</returns>
        public static DocumentResult CreateDefault(string template, DocumentConfiguration configuration = default)
        {
            using (var reader = new StringReader(template))
            {
                return Document.CreateDefault(reader, configuration);
            }
        }

        /// <summary>
        /// Create native document from given Cottle template (as a text reader). Native document uses IL code
        /// generation for better execution performance but has a significant construction cost. Code generated by JIT
        /// compiler can be reclaimed by garbage collector but you should use a caching mechanism to avoid re-creating
        /// multiple compiled document from the same template nonetheless.
        /// </summary>
        /// <param name="template">Cottle template</param>
        /// <param name="configuration">Template engine configuration or null to use default configuration</param>
        /// <returns>Document or error details</returns>
        public static DocumentResult CreateNative(TextReader template, DocumentConfiguration configuration = default)
        {
            return Document.Create(template, configuration, statement => new EmittedDocument(statement));
        }

        /// <summary>
        /// Create native document from given Cottle template (as a string). Native document uses IL code generation for
        /// better execution performance but has a significant construction cost. Code generated by JIT compiler can be
        /// reclaimed by garbage collector but you should use a caching mechanism to avoid re-creating multiple compiled
        /// document from the same template nonetheless.
        /// </summary>
        /// <param name="template">Cottle template</param>
        /// <param name="configuration">Template engine configuration or null to use default configuration</param>
        /// <returns>Document or error details</returns>
        public static DocumentResult CreateNative(string template, DocumentConfiguration configuration = default)
        {
            using (var reader = new StringReader(template))
            {
                return Document.CreateNative(reader, configuration);
            }
        }

        private static DocumentResult Create(TextReader template, DocumentConfiguration configuration,
            Func<Statement, IDocument> constructor)
        {
            var parser = Parser.Create(configuration);

            return parser.Parse(template, out var statement, out var reports)
                ? DocumentResult.CreateSuccess(constructor(statement))
                : DocumentResult.CreateFailure(reports);
        }
    }
}