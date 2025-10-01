using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using PgVectorDynamicRAG.Factories;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using Microsoft.SemanticKernel.Services;
using Microsoft.SemanticKernel.ChatCompletion;

namespace PgVectorDynamicRAG.Tests
{
    /// <summary>
    /// Comprehensive tests for file parsers and chunking functionality
    /// </summary>
    public class FileParserTests
    {
        private readonly string _testDataPath;
        private readonly Kernel _kernel;
        private readonly ILogger<FileParserTests> _logger;

        public FileParserTests()
        {
            // Set up the test data path
            _testDataPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "TestData");
            
            // Create a logger factory for testing
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<FileParserTests>();
            
            // Create a kernel for testing
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddLogging();
            
            // Register a mock image captioning service for testing
            kernelBuilder.Services.AddKeyedSingleton<IChatCompletionService>("ImageCaptioningService", new MockImageCaptioningService());
            
            _kernel = kernelBuilder.Build();
        }

        [Fact]
        public async Task JsonFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.json");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Json);
            var fileParser = FileParserFactory.GetParser(FileType.Json, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.json", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"JSON Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }
        
        [Fact]
        public async Task JsonFileParser_ShouldDynamicallyChunkLongFile()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file_long.json");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Json);
            var fileParser = FileParserFactory.GetParser(FileType.Json, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            
            // Use different token limits to test dynamic chunking
            // A smaller token limit should result in more chunks
            var smallChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.json", 500, 0.1f);
            var largeChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.json", 2000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            
            // Verify small chunk results
            Assert.NotNull(smallChunkResult);
            Assert.NotEmpty(smallChunkResult.chunkRecords);
            
            // Verify large chunk results
            Assert.NotNull(largeChunkResult);
            Assert.NotEmpty(largeChunkResult.chunkRecords);
            
            // The smaller token limit should generally result in more chunks
            // Unless the file is so small that it fits in a single chunk even with the small limit
            _logger.LogInformation($"JSON Parser (small chunks): Created {smallChunkResult.chunkRecords.Count} chunks");
            _logger.LogInformation($"JSON Parser (large chunks): Created {largeChunkResult.chunkRecords.Count} chunks");
            
            // Verify that each chunk has valid content
            foreach (var chunk in smallChunkResult.chunkRecords)
            {
                Assert.NotNull(chunk.Definition);
                Assert.NotEmpty(chunk.Definition);
                Assert.Equal("json", chunk.Modality);
            }
        }

        [Fact]
        public async Task CsvFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.csv");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Csv);
            var fileParser = FileParserFactory.GetParser(FileType.Csv, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.csv", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"CSV Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }
        
        [Fact]
        public async Task CsvFileParser_ShouldDynamicallyChunkLongFile()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file_long.csv");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Csv);
            var fileParser = FileParserFactory.GetParser(FileType.Csv, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            
            // Use different token limits to test dynamic chunking
            // A smaller token limit should result in more chunks
            var smallChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.csv", 500, 0.1f);
            var largeChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.csv", 2000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            
            // Verify small chunk results
            Assert.NotNull(smallChunkResult);
            Assert.NotEmpty(smallChunkResult.chunkRecords);
            
            // Verify large chunk results
            Assert.NotNull(largeChunkResult);
            Assert.NotEmpty(largeChunkResult.chunkRecords);
            
            // The smaller token limit should generally result in more chunks
            // Unless the file is so small that it fits in a single chunk even with the small limit
            _logger.LogInformation($"CSV Parser (small chunks): Created {smallChunkResult.chunkRecords.Count} chunks");
            _logger.LogInformation($"CSV Parser (large chunks): Created {largeChunkResult.chunkRecords.Count} chunks");
            
            // Verify that each chunk has valid content
            foreach (var chunk in smallChunkResult.chunkRecords)
            {
                Assert.NotNull(chunk.Definition);
                Assert.NotEmpty(chunk.Definition);
                Assert.Equal("tabular", chunk.Modality);
            }
        }

        [Fact]
        public async Task XlsxFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.xlsx");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Xlsx);
            var fileParser = FileParserFactory.GetParser(FileType.Xlsx, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.xlsx", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"XLSX Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }
        
        [Fact]
        public async Task XlsxFileParser_ShouldDynamicallyChunkLongFile()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file_long.xlsx");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Xlsx);
            var fileParser = FileParserFactory.GetParser(FileType.Xlsx, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            
            // Use different token limits to test dynamic chunking
            // A smaller token limit should result in more chunks
            var smallChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.xlsx", 500, 0.1f);
            var largeChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.xlsx", 2000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            
            // Verify small chunk results
            Assert.NotNull(smallChunkResult);
            Assert.NotEmpty(smallChunkResult.chunkRecords);
            
            // Verify large chunk results
            Assert.NotNull(largeChunkResult);
            Assert.NotEmpty(largeChunkResult.chunkRecords);
            
            // The smaller token limit should generally result in more chunks
            // Unless the file is so small that it fits in a single chunk even with the small limit
            _logger.LogInformation($"XLSX Parser (small chunks): Created {smallChunkResult.chunkRecords.Count} chunks");
            _logger.LogInformation($"XLSX Parser (large chunks): Created {largeChunkResult.chunkRecords.Count} chunks");
            
            // Verify that each chunk has valid content
            foreach (var chunk in smallChunkResult.chunkRecords)
            {
                Assert.NotNull(chunk.Definition);
                Assert.NotEmpty(chunk.Definition);
                Assert.Equal("tabular", chunk.Modality);
            }
        }

