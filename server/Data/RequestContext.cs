using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace PgVectorDynamicRAG.Data
{
    /// <summary>
    /// Request object for creating sample embeddings.
    /// </summary>
    public class CreateSampleEmbeddingsRequest
    {
        /// <summary>
        /// Gets or sets the dimension of the embeddings.
        /// </summary>
        [DefaultValue(1536)]
        public int dimension { get; set; }
        /// <summary>
        /// Gets or sets the number of embeddings to create.
        /// </summary>
        [DefaultValue(10)]
        public int nEmbeddings { get; set; }
    }

    /// <summary>
    /// Request object for creating a sample ANN.
    /// </summary>
    public class CreateSampleANNRequest
    {
        /// <summary>
        /// Gets or sets the dimension of the embeddings.
        /// </summary>
        [DefaultValue(1536)]
        public int dimension { get; set; }
        /// <summary>
        /// Gets or sets the number of neighbors to search for.
        /// </summary>
        [DefaultValue(10)]
        public int nNeighbors { get; set; }
    }

    public class EmbedNewFileRequest
    {
        /// 
        /// Gets or sets the local file path to embed.
        /// 
        [DefaultValue("./Tests/test_story_dog.txt")]
        public string? filePath { get; set; } // complete relative or absolute path to a local file to embed

        /// 
        /// Gets or sets the uploaded file to embed.
        /// 
        [DefaultValue(null)]
        public IFormFile? file { get; set; } // For direct file uploads

        /// 
        /// Gets or sets the name of the collection to embed the file into.
        /// 
        [DefaultValue("my_fixed_collection")]
        [Required]
        public string collectionName { get; set; } = string.Empty;

        /// 
        /// Gets or sets the chunk size for the file embedding.
        /// 
        [DefaultValue(4000)] // words
        [Range(100, 40000)]
        public int chunkSize { get; set; } = 4000;

        /// 
        /// Gets or sets the chunk overlap fraction for the file embedding.
        /// 
        [DefaultValue(0.1f)]
        public float chunkOverlapFraction { get; set; } = 0.1f;

        /// 
        /// Gets or sets whether to save a copy of the file to blob storage.
        /// 
        [DefaultValue(true)]
        public bool saveToBlobStorage { get; set; } = true;
    }

    public class EmbedNewTextRequest
    {
        /// 
        /// Gets or sets the text content to embed.
        /// 
        [DefaultValue("<enter your text here>")]
        public string? content { get; set; }

        /// 
        /// Gets or sets the name of the collection to embed the file into.
        /// 
        [DefaultValue("my_fixed_collection")]
        [Required]
        public string collectionName { get; set; } = string.Empty;

        /// 
        /// Gets or sets the chunk size for the file embedding.
        /// 
        [DefaultValue(4000)] // words
        [Range(100, 40000)]
        public int chunkSize { get; set; } = 4000;

        /// 
        /// Gets or sets the chunk overlap fraction for the file embedding.
        /// 
        [DefaultValue(0.1f)]
        public float chunkOverlapFraction { get; set; } = 0.1f;

        /// 
        /// Gets or sets whether to save a copy of the file to blob storage.
        /// 
        [DefaultValue(true)]
        public bool saveToBlobStorage { get; set; } = true;
    }

    /// <summary>
    /// Request object to remove a file from a named collection.
    /// </summary>
    public class RemoveFileFromCollectionRequest
    {
        /// <summary>
        /// Gets or sets the file path to remove from the collection.
        /// </summary>
        [DefaultValue("./Tests/test_story_dog.txt")]
        public string pathInContainer { get; set; }
        /// <summary>
        /// Gets or sets the name of the collection to remove the file from.
        /// </summary>
        [DefaultValue("my_fixed_collection")]
        public required string collectionName { get; set; }
        [DefaultValue(true)]
        public bool isBlobFile { get; set; }
    }

    /// <summary>
    /// Request object for searching a named collection.
    /// </summary>
    public class TextSearchFixedCollectionRequest
    {
        /// <summary>
        /// Gets or sets the search query.
        /// </summary>
        [DefaultValue("<enter you search query here>")]
        public required string query { get; set; }
        /// <summary>
        /// Gets or sets the name of the collection to search.
        /// </summary>
        [DefaultValue("my_fixed_collection")]
        public required string collectionName { get; set; }
        /// <summary>
        /// Gets or sets the number of search results to return.
        /// </summary>
        [DefaultValue(10)] // records
        [Range(1, int.MaxValue, ErrorMessage = "nResults must be greater than 0.")]
        public int nResults { get; set; }
        /// <summary>
        /// Gets or sets whether time-window filtering is enabled.
        /// </summary>
        [DefaultValue(false)]
        public bool timeWindowEnabled { get; set; }
        /// <summary>
        /// Gets or sets the time window size in days for filtering results.
        /// </summary>
        [DefaultValue(30)]
        [Range(1, 3650, ErrorMessage = "window_days must be between 1 and 3650.")]
        public int windowDays { get; set; }
        /// <summary>
        /// Gets or sets the alpha parameter which balances embedding search (1=complete embedding search) with semantic search (0=complete semantic search).
        /// </summary>
        [DefaultValue(0.75f)]
        [Range(0, 1, ErrorMessage = "alpha must be between 0 and 1.")]
        public double alpha { get; set; }
        /// <summary>
        /// Gets or ses when the temporalDecay is applied
        /// </summary>
        [DefaultValue(false)]
        public bool temporalDecayEnabled { get; set; }
        /// <summary>
        /// Gets or sets the exponential decay factor for the temporal decay (by day)   Decay impact factor for exponential decay. 0.005 - low, 0.01 - medium, 0.05
        /// </summary>
        [DefaultValue(0.01f)]
        [Range(0.001, 0.1, ErrorMessage = "decay rate must be between 0.001 and 0.1")]
        public double decayImpact { get; set; }
    }

    /// <summary>
    /// Request object for searching a named collection.
    /// </summary>
    public class ImageSearchFixedCollectionRequest
    {
        /// <summary>
        /// Gets or sets the uploaded image for search.
        /// </summary>
        [DefaultValue(null)]
        public required IFormFile image { get; set; } // For direct file uploads


        /// <summary>
        /// Gets or sets the name of the collection to search.
        /// </summary>
        [DefaultValue("my_fixed_collection")]
        public required string collectionName { get; set; }

        /// <summary>
        /// Gets or sets the number of search results to return.
        /// </summary>
        [DefaultValue(10)] // records
        [Range(1, int.MaxValue, ErrorMessage = "nResults must be greater than 0.")]
        public int nResults { get; set; }

        /// <summary>
        /// Gets or sets whether time-window filtering is enabled.
        /// </summary>
        [DefaultValue(false)]
        public bool timeWindowEnabled { get; set; }

        /// <summary>
        /// Gets or sets the time window size in days for filtering results.
        /// </summary>
        [DefaultValue(30)]
        [Range(1, 3650, ErrorMessage = "window_days must be between 1 and 3650.")]
        public int windowDays { get; set; }

        /// <summary>
        /// Gets or sets whether temporal decay is applied.
        /// </summary>
        [DefaultValue(false)]
        public bool temporalDecayEnabled { get; set; }

        /// <summary>
        /// Gets or sets the exponential decay factor for temporal decay (per day).
        /// </summary>
        [DefaultValue(0.01f)]
        [Range(0.001, 0.1, ErrorMessage = "decay rate must be between 0.001 and 0.1")]
        public double decayImpact { get; set; }
    }


    /// <summary>
    /// Request object for chatting with a named collection by using
    /// hybrid search results as context to the chat model.
    /// </summary>
    public class ChatWithCollectionRequest
    {
        /// <summary>
        /// Gets or sets the search query.
        /// </summary>
        [DefaultValue("<enter you search query here>")]
        public required string query { get; set; }
        /// <summary>
        /// Gets or sets the name of the collection to search.
        /// </summary>
        [DefaultValue("my_fixed_collection")]    
        public required string collectionName { get; set; }
        /// <summary>
        /// Gets or sets the number of search results to return.
        /// </summary>
        [DefaultValue(10)] // records
        public int nResults { get; set; }
        /// <summary>
        /// Gets or sets the name of the chat model to use.
        /// </summary>
        [DefaultValue("gpt-4o")]
        public string chatModelName { get; set; }
        /// <summary>
        /// Gets or sets the alpha parameter which balances embedding search (1=complete embedding search) with semantic search (0=complete semantic search).
        /// </summary>
        [DefaultValue(0.75f)]
        [Range(0, 1, ErrorMessage = "alpha must be between 0 and 1.")]
        public double alpha { get; set; }
        /// <summary>
        /// Gets or sets whether time-window filtering is enabled.
        /// </summary>
        [DefaultValue(false)]
        public bool timeWindowEnabled { get; set; }
        /// <summary>
        /// Gets or sets the time window size in days for filtering results.
        /// </summary>
        [DefaultValue(30)]
        [Range(1, 3650, ErrorMessage = "window_days must be between 1 and 3650.")]
        public int windowDays { get; set; }
        /// <summary>
        /// Gets or ses when the temporalDecay is applied
        /// </summary>
        [DefaultValue(false)]
        public bool temporalDecayEnabled { get; set; }
        /// <summary>
        /// Gets or sets the exponential decay factor for the temporal decay (by day)   Decay impact factor for exponential decay. 0.005 - low, 0.01 - medium, 0.05
        /// </summary>
        [DefaultValue(0.01f)]
        [Range(0.001, 0.1, ErrorMessage = "decay rate must be between 0.001 and 0.1")]
        public double decayImpact { get; set; }
    }

    /// <summary>
    /// Request to create a new named collection.
    /// </summary>
    public class CreateCollectionRequest
    {
        /// <summary>
        /// TextEmbedding or ImageEmbedding
        /// </summary>
        [DefaultValue("TextEmbedding")]
        public string embeddingType { get; set; }

        /// <summary>
        /// Gets or sets the name of the default embedding model to use for this collection.
        /// This is the model that will be used to generate embeddings for new records in this collection.
        /// If not specified, the default embedding model will be used.
        /// </summary>
        [DefaultValue("text-embedding-3-small")]
        public required string defaultEmbeddingModelName { get; set; }

        /// <summary>
        /// Gets or sets the name of the new collection.
        /// </summary>
        [DefaultValue("my_fixed_collection")]
        public required string collectionName { get; set; }
        /// <summary>
        /// Gets or sets the dimension of the embeddings.
        /// </summary>
        [DefaultValue(true)]
        public bool initHNSW { get; set; } // initialize HNSW index on the embeddings column
        /// <summary>
        /// Gets or sets the dimension of the embeddings.
        /// </summary>
        [DefaultValue(true)]
        public bool initBTree { get; set; } // experimental: initialize a BTree index on the EmbeddingServiceId column
        /// <summary>
        /// Gets or sets a description of the collection
        /// </summary>
        [DefaultValue("")]
        public string? description { get; set; }
    }

    /// <summary>
    /// Request to update the index of a named collection.
    /// </summary>
    public class UpdateIndexRequest
    {
        /// <summary>
        /// Gets or sets the name of the new collection.
        /// </summary>
        [DefaultValue("my_fixed_collection")]
        public required string collectionName { get; set ;}
    }

    /// <summary>
    /// Request to delete a named collection.
    /// </summary>
    public class DeleteCollectionRequest
    {
        /// <summary>
        /// Gets or sets the name of the collection to delete.
        /// </summary>
        [DefaultValue("my_fixed_collection")]
        public required string collectionName { get; set;}
        /// <summary>
        /// Boolean flag to denote whether to try to remove these files from blob.
        /// </summary>
        [DefaultValue(true)]
        public bool deleteBlobs { get; set; }
    }

    /// <summary>
    /// Request to delete a named collection.
    /// </summary>
    public class DeleteCollectionBlobsRequest
    {
        /// <summary>
        /// Gets or sets the name of the collection to delete.
        /// </summary>
        [DefaultValue("my_fixed_collection")]
        public required string collectionName { get; set; }
    }

    /// <summary>
    /// Request to access a single blob from a collection.
    /// </summary>
    public class SingleBlobRequest
    {
        /// <summary>
        /// Path to the blob in the container.
        /// This is the relative path to the blob in the container.
        /// It is expected to be in the format: /<collection_name>/path/to/blob/in/container/blob.txt
        /// </summary>
        [DefaultValue("/<collection_name>/path/to/blob/in/container/blob.txt")]
        public required string pathInContainer { get; set ;}
    }

    // /// <summary>
    // /// Request to delete a named collection.
    // /// </summary>
    // public class UploadSingleBlob
    // {
    //     /// <summary>
    //     /// Gets or sets the name of the collection to delete.
    //     /// </summary>
    //     [DefaultValue("my_fixed_collection")]
    //     public string collectionName { get; set ;}

    //     /// <summary>
    //     /// Gets or sets the relative path to the blob in the container
    //     /// </summary>
    //     [DefaultValue("/relative/path/to/blob/in/container/blob.txt")]
    //     public string blobRelativePath { get; set ;}

    //     /// <summary> 
    //     /// Gets or sets the file data
    //     /// </summary>
    //     public IFormFile blob { get; set; }
    // }

}