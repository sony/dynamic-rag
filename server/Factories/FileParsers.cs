using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using PgVectorDynamicRAG.Data;
using PgVectorDynamicRAG.Embeddings;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Net.Http;
using Newtonsoft.Json;

namespace PgVectorDynamicRAG.Factories
{
    /// <summary>
    /// Utility class for AWS Bedrock image captioning
    /// </summary>
    public static class AwsImageCaptioningUtility
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Generates a caption for the provided image data using direct AWS Bedrock HTTP requests.
        /// </summary>
        /// <param name="imageData">The image data as a byte array.</param>
        /// <returns>A generated caption for the image.</returns>
        public static async Task<string> GenerateImageCaptionAws(byte[] imageData)
        {
            try
            {
                var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-west-2";
                var modelId = Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_ID") ?? "anthropic.claude-3-5-sonnet-20241022-v2:0";
                var token = Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_API_KEY");
                
                if (string.IsNullOrEmpty(token))
                {
                    throw new InvalidOperationException("IMAGE_CAPTION_MODEL_API_KEY environment variable is required for AWS provider");
                }

                var url = $"https://bedrock-runtime.{region}.amazonaws.com/model/{modelId}/converse";
                var base64Image = Convert.ToBase64String(imageData);

                // Determine image format from image data (simple heuristic)
                string imageFormat = "jpeg"; // default
                if (imageData.Length > 8)
                {
                    var header = imageData.Take(8).ToArray();
                    if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                        imageFormat = "png";
                    else if (header[0] == 0x42 && header[1] == 0x4D)
                        imageFormat = "bmp";
                    else if (header[0] == 0xFF && header[1] == 0xD8)
                        imageFormat = "jpeg";
                }

                var payload = new
                {
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { text = "Please describe this image in a short, concise manner." },
                                new
                                {
                                    image = new
                                    {
                                        format = imageFormat,
                                        source = new { bytes = base64Image }
                                    }
                                }
                            }
                        }
                    },
                    inferenceConfig = new { maxTokens = 512 }
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"AWS Bedrock request failed: {response.StatusCode} - {responseContent}");
                }

                dynamic responseObj = JsonConvert.DeserializeObject(responseContent);
                return responseObj?.output?.message?.content?[0]?.text?.ToString() ?? "Unable to generate caption";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating AWS image caption: {ex.Message}");
                return "Error generating image caption";
            }
        }
    }
    /// <summary>
    /// Represents the result of parsing a file, including the main parsed content and additional metadata.
    /// </summary>
    #region FileParsingResult
    public class FileParsingResult
    {
        /// <summary>
        /// The primary parsed content (for example, extracted text).
        /// </summary>
        public string parsedContent { get; set; } = string.Empty;

        /// <summary>
        /// Additional metadata extracted from the file (such as row count, image dimensions, etc.).
        /// </summary>
        public Dictionary<string, object> metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Alternate parsed content for specific parsers (e.g., Excel sheets).
        /// </summary>
        public Dictionary<string, string> parsedContentAlternateDict { get; set; } = new Dictionary<string, string>();
    }
    #endregion

    /// <summary>
    /// Represents the result of chunking a file parsing a file, including the main parsed content and additional metadata
    /// in chunks that can be iterated over and embedded.
    /// </summary>
    #region FileChunkingResult
    public class FileChunkingResult
    {
        /// <summary>
        /// The primary parsed content (for example, extracted text).
        /// </summary>
        public List<EmbeddingChunkRecord> chunkRecords { get; set; } = new List<EmbeddingChunkRecord>();

        /// <summary>
        /// Additional metadata extracted from the file (such as row count, image dimensions, etc.).
        /// </summary>
        public Dictionary<string, object> metadata { get; set; } = new Dictionary<string, object>();
    }
    #endregion

    #region Interface IFileParser
    /// <summary>
    /// Defines a parser that converts raw file data into a structured <see cref="FileParsingResult"/>.
    /// </summary>
    public interface IFileParser
    {
        /// <summary>
        /// Parses the raw data provided by a file loader and returns a <see cref="FileParsingResult"/>.
        /// </summary>
        /// <param name="rawData">The raw data produced by a file loader.</param>
        /// <returns>A <see cref="FileParsingResult"/> containing parsed content and metadata.</returns>
        Task<FileParsingResult> Parse(object rawData);
        /// <summary>
        /// Chunks the data provided by a file loader and returns a <see cref="FileChunkingResult"/>
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction);
        
    }
    #endregion

    #region JSON Parser
    /// <summary>
    /// Implements the JSON file parser
    /// </summary>
    public class JsonFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        /// <summary>
        /// Defines the JSON file parser
        /// </summary>
        /// <param name="kernel"></param>
        public JsonFileParser(Kernel kernel)
        {
            _kernel = kernel;
        }
        /// <summary>
        /// Parsing method for the JOSN file parser
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();
            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }
            try
            {
                if (rawData is JObject jObject)
                {
                    // Return the formatted JSON and list the top-level keys as metadata.
                    result.parsedContent = jObject.ToString();
                    result.metadata["Keys"] = string.Join(", ", jObject.Properties().Select(p => p.Name));
                }
                else
                {
                    result.parsedContent = rawData.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during JSON file parsing: {ex.Message}");
                throw;
            }
            return result;
        }

        /// <summary>
        /// Implements the chunking method for the JSON file parser
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            var result = new FileChunkingResult();
            result.metadata = parsedResult.metadata;

            var jsonContent = parsedResult.parsedContent;
            
            // Check if the content exceeds token limit for cl100k_base model
            // chunkSize parameter is used as the maximum token limit
            if (!TokenCheckUtility.ExceedsTokenLimit(jsonContent, chunkSize, "cl100k_base"))
            {
                // Embed the entire JSON as a single chunk if it fits within token limit
                var chunkRecords = new List<EmbeddingChunkRecord>
                {
                    new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        PageId = 0,
                        ChunkIndex = 0,
                        Modality = "json",
                        Definition = jsonContent
                    }
                };
                result.chunkRecords = chunkRecords;
            }
            else
            {
                // Use TokenCheckUtility to split the content dynamically based on token count
                var chunks = new List<string>();
                
                // Calculate character-based chunk size and overlap for initial splitting
                int chunkSizeInChars = chunkSize * 4; // Approximate character count per token
                int overlap = (int)(chunkSizeInChars * chunkOverlapFraction);
                int jsonLength = jsonContent.Length;
                
                // Initial character-based chunking
                var initialChunks = new List<string>();
                for (int i = 0; i < jsonLength; i += chunkSizeInChars - overlap)
                {
                    int length = Math.Min(chunkSizeInChars, jsonLength - i);
                    var chunk = jsonContent.Substring(i, length);
                    initialChunks.Add(chunk);
                }
                
                // Process each initial chunk and further split if needed based on token count
                foreach (var initialChunk in initialChunks)
                {
                    if (TokenCheckUtility.ExceedsTokenLimit(initialChunk, chunkSize, "cl100k_base"))
                    {
                        // If the chunk exceeds token limit, split it further using TokenCheckUtility
                        var splitChunks = TokenCheckUtility.SplitTextToFitTokenLimit(initialChunk, chunkSize, chunkOverlapFraction, "cl100k_base");
                        chunks.AddRange(splitChunks);
                    }
                    else
                    {
                        // If the chunk fits within token limit, add it as is
                        chunks.Add(initialChunk);
                    }
                }

                var chunkRecords = new List<EmbeddingChunkRecord>();
                for (int i = 0; i < chunks.Count; i++)
                {
                    chunkRecords.Add(new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        PageId = 0,
                        ChunkIndex = i,
                        Modality = "json",
                        Definition = chunks[i]
                    });
                }

                result.chunkRecords = chunkRecords;
            }

            return result;
        }
    }
    #endregion

    #region CSV Parser
    /// <summary>
    /// Implements the CSV file parser
    /// </summary>
    public class CsvFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        /// <summary>
        /// Defines the CSV file parser
        /// </summary>
        /// <param name="kernel"></param>
        public CsvFileParser(Kernel kernel)
        {
            _kernel = kernel;
        }

        /// <summary>
        /// Implements the CSV file parser
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();
            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }
            try
            {
                // Assume rawData is a collection of dynamic objects representing CSV records.
                if (rawData is IEnumerable<dynamic> records)
                {
                    var builder = new StringBuilder();
                    bool isHeaderAdded = false;
                    int count = 0;

                    foreach (var record in records)
                    {
                        var recordDict = (IDictionary<string, object>)record;
                        if (!isHeaderAdded)
                        {
                            var header = string.Join(", ", recordDict.Keys);
                            builder.AppendLine(header);
                            isHeaderAdded = true;
                        }
                        var recordString = string.Join(", ", recordDict.Values);
                        builder.AppendLine(recordString);
                        count++;
                    }
                    result.parsedContent = builder.ToString();
                    result.metadata["RowCountSampled"] = count;
                }
                else
                {
                    result.parsedContent = rawData.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during CSV file parsing: {ex.Message}");
                throw;
            }
            return result;
        }

        /// <summary>
        /// Implements the CSV file chunking
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            // Add a small delay to make this method truly asynchronous
            await Task.Delay(1); // This addresses the async warning
            
            var result = new FileChunkingResult();
            result.metadata = parsedResult.metadata;

            var csvContent = parsedResult.parsedContent;
            var lines = csvContent.Split('\n');
            var header = lines[0];
            var rows = lines.Skip(1).ToArray();
            var columnCount = header.Split(',').Length;

            // Check if entire table can be embedded as a single chunk using token-based check
            if (!TokenCheckUtility.ExceedsTokenLimit(csvContent, chunkSize, "cl100k_base"))
            {
                var chunkRecord = new EmbeddingChunkRecord
                {
                    Key = Guid.NewGuid(),
                    FileName = fileName,
                    PageId = 0,
                    ChunkIndex = 0,
                    Definition = csvContent,
                    Modality = "tabular"
                };
                result.chunkRecords.Add(chunkRecord);
                return result;
            }

            // Check if individual columns can be embedded as separate chunks
            var columns = header.Split(',');
            bool allColumnsEmbeddable = true;
            for (int i = 0; i < columnCount; i++)
            {
                var columnData = new StringBuilder();
                columnData.AppendLine(columns[i]);
                foreach (var row in rows)
                {
                    var cells = row.Split(',');
                    if (i < cells.Length)
                    {
                        columnData.AppendLine(cells[i]);
                    }
                }
                if (!CanEmbed(columnData.ToString(), chunkSize))
                {
                    allColumnsEmbeddable = false;
                    break;
                }
            }

            if (allColumnsEmbeddable)
            {
                for (int i = 0; i < columnCount; i++)
                {
                    var columnData = new StringBuilder();
                    columnData.AppendLine(columns[i]);
                    foreach (var row in rows)
                    {
                        var cells = row.Split(',');
                        if (i < cells.Length)
                        {
                            columnData.AppendLine(cells[i]);
                        }
                    }
                    var chunkRecord = new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        ChunkIndex = i,
                        PageId = 0,
                        Definition = columnData.ToString(),
                        Modality = "tabular"
                    };
                    result.chunkRecords.Add(chunkRecord);
                }
                return result;
            }

            // Chunk by rows if columns can't be embedded as separate chunks
            int rowCounter = 0;
            var chunkRecords = new List<EmbeddingChunkRecord>();
            
            while (rowCounter < rows.Length)
            {
                // Always start with the header for each chunk
                string currentChunk = header + "\n";
                int startRowIndex = rowCounter;
                
                while (rowCounter < rows.Length)
                {
                    // Try adding the next row to the current chunk
                    string potentialChunk = currentChunk + rows[rowCounter] + "\n";
                    
                    // Check if adding this row would exceed the token limit
                    if (TokenCheckUtility.ExceedsTokenLimit(potentialChunk, chunkSize, "cl100k_base"))
                    {
                        // If the chunk already has rows (beyond just the header), stop here
                        if (currentChunk.Length > header.Length + 1)
                            break;
                        
                        // If we can't even fit a single row, we need to split the row itself
                        var rowWithHeader = header + "\n" + rows[rowCounter];
                        var splitChunks = TokenCheckUtility.SplitTextToFitTokenLimit(rowWithHeader, chunkSize, chunkOverlapFraction, "cl100k_base");
                        
                        foreach (var splitChunk in splitChunks)
                        {
                            chunkRecords.Add(new EmbeddingChunkRecord
                            {
                                Key = Guid.NewGuid(),
                                FileName = fileName,
                                ChunkIndex = chunkRecords.Count,
                                PageId = 0,
                                Definition = splitChunk,
                                Modality = "tabular"
                            });
                        }
                        
                        rowCounter++; // Move to the next row after splitting
                        break;
                    }
                    
                    // This row fits, add it to the current chunk
                    currentChunk = potentialChunk;
                    rowCounter++;
                }
                
                // If we have a valid chunk with content (and we didn't just process a split row)
                if (currentChunk.Length > header.Length + 1 && startRowIndex < rowCounter)
                {
                    chunkRecords.Add(new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        ChunkIndex = chunkRecords.Count,
                        PageId = 0,
                        Definition = currentChunk,
                        Modality = "tabular"
                    });
                }
            }
            
            // Add all the chunk records to the result
            result.chunkRecords.AddRange(chunkRecords);
            return result;
        }

        /// <summary>
        /// Private method to determine whether we can embed content in a single chunk
        /// </summary>
        /// <param name="content"></param>
        /// <param name="chunkSize"></param>
        /// <returns></returns>
        private bool CanEmbed(string content, int chunkSize)
        {
            // Use TokenCheckUtility to determine if the content fits within the token limit
            return !TokenCheckUtility.ExceedsTokenLimit(content, chunkSize, "cl100k_base");
        }
    }
    #endregion

    // #region PDF Parser
    // public class PdfFileParser : IFileParser
    // {
    //     public FileParsingResult Parse(object rawData)
    //     {
    //         var result = new FileParsingResult();
    //         if (rawData == null)
    //         {
    //             result.parsedContent = string.Empty;
    //             result.metadata["Error"] = "No data provided.";
    //             return result;
    //         }
    //         // Assume rawData is a string containing the extracted PDF text.
    //         result.parsedContent = rawData.ToString();
    //         result.metadata["CharacterCount"] = result.parsedContent.Length;
    //         return result;
    //     }
    //     public FileChunkingResult Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
    //     {
    //         var result = new FileChunkingResult();
    //         return result;
    //     }
    // }
    // #endregion

    #region DOCX Parser
    /// <summary>
    /// Implements the CSV file parser
    /// </summary>
    public class DocxFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        private readonly bool _isAwsProvider;
        /// <summary>
        /// Creates an instance of the DOCX file parser
        /// </summary>
        /// <param name="kernel"></param>
        public DocxFileParser(Kernel kernel)
        {
            _kernel = kernel;
            var provider = Environment.GetEnvironmentVariable("PROVIDER")?.ToLower() ?? "azure";
            _isAwsProvider = provider == "aws" || provider == "bedrock";
        }

        /// <summary>
        /// Method for parsing DOCX
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();

            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }

            try
            {
                using (var memoryStream = new MemoryStream((byte[])rawData))
                using (var document = WordprocessingDocument.Open(memoryStream, false))
                {
                    var allText = new StringBuilder();
                    var imageCaptions = new List<(int, string)>();
                    int imageIndex = 0;


                    // Extract text from the document.
                    foreach (var paragraph in document.MainDocumentPart.Document.Body.Elements<Paragraph>())
                    {
                        var paragraphText = paragraph.InnerText.Trim();
                        if (!string.IsNullOrEmpty(paragraphText) && !paragraphText.StartsWith("INCLUDEPICTURE"))
                        {
                            allText.AppendLine(paragraphText);
                        }
                    }

                    // Extract images from the document.
                    foreach (var imagePart in document.MainDocumentPart.ImageParts)
                    {
                        using (var stream = imagePart.GetStream())
                        using (var memoryStreamImage = new MemoryStream())
                        {
                            stream.CopyTo(memoryStreamImage);
                            var imageData = memoryStreamImage.ToArray();
                            var caption = await GenerateImageCaption(imageData);
                            imageCaptions.Add((imageIndex++, caption));
                        }
                    }
                    result.parsedContent = allText.ToString();
                    
                    result.metadata["ImageCaptions"] = imageCaptions;
                    result.metadata["CharacterCount"] = result.parsedContent.Length;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during DOCX file parsing: {ex.Message}");
                throw;
            }
            return result;
        }

        /// <summary>
        /// Method for chunking DOCX
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            var result = new FileChunkingResult();
            int globalChunkIndex = 0;

            // Process text chunks.
            if (parsedResult.metadata.TryGetValue("CharacterCount", out var charCountObj) && charCountObj is int charCount)
            {
                var text = parsedResult.parsedContent;
                
                // Use TokenCheckUtility to dynamically split text based on token count
                // Make this operation asynchronous by wrapping it in a Task.Run
                var chunks = await Task.Run(() => TokenCheckUtility.SplitTextToFitTokenLimit(text, chunkSize, chunkOverlapFraction, "cl100k_base"));
                
                // Calculate overlap in tokens for consistency
                int overlapTokens = (int)(chunkSize * chunkOverlapFraction);
                
                // Process each chunk
                foreach (var chunkText in chunks)
                {
                    // Skip empty chunks
                    if (string.IsNullOrWhiteSpace(chunkText))
                        continue;
                    
                    var record = new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        PageId = 0, // DOCX files do not have pages, so we set PageId to 0.
                        ChunkIndex = globalChunkIndex,
                        Definition = chunkText,
                        Modality = "text"
                        // DefinitionEmbedding and EmbeddingServiceId can be set later.
                    };
                    result.chunkRecords.Add(record);
                    globalChunkIndex++;
                }
            }

            // Process image caption chunks.
            if (parsedResult.metadata.TryGetValue("ImageCaptions", out var imagesObj) &&
                imagesObj is List<(int, string)> imageCaptions)
            {
                foreach (var (imageIndex, caption) in imageCaptions)
                {
                    var record = new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        PageId = 0, // DOCX files do not have pages, so we set PageId to 0.
                        ChunkIndex = globalChunkIndex,
                        Definition = caption,
                        Modality = "image-caption"
                    };
                    result.chunkRecords.Add(record);
                    globalChunkIndex++;
                }
            }

            return result;
        }

        /// <summary>
        /// Generates a caption for the provided image data using the registered ImageCaptioningService.
        /// </summary>
        /// <param name="imageData">The image data as a byte array.</param>
        /// <returns>A generated caption for the image.</returns>
        private async Task<string> GenerateImageCaption(byte[] imageData)
        {
            if (_isAwsProvider)
            {
                return await AwsImageCaptioningUtility.GenerateImageCaptionAws(imageData);
            }

            var chatHistory = new ChatHistory("Your job is to create short image descriptions");

            chatHistory.AddUserMessage(
            [
                // https://github.com/microsoft/semantic-kernel/issues/12944
                new TextContent("What's in this image?"),
                new ImageContent(imageData, "image/jpeg"),
            ]);

            // Retrieve the image captioning service (registered as a text completion service).
            var imageCaptionService = _kernel.GetRequiredService<IChatCompletionService>("ImageCaptioningService");
            // Build a prompt instructing the model to generate an image caption.
            var reply = await imageCaptionService.GetChatMessageContentAsync(chatHistory);

            // Call the service asynchronously (synchronously waiting here for simplicity).
            return reply.Content;
        }
    }
    #endregion

    #region PDF Parser
    /// <summary>
    /// A PDF file parser that extracts text and images (with generated captions) from a PDF.
    /// Assumes that rawData is pre‐loaded (preferably as a byte[] of the PDF file) via a file loader.
    /// </summary>
    public class PdfFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        private readonly bool _isAwsProvider;
        /// <summary>
        /// Creates a PDF file parser
        /// </summary>
        /// <param name="kernel"></param>
        public PdfFileParser(Kernel kernel)
        {
            _kernel = kernel;
            var provider = Environment.GetEnvironmentVariable("PROVIDER")?.ToLower() ?? "azure";
            _isAwsProvider = provider == "aws" || provider == "bedrock";
        }

        /// <summary>
        /// Method for parsing PDFs
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();

            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }
            // If rawData is a byte array, assume it's the raw PDF bytes.
            if (rawData is byte[] pdfBytes)
            {
                try
                {
                    using (var ms = new MemoryStream(pdfBytes))
                    using (var pdfReader = new PdfReader(ms))
                    using (var pdfDoc = new PdfDocument(pdfReader))
                    {
                        int pageCount = pdfDoc.GetNumberOfPages();
                        result.metadata["PageCount"] = pageCount;

                        var allText = new StringBuilder();
                        var pageTexts = new List<string>();
                        // Store image captions as tuples of (PageId, Caption)
                        var imageCaptions = new List<(int, string)>();
                        // Loop through each page to extract text and images.
                        for (int i = 1; i <= pageCount; i++)
                        {
                            var page = pdfDoc.GetPage(i);

                            // Extract text from the page.
                            var pageText = PdfTextExtractor.GetTextFromPage(page);
                            pageTexts.Add(pageText);
                            allText.AppendLine(pageText);

                            // Extract images from the page.
                            var images = ExtractImagesFromPage(page);
                            foreach (var imageData in images)
                            {
                                var caption = await GenerateImageCaption(imageData);
                                imageCaptions.Add((i, caption));
                            }
                        }
                        result.parsedContent = allText.ToString();
                        // Store the per-page texts and image captions in the metadata.
                        result.metadata["PageTexts"] = pageTexts;
                        result.metadata["ImageCaptions"] = imageCaptions;
                        result.metadata["CharacterCount"] = result.parsedContent.Length;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception during PDF file parsing: {ex.Message}");
                    result.metadata["Error"] = ex.Message;
                }
            }
            // Fallback: if rawData is already a string, assume it's pre-extracted text.
            else if (rawData is string text)
            {
                result.parsedContent = text;
                result.metadata["CharacterCount"] = text.Length;
                result.metadata["Warning"] = "Raw data provided as text; image extraction not performed.";
            }
            else
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "Unsupported rawData type. Expecting byte[] or string.";
            }
            Console.Write($"{result}");
            return result;
        }

        /// <summary>
        /// Method for chunking PDFs
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            var result = new FileChunkingResult();
            int globalChunkIndex = 0;

            // Process text chunks.
            if (parsedResult.metadata.TryGetValue("PageTexts", out var pagesObj) && pagesObj is List<string> pageTexts)
            {
                for (int pageId = 1; pageId <= pageTexts.Count; pageId++)
                {
                    var text = pageTexts[pageId - 1];
                    
                    // Check if the entire page text exceeds token limit
                    if (PgVectorDynamicRAG.Embeddings.TokenCheckUtility.ExceedsTokenLimit(text, chunkSize))
                    {
                        // Use TokenCheckUtility to split text into chunks that fit within token limit
                        var chunks = PgVectorDynamicRAG.Embeddings.TokenCheckUtility.SplitTextToFitTokenLimit(text, chunkSize, chunkOverlapFraction, "cl100k_base");
                        
                        foreach (var chunkText in chunks)
                        {
                            var record = new EmbeddingChunkRecord
                            {
                                Key = Guid.NewGuid(),
                                FileName = fileName,
                                PageId = pageId,
                                ChunkIndex = globalChunkIndex,
                                Definition = chunkText,
                                Modality = "text"
                                // DefinitionEmbedding and EmbeddingServiceId can be set later.
                            };
                            result.chunkRecords.Add(record);
                            globalChunkIndex++;
                        }
                    }
                    else
                    {
                        // If the text doesn't exceed the token limit, use it as a single chunk
                        var record = new EmbeddingChunkRecord
                        {
                            Key = Guid.NewGuid(),
                            FileName = fileName,
                            PageId = pageId,
                            ChunkIndex = globalChunkIndex,
                            Definition = text,
                            Modality = "text"
                        };
                        result.chunkRecords.Add(record);
                        globalChunkIndex++;
                    }
                }
            }

            // Process image caption chunks.
            if (parsedResult.metadata.TryGetValue("ImageCaptions", out var imagesObj) &&
                imagesObj is List<(int, string)> imageCaptions)
            {
                foreach (var (pageId, caption) in imageCaptions)
                {
                    var record = new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        PageId = pageId,
                        ChunkIndex = globalChunkIndex,
                        Definition = caption,
                        Modality = "image-caption"
                    };
                    result.chunkRecords.Add(record);
                    globalChunkIndex++;
                }
            }
            return result;
        }

        /// <summary>
        /// Extracts image byte arrays from a given PDF page.
        /// </summary>
        /// <param name="page">The PDF page to process.</param>
        /// <returns>A list of byte arrays representing extracted images.</returns>
        private List<byte[]> ExtractImagesFromPage(PdfPage page)
        {
            var listener = new ImageExtractionListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);
            return listener.Images;
        }

        /// <summary>
        /// Generates a caption for the provided image data using the registered ImageCaptioningService.
        /// </summary>
        /// <param name="imageData">The image data as a byte array.</param>
        /// <returns>A generated caption for the image.</returns>
        private async Task<string> GenerateImageCaption(byte[] imageData)
        {
            if (_isAwsProvider)
            {
                return await AwsImageCaptioningUtility.GenerateImageCaptionAws(imageData);
            }

            // Convert the image data to a base64 string (if needed for the prompt).
            // var base64Image = Convert.ToBase64String(imageData);
            var chatHistory = new ChatHistory("Your job is to create short image descriptions");

            chatHistory.AddUserMessage(
            [
                // https://github.com/microsoft/semantic-kernel/issues/12944
                new TextContent("What's in this image?"),
                new ImageContent(imageData, "image/jpeg"),
            ]);

            // Retrieve the image captioning service (registered as a text completion service).
            var imageCaptionService = _kernel.GetRequiredService<IChatCompletionService>("ImageCaptioningService");
            // Build a prompt instructing the model to generate an image caption.
            var reply = await imageCaptionService.GetChatMessageContentAsync(chatHistory);

            // Call the service asynchronously (synchronously waiting here for simplicity).
            return reply.Content;
            // Console.Write($"Caption gen service: {imageCaptionService}");
            // // Build a prompt instructing the model to generate an image caption.
            // var reply = await imageCaptionService.GetChatMessageContentAsync(chatHistory);
            // Console.Write($"Reply: {reply}");

            // // Call the service asynchronously (synchronously waiting here for simplicity).
            // return reply.Content;
        }

        /// <summary>
        /// Custom IEventListener implementation for extracting images using iText7.
        /// </summary>
        private class ImageExtractionListener : IEventListener
        {
            public List<byte[]> Images { get; } = new List<byte[]>();

            public void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_IMAGE && data is ImageRenderInfo renderInfo)
                {
                    try
                    {
                        var image = renderInfo.GetImage();
                        if (image != null)
                        {
                            var bytes = image.GetImageBytes();
                            Images.Add(bytes);
                        }
                    }
                    catch (Exception)
                    {
                        // Optionally log or handle image extraction errors.
                    }
                }
            }

            public ICollection<EventType> GetSupportedEvents()
            {
                return new EventType[] { EventType.RENDER_IMAGE };
            }
        }
    }
    #endregion PDF Parser

    #region TXT Parser
    /// <summary>
    /// Implements a TXT file parser
    /// </summary>
    public class TxtFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        /// <summary>
        /// Creates a txt file parser
        /// </summary>
        /// <param name="kernel"></param>
        public TxtFileParser(Kernel kernel)
        {
            _kernel = kernel;
        }
        /// <summary>
        /// TXT file parsing method
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();
            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }
            result.parsedContent = rawData.ToString();
            result.metadata["CharacterCount"] = result.parsedContent.Length;
            
            // Add token count to metadata
            int tokenCount = TokenCheckUtility.GetTokenCount(result.parsedContent);
            result.metadata["TokenCount"] = tokenCount;
            
            return result;
        }
        /// <summary>
        /// TXT file chunking method using dynamic token-based chunking
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            var result = new FileChunkingResult();
            result.metadata = parsedResult.metadata;
            
            // If the content is empty, return an empty result
            if (string.IsNullOrEmpty(parsedResult.parsedContent))
            {
                return result;
            }
            
            // Check if the entire content fits within the token limit
            if (!TokenCheckUtility.ExceedsTokenLimit(parsedResult.parsedContent, chunkSize))
            {
                // If it fits, create a single chunk
                var singleChunkRecord = new List<EmbeddingChunkRecord>
                {
                    new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        PageId = 0,
                        ChunkIndex = 0,
                        Modality = "text",
                        Definition = parsedResult.parsedContent
                    }
                };
                
                result.chunkRecords = singleChunkRecord;
                return result;
            }
            
            // If the content exceeds the token limit, split it into chunks
            // Calculate the overlap in tokens
            int overlapTokens = (int)(chunkSize * chunkOverlapFraction);
            
            // Use TokenCheckUtility to split the text into chunks that fit within the token limit
            List<string> textChunks = TokenCheckUtility.SplitTextToFitTokenLimit(parsedResult.parsedContent, chunkSize, chunkOverlapFraction, "cl100k_base");
            
            // Create embedding chunk records for each text chunk
            var chunkRecords = new List<EmbeddingChunkRecord>();
            for (int i = 0; i < textChunks.Count; i++)
            {
                chunkRecords.Add(new EmbeddingChunkRecord
                {
                    Key = Guid.NewGuid(),
                    FileName = fileName,
                    PageId = 0,
                    ChunkIndex = i,
                    Modality = "text",
                    Definition = textChunks[i]
                });
            }
            
            result.chunkRecords = chunkRecords;
            return await Task.FromResult(result);
        }
    }
    #endregion

    #region XLSX Parser
    /// <summary>
    /// Implements an XLXS file parser/chunker
    /// </summary>
    public class XlsxFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        /// <summary>
        /// Constructor for the XLSX parser
        /// </summary>
        /// <param name="kernel"></param>
        public XlsxFileParser(Kernel kernel)
        {
            _kernel = kernel;
        }

    /// <summary>
    /// Implements a TXT file parser method
    /// </summary>
    /// <param name="rawData"></param>
    /// <returns></returns>
    public async Task<FileParsingResult> Parse(object rawData)
    {
        // Results to: public Dictionary<string, string> parsedContentAlternateDict { get; set; } = new Dictionary<string, string>();
        var result = new FileParsingResult();
        if (rawData == null)
        {
            result.parsedContent = string.Empty;
            result.metadata["Error"] = "No data provided.";
            return result;
        }
        try
        {
            if (rawData is Dictionary<string, List<Dictionary<string, object>>> sheetsData)
            {
                // var builder = new StringBuilder();
                // bool isHeaderAdded = false;
                // int totalRowCount = 0;
                foreach (var sheet in sheetsData)
                {
                    var sheetBuilder = new StringBuilder();
                    bool isSheetHeaderAdded = false;
                    int sheetRowCount = 0;

                    sheetBuilder.AppendLine($"Worksheet name: {sheet.Key}");
                    foreach (var row in sheet.Value)
                    {
                        var rowDict = (IDictionary<string, object>)row;
                        if (!isSheetHeaderAdded)
                        {
                            var header = string.Join(", ", rowDict.Keys);
                            sheetBuilder.AppendLine(header);
                            // builder.AppendLine(header); // Add header to the main builder as well
                            isSheetHeaderAdded = true;
                            // isHeaderAdded = true;
                        }
                        var rowString = string.Join(", ", rowDict.Values);
                        sheetBuilder.AppendLine(rowString);
                        // builder.AppendLine(rowString); // Add row to the main builder as well
                        sheetRowCount++;
                        // totalRowCount++;
                    }
                    result.parsedContentAlternateDict[sheet.Key] = sheetBuilder.ToString();
                }
                result.parsedContent = string.Empty;
                // result.metadata["RowCountSampled"] = totalRowCount;
            }
            else
            {
                result.parsedContent = "Invalid data format.";
                result.metadata["Error"] = "Expected Dictionary<string, List<Dictionary<string, object>>>.";
            }
        }
        catch (Exception ex)
        {
            result.parsedContent = "Error during parsing.";
            result.metadata["Exception"] = ex.Message;
            Console.WriteLine($"Exception during excel file parsing: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Implements a TXT file chunking method using dynamic token-based chunking
    /// </summary>
    /// <param name="parsedResult"></param>
    /// <param name="fileName"></param>
    /// <param name="chunkSize"></param>
    /// <param name="chunkOverlapFraction"></param>
    /// <returns></returns>
    public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
    {
        var result = new FileChunkingResult();
        result.metadata = parsedResult.metadata;
        int sheet_i = 0;

        foreach (var sheetData in parsedResult.parsedContentAlternateDict)
        {
            var sheetName = sheetData.Key;
            var sheetContent = sheetData.Value;
            var lines = sheetContent.Split('\n');
            var header = string.Join("\n", lines.Take(2));
            var rows = lines.Skip(1).ToArray();
            sheet_i += 1;
        
            // Add token count to metadata for this sheet
            int tokenCount = TokenCheckUtility.GetTokenCount(sheetContent);
            result.metadata[$"Sheet_{sheet_i}_TokenCount"] = tokenCount;
        
            // Check if the entire sheet can be embedded as a single chunk based on token count
            if (!TokenCheckUtility.ExceedsTokenLimit(sheetContent, chunkSize))
            {
                result.chunkRecords.Add(new EmbeddingChunkRecord
                {
                    Key = Guid.NewGuid(),
                    FileName = fileName,
                    ChunkIndex = 0,
                    PageId = sheet_i,
                    Definition = sheetContent,
                    Modality = "tabular"
                });
            }
            else
            {
                // Check if individual columns can be embedded as separate chunks
                bool columnsEmbedded = true;
                var columns = header.Split(',');

                foreach (var column in columns)
                {
                    var columnData = string.Join("\n", rows.Select(row => row.Split(',')[Array.IndexOf(columns, column)]));
                    if (TokenCheckUtility.ExceedsTokenLimit(columnData, chunkSize))
                    {
                        columnsEmbedded = false;
                        break;
                    }
                }

                if (columnsEmbedded)
                {
                    foreach (var column in columns)
                    {
                        var columnData = string.Join("\n", rows.Select(row => row.Split(',')[Array.IndexOf(columns, column)]));
                        result.chunkRecords.Add(new EmbeddingChunkRecord
                        {
                            Key = Guid.NewGuid(),
                            FileName = fileName,
                            ChunkIndex = Array.IndexOf(columns, column),
                            PageId = sheet_i,
                            Definition = columnData,
                            Modality = "tabular"
                        });
                    }
                }
                else
                {
                    // Dynamically chunk by rows based on token count
                    int rowIndex = 0;
                    int chunkIndex = 0;
                    
                    while (rowIndex < rows.Length)
                    {
                        // Start with the header and add rows until we reach the token limit
                        var chunkBuilder = new StringBuilder(header);
                        int rowsAdded = 0;
                        
                        while (rowIndex + rowsAdded < rows.Length)
                        {
                            // Try adding the next row
                            string nextRow = rows[rowIndex + rowsAdded];
                            string potentialChunk = chunkBuilder.ToString() + "\n" + nextRow;
                            
                            // Check if adding this row would exceed the token limit
                            if (rowsAdded > 0 && TokenCheckUtility.ExceedsTokenLimit(potentialChunk, chunkSize))
                            {
                                break;
                            }
                            
                            // Add the row to the chunk
                            chunkBuilder.Append("\n").Append(nextRow);
                            rowsAdded++;
                        }
                        
                        // Create a chunk with the rows we've collected
                        string chunkData = chunkBuilder.ToString();
                        result.chunkRecords.Add(new EmbeddingChunkRecord
                        {
                            Key = Guid.NewGuid(),
                            FileName = fileName,
                            ChunkIndex = chunkIndex,
                            PageId = sheet_i,
                            Definition = chunkData,
                            Modality = "tabular"
                        });
                        
                        // Move to the next set of rows
                        rowIndex += rowsAdded;
                        chunkIndex++;
                    }
                }
            }
        }
        return await Task.FromResult(result);
    }

    /// <summary>
    /// Checks if the content exceeds the token limit
    /// </summary>
    /// <param name="content">The content to check</param>
    /// <param name="tokenLimit">The maximum number of tokens allowed</param>
    /// <returns>True if the content can be embedded, false otherwise</returns>
    private bool CanEmbed(string content, int tokenLimit)
    {
        return !TokenCheckUtility.ExceedsTokenLimit(content, tokenLimit);
    }
    }
    #endregion

    #region XML Parser
    /// <summary>
    /// Implements an XML file parser
    /// </summary>
    public class XmlFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        /// <summary>
        /// Creates Xml file parser
        /// </summary>
        /// <param name="kernel"></param>
        public XmlFileParser(Kernel kernel)
        {
            _kernel = kernel;
        }

        /// <summary>
        /// Implements a TXT file parsing
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();
            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }
            try
            {
                if (rawData is XDocument xDocument)
                {
                    // Return the formatted XML and list the top-level elements as metadata.
                    result.parsedContent = xDocument.ToString();
                    result.metadata["RootElement"] = xDocument.Root.Name.LocalName;
                    result.metadata["TopLevelElements"] = string.Join(", ", xDocument.Root.Elements().Select(e => e.Name.LocalName));
                }
                else
                {
                    result.parsedContent = rawData.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during XML file parsing: {ex.Message}");
                result.metadata["Error"] = ex.Message;
            }
            return result;
        }

        /// <summary>
        /// Implements XML file chunking using dynamic token-based chunking
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            var result = new FileChunkingResult();
            result.metadata = parsedResult.metadata;

            var xmlContent = parsedResult.parsedContent;
            
            // Add token count to metadata
            int tokenCount = TokenCheckUtility.GetTokenCount(xmlContent);
            result.metadata["TokenCount"] = tokenCount;
            
            // Check if the entire XML content fits within the token limit
            if (!TokenCheckUtility.ExceedsTokenLimit(xmlContent, chunkSize))
            {
                // Embed the entire XML as a single chunk
                var chunkRecords = new List<EmbeddingChunkRecord>
                {
                    new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        PageId = 0,
                        ChunkIndex = 0,
                        Modality = "xml",
                        Definition = xmlContent
                    }
                };
                result.chunkRecords = chunkRecords;
            }
            else
            {
                // Use TokenCheckUtility to split the text into chunks that fit within the token limit
                List<string> xmlChunks = TokenCheckUtility.SplitTextToFitTokenLimit(xmlContent, chunkSize, chunkOverlapFraction, "cl100k_base");
                
                // Create embedding chunk records for each XML chunk
                var chunkRecords = new List<EmbeddingChunkRecord>();
                for (int i = 0; i < xmlChunks.Count; i++)
                {
                    chunkRecords.Add(new EmbeddingChunkRecord
                    {
                        Key = Guid.NewGuid(),
                        FileName = fileName,
                        PageId = 0,
                        ChunkIndex = i,
                        Modality = "xml",
                        Definition = xmlChunks[i]
                    });
                }

                result.chunkRecords = chunkRecords;
            }

            return await Task.FromResult(result);
        }
    }
    #endregion

    #region PNG Parser
    /// <summary>
    /// Implements PNG file parsing/chunking
    /// </summary>
    public class PngFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        private readonly bool _isAwsProvider;
        /// <summary>
        /// Constructor for PNG file parser
        /// </summary>
        /// <param name="kernel"></param>
        public PngFileParser(Kernel kernel)
        {
            _kernel = kernel;
            var provider = Environment.GetEnvironmentVariable("PROVIDER")?.ToLower() ?? "azure";
            _isAwsProvider = provider == "aws" || provider == "bedrock";
        }

        /// <summary>
        /// Defines a PNG parser
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();

            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }

            try
            {
                if (rawData is byte[] imageData)
                {
                    var caption = await GenerateImageCaption(imageData);
                    result.parsedContent = caption;
                    result.metadata["ImageCaption"] = caption;
                }
                else
                {
                    result.parsedContent = string.Empty;
                    result.metadata["Error"] = "Unsupported rawData type. Expecting byte array.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during PNG file parsing: {ex.Message}");
                throw;
            }

            return result;
        }

        /// <summary>
        /// Defines a PNG chunker
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            var result = new FileChunkingResult();
            result.metadata = parsedResult.metadata;

            if (parsedResult.metadata.TryGetValue("ImageCaption", out var captionObj) && captionObj is string caption)
            {
                var record = new EmbeddingChunkRecord
                {
                    Key = Guid.NewGuid(),
                    FileName = fileName,
                    PageId = 1,
                    ChunkIndex = 0,
                    Definition = caption,
                    Modality = "image-caption"
                };
                result.chunkRecords.Add(record);
            }

            return result;
        }

        /// <summary>
        /// Defines a caption generator for images
        /// </summary>
        /// <param name="imageData"></param>
        /// <returns></returns>
        private async Task<string> GenerateImageCaption(byte[] imageData)
        {
            if (_isAwsProvider)
            {
                return await AwsImageCaptioningUtility.GenerateImageCaptionAws(imageData);
            }

            var chatHistory = new ChatHistory("Your job is to create short image descriptions");
            chatHistory.AddUserMessage(
            [
                // https://github.com/microsoft/semantic-kernel/issues/12944
                new TextContent(text: "What's in this image?"),
                new ImageContent(data: imageData, mimeType: "image/png")
            ]);

            var imageCaptionService = _kernel.GetRequiredService<IChatCompletionService>("ImageCaptioningService");
            var reply = await imageCaptionService.GetChatMessageContentAsync(chatHistory);

            return reply.Content;
        }
    }
    #endregion

    #region JPEG Parser
    /// <summary>
    /// Implements a JPEG file parser/chunker
    /// </summary>
    public class JpegFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        private readonly bool _isAwsProvider;
        /// <summary>
        /// Constructor for the JPEG file parser
        /// </summary>
        /// <param name="kernel"></param>
        public JpegFileParser(Kernel kernel)
        {
            _kernel = kernel;
            var provider = Environment.GetEnvironmentVariable("PROVIDER")?.ToLower() ?? "azure";
            _isAwsProvider = provider == "aws" || provider == "bedrock";
        }

        /// <summary>
        /// Implements the JPEG parser
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();

            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }

            try
            {
                if (rawData is byte[] imageData)
                {
                    var caption = await GenerateImageCaption(imageData);
                    result.parsedContent = caption;
                    result.metadata["ImageCaption"] = caption;
                }
                else
                {
                    result.parsedContent = string.Empty;
                    result.metadata["Error"] = "Unsupported rawData type. Expecting byte array.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during JPG file parsing: {ex.Message}");
                throw;
            }

            return result;
        }

        /// <summary>
        /// Implements JPEG file chunking
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            var result = new FileChunkingResult();
            result.metadata = parsedResult.metadata;

            if (parsedResult.metadata.TryGetValue("ImageCaption", out var captionObj) && captionObj is string caption)
            {
                var record = new EmbeddingChunkRecord
                {
                    Key = Guid.NewGuid(),
                    FileName = fileName,
                    PageId = 1,
                    ChunkIndex = 0,
                    Definition = caption,
                    Modality = "image-caption"
                };
                result.chunkRecords.Add(record);
            }

            return result;
        }

        /// <summary>
        /// Implements JPEG file chunking
        /// </summary>
        /// <param name="imageData"></param>
        /// <returns></returns>
        private async Task<string> GenerateImageCaption(byte[] imageData)
        {
            if (_isAwsProvider)
            {
                return await AwsImageCaptioningUtility.GenerateImageCaptionAws(imageData);
            }

            var chatHistory = new ChatHistory("Your job is to create short image descriptions.");

            chatHistory.AddUserMessage(
            [
                // https://github.com/microsoft/semantic-kernel/issues/12944
                new TextContent("What's in this image?"),
                new ImageContent(imageData, "image/jpeg"),
            ]);

            var imageCaptionService = _kernel.GetRequiredService<IChatCompletionService>("ImageCaptioningService");
            var reply = await imageCaptionService.GetChatMessageContentAsync(chatHistory);

            return reply.Content;
        }
    }
    #endregion

    #region BMP Parser
    /// <summary>
    /// Implements the PMG image parser and chunker
    /// </summary>
    public class BmpFileParser : IFileParser
    {
        private readonly Kernel _kernel;
        private readonly bool _isAwsProvider;

        /// <summary>
        /// Constructor for the BMP parser/chunker
        /// </summary>
        /// <param name="kernel"></param>
        public BmpFileParser(Kernel kernel)
        {
            _kernel = kernel;
            var provider = Environment.GetEnvironmentVariable("PROVIDER")?.ToLower() ?? "azure";
            _isAwsProvider = provider == "aws" || provider == "bedrock";
        }

        /// <summary>
        /// Defines the parser for BMP file parsing
        /// </summary>
        /// <param name="rawData"></param>
        /// <returns></returns>
        public async Task<FileParsingResult> Parse(object rawData)
        {
            var result = new FileParsingResult();

            if (rawData == null)
            {
                result.parsedContent = string.Empty;
                result.metadata["Error"] = "No data provided.";
                return result;
            }

            try
            {
                if (rawData is byte[] imageData)
                {
                    var caption = await GenerateImageCaption(imageData);
                    result.parsedContent = caption;
                    result.metadata["ImageCaption"] = caption;
                }
                else
                {
                    result.parsedContent = string.Empty;
                    result.metadata["Error"] = "Unsupported rawData type. Expecting byte array.";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during BMP file parsing: {ex.Message}");
                throw;
            }

            return result;
        }

        /// <summary>
        /// Defines the chunker for BMP file chunking
        /// </summary>
        /// <param name="parsedResult"></param>
        /// <param name="fileName"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlapFraction"></param>
        /// <returns></returns>
        public async Task<FileChunkingResult> Chunk(FileParsingResult parsedResult, string fileName, int chunkSize, float chunkOverlapFraction)
        {
            var result = new FileChunkingResult();
            result.metadata = parsedResult.metadata;

            if (parsedResult.metadata.TryGetValue("ImageCaption", out var captionObj) && captionObj is string caption)
            {
                var record = new EmbeddingChunkRecord
                {
                    Key = Guid.NewGuid(),
                    FileName = fileName,
                    PageId = 1,
                    ChunkIndex = 0,
                    Definition = caption,
                    Modality = "image-caption"
                };
                result.chunkRecords.Add(record);
            }

            return result;
        }

        /// <summary>
        /// Defines the image captioner method for BMP file parsing
        /// </summary>
        /// <param name="imageData"></param>
        /// <returns></returns>
        private async Task<string> GenerateImageCaption(byte[] imageData)
        {
            if (_isAwsProvider)
            {
                return await AwsImageCaptioningUtility.GenerateImageCaptionAws(imageData);
            }

            var chatHistory = new ChatHistory("Your job is to create short image descriptions");
            chatHistory.AddUserMessage(
            [
                // https://github.com/microsoft/semantic-kernel/issues/12944
                new TextContent("What's in this image?"),
                new ImageContent(imageData, "image/bmp"),
            ]);

            var imageCaptionService = _kernel.GetRequiredService<IChatCompletionService>("ImageCaptioningService");
            var reply = await imageCaptionService.GetChatMessageContentAsync(chatHistory);

            return reply.Content;
        }
    }
#endregion
}