        [Fact]
        public async Task DocxFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.docx");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Docx);
            var fileParser = FileParserFactory.GetParser(FileType.Docx, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.docx", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"DOCX Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }
        
        [Fact]
        public async Task DocxFileParser_ShouldDynamicallyChunkLongFile()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file_long.docx");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Docx);
            var fileParser = FileParserFactory.GetParser(FileType.Docx, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            
            // Use different token limits to test dynamic chunking
            // A smaller token limit should result in more chunks
            var smallChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.docx", 500, 0.1f);
            var largeChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.docx", 2000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            
            // Verify small chunk results
            Assert.NotNull(smallChunkResult);
            Assert.NotEmpty(smallChunkResult.chunkRecords);
            
            // Verify large chunk results
            Assert.NotNull(largeChunkResult);
            Assert.NotEmpty(largeChunkResult.chunkRecords);
            
            // The smaller token limit should generally result in more chunks
            _logger.LogInformation($"DOCX Parser (small chunks): Created {smallChunkResult.chunkRecords.Count} chunks");
            _logger.LogInformation($"DOCX Parser (large chunks): Created {largeChunkResult.chunkRecords.Count} chunks");
            
            // Verify that each chunk has valid content
            foreach (var chunk in smallChunkResult.chunkRecords)
            {
                Assert.NotNull(chunk.Definition);
                Assert.NotEmpty(chunk.Definition);
                Assert.Equal("text", chunk.Modality);
            }
        }

        [Fact]
        public async Task PdfFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.pdf");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Pdf);
            var fileParser = FileParserFactory.GetParser(FileType.Pdf, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.pdf", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"PDF Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }
        
        [Fact]
        public async Task PdfFileParser_ShouldDynamicallyChunkLongFile()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file_long.pdf");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Pdf);
            var fileParser = FileParserFactory.GetParser(FileType.Pdf, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            
            // Use different token limits to test dynamic chunking
            // A smaller token limit should result in more chunks
            var smallChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.pdf", 500, 0.1f);
            var largeChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.pdf", 2000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            
            // Verify small chunk results
            Assert.NotNull(smallChunkResult);
            Assert.NotEmpty(smallChunkResult.chunkRecords);
            
            // Verify large chunk results
            Assert.NotNull(largeChunkResult);
            Assert.NotEmpty(largeChunkResult.chunkRecords);
            
            // The smaller token limit should generally result in more chunks
            _logger.LogInformation($"PDF Parser (small chunks): Created {smallChunkResult.chunkRecords.Count} chunks");
            _logger.LogInformation($"PDF Parser (large chunks): Created {largeChunkResult.chunkRecords.Count} chunks");
            
            // Verify that each chunk has valid content
            foreach (var chunk in smallChunkResult.chunkRecords)
            {
                Assert.NotNull(chunk.Definition);
                Assert.NotEmpty(chunk.Definition);
        }
    }

    [Fact]
    public async Task TxtFileParser_ShouldParseAndChunk()
    {
        // Arrange
        var filePath = Path.Combine(_testDataPath, "test_file.txt");
        var fileLoader = FileLoaderFactory.GetLoader(FileType.Txt);
        var fileParser = FileParserFactory.GetParser(FileType.Txt, _kernel);
        
        // Act
        var rawData = fileLoader.LoadFile(filePath);
        var parsingResult = await fileParser.Parse(rawData);
        var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.txt", 1000, 0.1f);
        
        // Assert
        Assert.NotNull(parsingResult);
        Assert.NotEmpty(parsingResult.parsedContent);
        Assert.NotNull(chunkingResult);
        Assert.NotEmpty(chunkingResult.chunkRecords);
        
        _logger.LogInformation($"TXT Parser: Created {chunkingResult.chunkRecords.Count} chunks");
    }
    
