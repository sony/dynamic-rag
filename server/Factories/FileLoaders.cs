using System.Globalization;
using System.Xml.Linq;
using Newtonsoft.Json;          // NuGet: Install-Package Newtonsoft.Json
using CsvHelper;                // NuGet: Install-Package CsvHelper
using ClosedXML.Excel;         // NuGet: Install-Package ClosedXML
using SkiaSharp;

namespace PgVectorDynamicRAG.Factories
{

    #region Interface IFileLoader
    /// <summary>
    /// Defines a loader that takes a file path, loads and parses the file,
    /// and returns an object representing the file’s data.
    /// </summary>
    public interface IFileLoader
    {
        /// <summary>
        /// Loads the file from the given path and parses its content.
        /// </summary>
        /// <param name="filePath">The full path of the file to load.</param>
        /// <returns>An object representing the file’s data.</returns>
        object LoadFile(string filePath);
    }
    #endregion

    #region JSON Loader
    /// <summary>
    /// Implements the JSON file loader.
    /// </summary>
    public class JsonFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for JSON files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public object LoadFile(string filePath)
        {
            // Read the file content as text and deserialize JSON.
            string fileContent = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject(fileContent);
        }
    }
    #endregion

    #region CSV Loader
    /// <summary>
    /// Implements the CSV file loader.
    /// </summary>
    public class CsvFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for CSV files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public object LoadFile(string filePath)
        {
            // Read CSV content from the file and parse it.
            string fileContent = File.ReadAllText(filePath);
            using (var reader = new StringReader(fileContent))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var records = csv.GetRecords<dynamic>().ToList();
                return records;
            }
        }
    }
    #endregion

    #region DOCX Loader
    /// <summary>
    /// Implements the DOCS file loader.
    /// </summary>[]
    public class DocxFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for DOCX files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public object LoadFile(string filePath)
        {
            // Read the file into a byte array for further processing.
            return File.ReadAllBytes(filePath);
        }
    }
    #endregion

    #region PDF Loader
    /// <summary>
    /// Implements the PDF file loader.
    /// </summary>
    public class PdfFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for PDF files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public object LoadFile(string filePath)
        {
            // Read all bytes of the file to enable both text and image extraction.
            return File.ReadAllBytes(filePath);
        }
    }
    #endregion

    #region TXT Loader
    /// <summary>
    /// Implements the TXT file loader.
    /// </summary>
    public class TxtFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for TXT files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public object LoadFile(string filePath)
        {
            // Read and return the text file content.
            return File.ReadAllText(filePath);
        }
    }
    #endregion

    #region XLSX Loader
    /// <summary>
    /// Implements the XLSX file loader.
    /// </summary>
    public class XlsxFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for XLSX files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public object LoadFile(string filePath)
        {
            using (var workbook = new XLWorkbook(filePath))
            {
                var sheetsData = new Dictionary<string, List<Dictionary<string, object>>>();

                foreach (var worksheet in workbook.Worksheets)
                {
                    var sheetData = new List<Dictionary<string, object>>();
                    var headers = worksheet.Row(1).Cells().Select(c => c.Value.ToString()).ToList();

                    foreach (var row in worksheet.RowsUsed().Skip(1))
                    {
                        var rowData = new Dictionary<string, object>();
                        int columnIndex = 0;
                        foreach (var cell in row.CellsUsed())
                        {
                            rowData[headers[columnIndex]] = cell.Value;
                            columnIndex++;
                        }
                        sheetData.Add(rowData);
                    }
                    sheetsData[worksheet.Name] = sheetData;
                }
                return sheetsData;
            }
        }
    }
    #endregion

    #region XML Loader
    /// <summary>
    /// Implements the XML file loader.
    /// </summary>
    public class XmlFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for XML files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public object LoadFile(string filePath)
        {
            // Read the file content as text and parse it as XML.
            string fileContent = File.ReadAllText(filePath);
            return XDocument.Parse(fileContent);
        }
    }
    #endregion

    #region PNG Loader
    /// <summary>
    /// Implements the PNG file loader.
    /// </summary>
    public class PngFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for PNG files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public object LoadFile(string filePath)
        {
            try
            {
                // Load the image from the file path using SkiaSharp
                Console.WriteLine($"Starting to load PNG file: {filePath}");
                using (var inputStream = File.OpenRead(filePath))
                {
                    using (var skBitmap = SKBitmap.Decode(inputStream))
                    {
                        if (skBitmap == null)
                        {
                            throw new Exception("Failed to decode PNG file.");
                        }

                        // Convert the image to a byte array
                        using (var ms = new MemoryStream())
                        {
                            using (var skImage = SKImage.FromBitmap(skBitmap))
                            {
                                using (var skData = skImage.Encode(SKEncodedImageFormat.Png, 100))
                                {
                                    skData.SaveTo(ms);
                                }
                            }
                            Console.WriteLine($"Loaded PNG file: {filePath}");
                            return ms.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load PNG file.", ex);
            }
        }
    }
    #endregion

    #region JPEG Loader
    /// <summary>
    /// Implements the JPEG file loader.
    /// </summary>
    public class JpegFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for JPEG files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public object LoadFile(string filePath)
        {
            try
            {
                // Load the image from the file path using SkiaSharp
                Console.WriteLine($"Starting to load JPEG file: {filePath}");
                using (var inputStream = File.OpenRead(filePath))
                {
                    using (var skBitmap = SKBitmap.Decode(inputStream))
                    {
                        if (skBitmap == null)
                        {
                            throw new Exception("Failed to decode JPEG file.");
                        }

                        // Convert the image to a byte array
                        using (var ms = new MemoryStream())
                        {
                            using (var skImage = SKImage.FromBitmap(skBitmap))
                            {
                                using (var skData = skImage.Encode(SKEncodedImageFormat.Png, 100))
                                {
                                    skData.SaveTo(ms);
                                }
                            }
                            Console.WriteLine($"Loaded JPEG file: {filePath}");
                            return ms.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load JPEG file.", ex);
            }
        }
    }
    #endregion

    #region BMP Loader
    /// <summary>
    /// Implements the BMP file loader.
    /// </summary>
    public class BmpFileLoader : IFileLoader
    {
        /// <summary>
        /// Implements the LoadFile method for BMP files.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public object LoadFile(string filePath)
        {
            try
            {
                // Load the image from the file path using SkiaSharp
                Console.WriteLine($"Starting to load BMP file: {filePath}");
                using (var inputStream = File.OpenRead(filePath))
                {
                    using (var skBitmap = SKBitmap.Decode(inputStream))
                    {
                        if (skBitmap == null)
                        {
                            throw new Exception("Failed to decode BMP file.");
                        }

                        // Convert the image to a byte array
                        using (var ms = new MemoryStream())
                        {
                            using (var skImage = SKImage.FromBitmap(skBitmap))
                            {
                                using (var skData = skImage.Encode(SKEncodedImageFormat.Png, 100))
                                {
                                    skData.SaveTo(ms);
                                }
                            }
                            Console.WriteLine($"Loaded BMP file: {filePath}");
                            return ms.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to load BMP file.", ex);
            }
        }
    }
    #endregion
}