using PgVectorDynamicRAG.Data;

/// <summary>
/// Represents a model with its dimension, glossary model type, modality, and default similarity algorithm.
/// </summary>
namespace PgVectorDynamicRAG.Models
{
    public class ModelDetails
    {
        /// <summary>
        /// Combined structure holding both the model registry and dimensions.
        /// </summary>
        public static readonly Dictionary<string, Dictionary<string, (int Dimension, Type GlossaryModel, string Modality, string DefaultSimilarityAlgo)>> ModelRegistry = new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "ChatCompletion", new Dictionary<string, (int Dimension, Type GlossaryModel, string Modality, string DefaultSimilarityAlgo)>
                {
                    { "gpt-4", (0, null, null, null) }, // Assuming no details for this model
                    { "gpt-4o", (0, null, null, null) }, // Assuming no details for this model
                    { "o1", (0, null, null, null) }, // Assuming no details for this model
                    { "o1-mini", (0, null, null, null)},
                    { "anthropic.claude-opus-4-20250514-v1:0", (0, null, null, null) },
                    { "anthropic.claude-3-5-sonnet-20240620-v1:0", (0, null, null, null) },
                    { "anthropic.claude-3-5-sonnet-20241022-v2:0", (0, null, null, null) }
                }
            },
            {
                "ImageCaptioning", new Dictionary<string, (int Dimension, Type GlossaryModel, string Modality, string DefaultSimilarityAlgo)>
                {
                    { "gpt-4o", (0, null, null, null) },
                    { "anthropic.claude-opus-4-20250514-v1:0", (0, null, null, null) },
                    { "anthropic.claude-3-5-sonnet-20240620-v1:0", (0, null, null, null) },
                    { "anthropic.claude-3-5-sonnet-20241022-v2:0", (0, null, null, null) }
                }
            },
            {
                "TextEmbedding", new Dictionary<string, (int Dimension, Type GlossaryModel, string Modality, string DefaultSimilarityAlgo)>
                {
                    { "text-embedding-ada-002", (1536, typeof(GlossaryModelAda002), "TextEmbedding", "cosine") },
                    { "text-embedding-3-small", (1536, typeof(GlossaryModelAda3sm), "TextEmbedding", "cosine") },
                    { "text-embedding-3-large", (3072, typeof(GlossaryModelAda3lg), "TextEmbedding", "cosine") },
                    { "amazon.titan-embed-text-v2:0", (1024, typeof(GlossaryModelTitanTextEmbeddingV2), "TextEmbedding", "cosine") }
                    // { "openai-clip-image-text-embedd-3", (768, typeof(GlossaryModelOpenaiClip), "ImageEmbedding", "cosine")}
                }
            },
            {
                "ImageEmbedding", new Dictionary<string, (int Dimension, Type GlossaryModel, string Modality, string DefaultSimilarityAlgo)>
                {
                    { "openai-clip-image-text-embedd-3", (768, typeof(GlossaryModelOpenaiClip), "ImageEmbedding", "cosine")}
                }
            }
        };

        /// <summary>
        /// Retrieves the details of models by their category.
        /// </summary>
        /// <param name="modelCategory">The category of models to retrieve details for (e.g. "chatCompletions", "ImageCaptioning", "embeddings").</param>
        /// <returns>A list of dictionaries containing model details, where each dictionary has keys "modelId", "dimension", "GlossaryModel", "modality", and "defaultSimilarityAlgo".</returns>
        public static List<Dictionary<string, object>> GetModelDetailsByType(string modelCategory)
        {
            var result = new List<Dictionary<string, object>>();

            if (ModelRegistry.TryGetValue(modelCategory, out var models))
            {
                foreach (var model in models)
                {
                    var modelDetailDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "ModelId", model.Key },
                        { "Dimension", model.Value.Dimension },
                        { "GlossaryModel", model.Value.GlossaryModel },
                        { "Modality", model.Value.Modality },
                        { "DefaultSimilarityAlgo", model.Value.DefaultSimilarityAlgo }
                    };

                    result.Add(modelDetailDict);
                }
            }

            return result;
        }
    }
}