    [Fact]
    public async Task TxtFileParser_ShouldDynamicallyChunkLongFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDataPath, "test_file_long.txt");
        var fileLoader = FileLoaderFactory.GetLoader(FileType.Txt);
        var fileParser = FileParserFactory.GetParser(FileType.Txt, _kernel);
        
        // Act
        var rawData = fileLoader.LoadFile(filePath);
        var parsingResult = await fileParser.Parse(rawData);
        
        // Use different token limits to test dynamic chunking
        // A smaller token limit should result in more chunks
        var smallChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.txt", 500, 0.1f);
        var largeChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.txt", 2000, 0.1f);
        
        // Assert
        Assert.NotNull(parsingResult);
        Assert.NotEmpty(parsingResult.parsedContent);
        
        // Verify small chunk results
        Assert.NotNull(smallChunkResult);
        Assert.NotEmpty(smallChunkResult.chunkRecords);
        
        // Verify large chunk results
        Assert.NotNull(largeChunkResult);
        Assert.NotEmpty(largeChunkResult.chunkRecords);
        
        // The smaller token limit should generally result in more chunks
        // Unless the file is so small that it fits in a single chunk even with the small limit
        _logger.LogInformation($"TXT Parser (small chunks): Created {smallChunkResult.chunkRecords.Count} chunks");
        _logger.LogInformation($"TXT Parser (large chunks): Created {largeChunkResult.chunkRecords.Count} chunks");
        
        // Verify that each chunk has valid content
        foreach (var chunk in smallChunkResult.chunkRecords)
        {
            Assert.NotNull(chunk.Definition);
            Assert.NotEmpty(chunk.Definition);
            Assert.Equal("text", chunk.Modality);
        }
    }

    [Fact]
        public async Task XmlFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.xml");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Xml);
            var fileParser = FileParserFactory.GetParser(FileType.Xml, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.xml", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"XML Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }
        
        [Fact]
        public async Task XmlFileParser_ShouldDynamicallyChunkLongFile()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file_long.xml");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Xml);
            var fileParser = FileParserFactory.GetParser(FileType.Xml, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            
            // Use different token limits to test dynamic chunking
            // A smaller token limit should result in more chunks
            var smallChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.xml", 500, 0.1f);
            var largeChunkResult = await fileParser.Chunk(parsingResult, "test_file_long.xml", 2000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotEmpty(parsingResult.parsedContent);
            
            // Verify small chunk results
            Assert.NotNull(smallChunkResult);
            Assert.NotEmpty(smallChunkResult.chunkRecords);
            
            // Verify large chunk results
            Assert.NotNull(largeChunkResult);
            Assert.NotEmpty(largeChunkResult.chunkRecords);
            
            // The smaller token limit should generally result in more chunks
            // Unless the file is so small that it fits in a single chunk even with the small limit
            _logger.LogInformation($"XML Parser (small chunks): Created {smallChunkResult.chunkRecords.Count} chunks");
            _logger.LogInformation($"XML Parser (large chunks): Created {largeChunkResult.chunkRecords.Count} chunks");
            
            // Verify that each chunk has valid content
            foreach (var chunk in smallChunkResult.chunkRecords)
            {
                Assert.NotNull(chunk.Definition);
                Assert.NotEmpty(chunk.Definition);
                Assert.Equal("xml", chunk.Modality);
            }
        }

        [Fact]
        public async Task PngFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.png");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Png);
            var fileParser = FileParserFactory.GetParser(FileType.Png, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.png", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"PNG Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }

        [Fact]
        public async Task JpegFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.jpg");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Jpeg);
            var fileParser = FileParserFactory.GetParser(FileType.Jpeg, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.jpg", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"JPEG Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }

        [Fact]
        public async Task BmpFileParser_ShouldParseAndChunk()
        {
            // Arrange
            var filePath = Path.Combine(_testDataPath, "test_file.bmp");
            var fileLoader = FileLoaderFactory.GetLoader(FileType.Bmp);
            var fileParser = FileParserFactory.GetParser(FileType.Bmp, _kernel);
            
            // Act
            var rawData = fileLoader.LoadFile(filePath);
            var parsingResult = await fileParser.Parse(rawData);
            var chunkingResult = await fileParser.Chunk(parsingResult, "test_file.bmp", 1000, 0.1f);
            
            // Assert
            Assert.NotNull(parsingResult);
            Assert.NotNull(chunkingResult);
            Assert.NotEmpty(chunkingResult.chunkRecords);
            
            _logger.LogInformation($"BMP Parser: Created {chunkingResult.chunkRecords.Count} chunks");
        }
    }
    
    /// <summary>
    /// Mock implementation of IChatCompletionService for testing purposes
    /// </summary>
    public class MockImageCaptioningService : IChatCompletionService
    {
        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory, 
            PromptExecutionSettings? executionSettings = null, 
            Kernel? kernel = null, 
            CancellationToken cancellationToken = default)
        {
            // Return a simple mock caption for any image
            var response = new List<ChatMessageContent>
            {
                new ChatMessageContent(
                    AuthorRole.User,
                    "This is a mock image caption for testing purposes.")
            };
            
            return Task.FromResult<IReadOnlyList<ChatMessageContent>>(response);
        }
        
        public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            // Return an empty async enumerable for testing
            return AsyncEnumerable.Empty<StreamingChatMessageContent>();
        }
        
        public ChatHistory CreateNewChat(string? instructions = null)
        {
            return new ChatHistory(instructions ?? "");
        }
        
        // Implement required interface properties with minimal implementations
        public object? ServiceProvider => null;

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();
    }
}
