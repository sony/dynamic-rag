using PgVectorDynamicRAG.Factories; // Adjust namespace as necessary

/// <summary>
/// Factory class for creating instances of <see cref="IFileLoader"/> based on the specified <see cref="FileType"/>.
/// </summary>
public static class FileLoaderFactory
{
    /// <summary>
    /// Creates an instance of <see cref="IFileLoader"/> based on the provided <paramref name="fileType"/>.
    /// </summary>
    /// <param name="fileType">The type of file for which a loader is required.</param>
    /// <returns>An instance of <see cref="IFileLoader"/> that can handle the specified file type.</returns>
    /// <exception cref="NotSupportedException">Thrown when the specified <paramref name="fileType"/> is not supported.</exception>
    public static IFileLoader GetLoader(FileType fileType)
    {
        return fileType switch
        {
            FileType.Json  => new JsonFileLoader(),
            FileType.Xml   => new XmlFileLoader(),
            FileType.Csv   => new CsvFileLoader(),
            FileType.Docx  => new DocxFileLoader(),
            FileType.Pdf   => new PdfFileLoader(),
            FileType.Txt   => new TxtFileLoader(),
            FileType.Xlsx  => new XlsxFileLoader(),
            FileType.Png   => new PngFileLoader(),
            FileType.Jpeg  => new JpegFileLoader(),
            FileType.Bmp   => new BmpFileLoader(),
            _ => throw new NotSupportedException($"File type {fileType} is not supported.")
        };
    }
}
