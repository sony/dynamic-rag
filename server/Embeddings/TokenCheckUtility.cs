using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SharpToken;

namespace PgVectorDynamicRAG.Embeddings
{
    /// <summary>
    /// Utility class for checking token counts in text to ensure they don't exceed model context limits.
    /// </summary>
    public static class TokenCheckUtility
    {
        private static readonly Dictionary<string, GptEncoding> _encoders = new Dictionary<string, GptEncoding>
        {
            ["cl100k_base"] = GptEncoding.GetEncoding("cl100k_base"), // For text-embedding-ada-002 and newer models
            ["p50k_base"] = GptEncoding.GetEncoding("p50k_base")      // For older models
        };

        private static readonly int _chunkRetries;

        static TokenCheckUtility()
        {
            // Get the CHUNK_RETRIES environment variable or use a default value
            string? chunkRetriesStr = Environment.GetEnvironmentVariable("MAX_RESURSIVE_SPLIT_DEPTH");
            if (string.IsNullOrEmpty(chunkRetriesStr) || !int.TryParse(chunkRetriesStr, out _chunkRetries))
            {
                _chunkRetries = 1000; // Default value if not set or invalid
            }
        }

        /// <summary>
        /// Gets the number of tokens in the given text for a specific model.
        /// </summary>
        /// <param name="text">The text to count tokens for.</param>
        /// <param name="modelName">The name of the model to use for token counting.</param>
        /// <param name="logger">Optional logger for warnings.</param>
        /// <returns>The number of tokens in the text.</returns>
        public static int GetTokenCount(string text, string modelName = "cl100k_base", ILogger? logger = null)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            // Default to cl100k_base if the model encoding is not found
            if (!_encoders.TryGetValue(modelName, out var encoding))
            {
                logger?.LogWarning("Encoding for model {ModelName} not found, using cl100k_base instead", modelName);
                encoding = _encoders["cl100k_base"];
            }

            return encoding.Encode(text).Count;
        }

        /// <summary>
        /// Checks if the text exceeds the token limit for the specified model.
        /// </summary>
        /// <param name="text">The text to check.</param>
        /// <param name="maxTokens">The maximum number of tokens allowed.</param>
        /// <param name="modelName">The name of the model to use for token counting.</param>
        /// <param name="logger">Optional logger for warnings.</param>
        /// <returns>True if the text exceeds the token limit, false otherwise.</returns>
        public static bool ExceedsTokenLimit(string text, int maxTokens, string modelName = "cl100k_base", ILogger? logger = null)
        {
            int tokenCount = GetTokenCount(text, modelName, logger);
            return tokenCount > maxTokens;
        }

        /// <summary>
        /// Splits text into chunks that don't exceed the token limit with optional overlap between chunks.
        /// </summary>
        /// <param name="text">The text to split.</param>
        /// <param name="maxTokens">The maximum number of tokens per chunk.</param>
        /// <param name="chunkOverlapFraction">The fraction of maxTokens to use for overlap between chunks.</param>
        /// <param name="modelName">The name of the model to use for token counting.</param>
        /// <param name="logger">Optional logger for warnings.</param>
        /// <param name="retryLevel">The current retry level (for recursive calls).</param>
        /// <returns>A list of text chunks that don't exceed the token limit with proper overlap.</returns>
        public static List<string> SplitTextToFitTokenLimit(string text, int maxTokens, float chunkOverlapFraction, string modelName = "cl100k_base", ILogger? logger = null, int retryLevel = 0)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            // If we've exceeded the retry limit, throw an exception
            if (retryLevel > _chunkRetries)
            {
                logger?.LogError("Exceeded maximum retry level ({RetryLevel}) for text chunking", _chunkRetries);
                throw new InvalidOperationException("Maximum document split depth reached, this document may be too large. Support team may need to increase MAX_RECURSIVE_SPLIT_DEPTH to accommodate your data.");
            }

