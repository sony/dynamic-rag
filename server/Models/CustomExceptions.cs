namespace PgVectorDynamicRAG.models
{
    /// <summary>
    /// Exception class for handling errors related to CLIP embeddings.
    /// This exception is thrown when there are issues with generating or processing CLIP embeddings.
    /// It can be used to encapsulate specific error messages and optionally include an inner exception for
    /// more detailed error information.
    /// </summary>
    public class ClipEmbeddingException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipEmbeddingException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="inner">An optional inner exception that provides more details about the error.</param>
        /// <remarks>
        /// This constructor allows you to create an exception with a custom message and an optional inner exception
        /// to provide additional context about the error.
        /// </remarks>
        /// EXPERIMENTAL
        public ClipEmbeddingException(string message, Exception? inner = null)
            : base(message, inner) { }
    }

}