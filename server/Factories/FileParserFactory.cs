using Microsoft.SemanticKernel;
using PgVectorDynamicRAG.Factories; // Adjust namespace as necessary

/// <summary>
/// Factory class for creating instances of <see cref="IFileParser"/> based on the specified <see cref="FileType"/>.
/// </summary>
public static class FileParserFactory
{
    /// <summary>
    /// Creates an instance of an <see cref="IFileParser"/> implementation based on the provided <paramref name="fileType"/>.
    /// </summary>
    /// <param name="fileType">The type of the file to be parsed.</param>
    /// <param name="kernel">The <see cref="Kernel"/> instance to be used by the parser.</param>
    /// <returns>An instance of an <see cref="IFileParser"/> implementation for the specified <paramref name="fileType"/>.</returns>
    /// <exception cref="NotSupportedException">Thrown when the specified <paramref name="fileType"/> is not supported.</exception>
    public static IFileParser GetParser(FileType fileType, Kernel kernel)
    {
        return fileType switch
        {
            FileType.Json  => new JsonFileParser(kernel),
            FileType.Xml   => new XmlFileParser(kernel),
            FileType.Csv   => new CsvFileParser(kernel),
            FileType.Docx  => new DocxFileParser(kernel),
            FileType.Pdf   => new PdfFileParser(kernel),
            FileType.Txt   => new TxtFileParser(kernel),
            FileType.Xlsx  => new XlsxFileParser(kernel),
            FileType.Png   => new PngFileParser(kernel),
            FileType.Jpeg  => new JpegFileParser(kernel),
            FileType.Bmp   => new BmpFileParser(kernel),
            _ => throw new NotSupportedException($"File type {fileType} is not supported.")
        };
    }
}