            // For the recursive splitting process, we'll use a helper method that tracks chunk boundaries
            var chunkBoundaries = SplitTextToChunkBoundaries(text, maxTokens, modelName, logger, retryLevel);
            
            // Now apply overlaps to the final chunks
            return CreateOverlappingChunks(text, chunkBoundaries, maxTokens, chunkOverlapFraction, modelName, logger);
        }

        /// <summary>
        /// Helper method that recursively splits text and returns the boundaries of chunks.
        /// </summary>
        /// <param name="text">The text to split.</param>
        /// <param name="maxTokens">The maximum number of tokens per chunk.</param>
        /// <param name="modelName">The name of the model to use for token counting.</param>
        /// <param name="logger">Optional logger for warnings.</param>
        /// <param name="retryLevel">The current retry level (for recursive calls).</param>
        /// <returns>A list of (start, end) indices representing chunk boundaries.</returns>
        private static List<(int Start, int End)> SplitTextToChunkBoundaries(string text, int maxTokens, string modelName, ILogger? logger, int retryLevel)
        {
            // Check if the text fits within the token limit
            if (!ExceedsTokenLimit(text, maxTokens, modelName, logger))
            {
                return new List<(int, int)> { (0, text.Length) };
            }

            // Split the text in half and recursively process each half
            int midPoint = text.Length / 2;
            
            // Try to find a natural break point (sentence or paragraph)
            int breakPoint = FindNaturalBreakPoint(text, midPoint);
            
            // Recursively process each half
            var firstHalfBoundaries = SplitTextToChunkBoundaries(
                text.Substring(0, breakPoint), 
                maxTokens, 
                modelName, 
                logger, 
                retryLevel + 1
            );
            
            var secondHalfBoundaries = SplitTextToChunkBoundaries(
                text.Substring(breakPoint).TrimStart(), 
                maxTokens, 
                modelName, 
                logger, 
                retryLevel + 1
            );

            // Adjust the indices of the second half to account for the offset
            int secondHalfOffset = breakPoint;
            for (int i = 0; i < secondHalfBoundaries.Count; i++)
            {
                var (start, end) = secondHalfBoundaries[i];
                secondHalfBoundaries[i] = (start + secondHalfOffset, end + secondHalfOffset);
            }

            // Combine the boundaries
            var result = new List<(int, int)>();
            result.AddRange(firstHalfBoundaries);
            result.AddRange(secondHalfBoundaries);
            
            return result;
        }

        /// <summary>
        /// Creates overlapping chunks from the original text based on chunk boundaries.
        /// </summary>
        /// <param name="text">The original text.</param>
        /// <param name="chunkBoundaries">The list of chunk boundaries.</param>
        /// <param name="maxTokens">The maximum number of tokens per chunk.</param>
        /// <param name="chunkOverlapFraction">The fraction of maxTokens to use for overlap.</param>
        /// <param name="modelName">The name of the model to use for token counting.</param>
        /// <param name="logger">Optional logger for warnings.</param>
        /// <returns>A list of text chunks with proper overlap.</returns>
        private static List<string> CreateOverlappingChunks(string text, List<(int Start, int End)> chunkBoundaries, int maxTokens, float chunkOverlapFraction, string modelName, ILogger? logger)
        {
            if (chunkBoundaries.Count == 0)
                return new List<string>();

            if (chunkBoundaries.Count == 1)
                return new List<string> { text.Substring(chunkBoundaries[0].Start, chunkBoundaries[0].End - chunkBoundaries[0].Start) };

            var result = new List<string>();
            int overlapTokens = (int)(maxTokens * chunkOverlapFraction);

            for (int i = 0; i < chunkBoundaries.Count; i++)
            {
                var (start, end) = chunkBoundaries[i];
                string chunk = text.Substring(start, end - start);

                // For all chunks except the first one, try to add overlap at the beginning
                if (i > 0)
                {
                    int prevEnd = chunkBoundaries[i - 1].End;
                    int overlapStart = Math.Max(start - 200, prevEnd - 200); // Start with a reasonable character estimate
                    
                    while (overlapStart > prevEnd)
                    {
                        overlapStart = prevEnd;
                    }

                    // Find a natural break point for the overlap
                    if (overlapStart < start)
                    {
                        string overlapText = text.Substring(overlapStart, start - overlapStart);
                        
                        // Check if adding this overlap would exceed the token limit
                        string potentialChunk = overlapText + chunk;
                        if (!ExceedsTokenLimit(potentialChunk, maxTokens, modelName, logger))
                        {
                            chunk = potentialChunk;
                            start = overlapStart;
                        }
                        else
                        {
                            // Try to find a smaller overlap that fits
                            int naturalBreak = FindNaturalBreakPoint(overlapText, overlapText.Length / 2);
                            if (naturalBreak > 0)
                            {
                                string smallerOverlap = overlapText.Substring(naturalBreak);
                                potentialChunk = smallerOverlap + chunk;
                                if (!ExceedsTokenLimit(potentialChunk, maxTokens, modelName, logger))
                                {
                                    chunk = potentialChunk;
                                    start = overlapStart + naturalBreak;
                                }
                            }
                        }
                    }
                }

                // For all chunks except the last one, try to add overlap at the end
                if (i < chunkBoundaries.Count - 1)
                {
                    int nextStart = chunkBoundaries[i + 1].Start;
                    int overlapEnd = Math.Min(end + 200, nextStart + 200); // Start with a reasonable character estimate
                    
                    while (overlapEnd < nextStart)
                    {
                        overlapEnd = nextStart;
                    }

                    // Find a natural break point for the overlap
                    if (overlapEnd > end)
                    {
                        string overlapText = text.Substring(end, overlapEnd - end);
                        
                        // Check if adding this overlap would exceed the token limit
                        string potentialChunk = chunk + overlapText;
                        if (!ExceedsTokenLimit(potentialChunk, maxTokens, modelName, logger))
                        {
                            chunk = potentialChunk;
                            end = overlapEnd;
                        }
                        else
                        {
                            // Try to find a smaller overlap that fits
                            int naturalBreak = FindNaturalBreakPoint(overlapText, overlapText.Length / 2);
                            if (naturalBreak > 0 && naturalBreak < overlapText.Length)
                            {
                                string smallerOverlap = overlapText.Substring(0, naturalBreak);
                                potentialChunk = chunk + smallerOverlap;
                                if (!ExceedsTokenLimit(potentialChunk, maxTokens, modelName, logger))
                                {
                                    chunk = potentialChunk;
                                    end = end + naturalBreak;
                                }
                            }
                        }
                    }
                }

                result.Add(chunk);
            }

            return result;
        }

        /// <summary>
        /// Finds a natural break point in the text near the specified position.
        /// </summary>
        /// <param name="text">The text to find a break point in.</param>
        /// <param name="position">The approximate position to find a break point near.</param>
        /// <returns>The position of the natural break point.</returns>
        private static int FindNaturalBreakPoint(string text, int position)
        {
            // Look for paragraph breaks first
            int paragraphBreak = text.LastIndexOf("\n\n", position);
            if (paragraphBreak != -1 && paragraphBreak > position - 200)
                return paragraphBreak + 2;

            // Look for line breaks
            int lineBreak = text.LastIndexOf('\n', position);
            if (lineBreak != -1 && lineBreak > position - 100)
                return lineBreak + 1;

            // Look for sentence breaks
            foreach (char sentenceEnd in new[] { '.', '!', '?' })
            {
                int sentenceBreak = text.LastIndexOf(sentenceEnd, position);
                if (sentenceBreak != -1 && sentenceBreak > position - 50)
                    return sentenceBreak + 1;
            }

            // Fall back to word boundaries
            int spaceBreak = text.LastIndexOf(' ', position);
            if (spaceBreak != -1)
                return spaceBreak + 1;

            // If no natural break is found, just use the midpoint
            return position;
        }
    }
}
