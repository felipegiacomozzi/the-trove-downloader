using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Web;
using static System.String;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TheTroveDownloader
{
    public class TheTroveDownloader : IHostedService
    {
        private static readonly List<string> IgnoredNames = new List<string>();
        private static readonly List<string> OnlyIncludedNames = new List<string>();
        private static readonly List<string> IgnoredTypes = new List<string>();
        private static string _basePath = Empty;
        private static int _parallel = 5;
        private static string _theTroveUrl = "https://thetrove.is/Books/";
        private static string _lastDownloadError = Empty;
        private static ILogger<TheTroveDownloader> _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private static bool isCancelled = false;

        public TheTroveDownloader(ILogger<TheTroveDownloader> logger, IHostApplicationLifetime appLifetime)
        {
            _logger = logger;
            _appLifetime = appLifetime;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStopping.Register(OnStopping);

            IgnoredNames.Add("Parent directory/");
            IgnoredNames.Add("?one");
            IgnoredNames.Add(".DS_Store");
            ReadOptions();

            try
            {
                LoadPage(_theTroveUrl, _basePath);
                _logger.LogInformation("Finished.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
                _logger.LogError("\n\r \n\r-----------------------------------------------------------------------------------");
                _logger.LogError("For help in solving this problem, please send the log file generated in this folder (the-trove-downloader.log)");
                _logger.LogError(@"to the Issues page on GitHub (https://github.com/felipegiacomozzi/the-trove-downloader/issues)");
                GenerateLogError(ex);
            }

            _logger.LogInformation("\n\r \n\rPress any key to close this window");
            Console.ReadKey();
            return Task.CompletedTask;
        }

        private static void OnStopping() => isCancelled = true;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static void ReadOptions()
        {
            Console.WriteLine("Enter the path to save files:");
            _basePath = Console.ReadLine()?.Replace('\\', '/').Replace(":\\", "://");

            if (IsNullOrWhiteSpace(_basePath))
            {
                throw new ArgumentNullException(_basePath, "Invalid path");
            }

            Console.WriteLine("(Optional) Enter the URL to download from:");
            string newUrl = Console.ReadLine();
            if (!IsNullOrWhiteSpace(newUrl)) _theTroveUrl = newUrl;

            Console.WriteLine("Choose a download mode:");
            Console.WriteLine("1. Download All (Default)");
            Console.WriteLine("2. Enter ignored folders");
            Console.WriteLine("3. Download specific folder");
            int.TryParse(Console.ReadLine(), out int optionSelected);

            switch (optionSelected)
            {
                case 2:
                    Console.WriteLine("(Optional) Inform the ignored directories (separated by comma):");
                    string ignoredNames = Console.ReadLine();
                    if (!IsNullOrWhiteSpace(ignoredNames))
                        IgnoredNames.AddRange(ignoredNames.Split(',').Select(s => s.Trim()));
                    break;
                case 3:
                    Console.WriteLine("(Optional) Inform the only directories to download (separated by comma):");
                    string onlyIncludedNames = Console.ReadLine();

                    if (!IsNullOrWhiteSpace(onlyIncludedNames))
                        OnlyIncludedNames.AddRange(onlyIncludedNames.Split(',').Select(s => s.Trim()));
                    break;
            }

            Console.WriteLine("(Optional) Inform the ignored file extensions (separated by comma):");
            string ignoredTypes = Console.ReadLine();

            if (!IsNullOrWhiteSpace(ignoredTypes))
                IgnoredTypes.AddRange(ignoredTypes.Split(',').Select(s => s.Trim()));

            Console.WriteLine("(Optional) Max concurrent downloads (Default 5):");
            string maxParallelStr = Console.ReadLine();

            if (int.TryParse(maxParallelStr, out int maxParallel) && maxParallel > 0)
                _parallel = maxParallel;
        }

        #region Navigation
        private static void LoadPage(string baseUrl, string basePath)
        {
            _logger.LogInformation($"Loading Page {baseUrl}");

            if (isCancelled)
                return;

            using HttpClient client = new HttpClient();
            var response = client.GetAsync(baseUrl).Result;
            var pageContents = response.Content.ReadAsStringAsync().Result;

            HtmlDocument pageDocument = new HtmlDocument();
            pageDocument.LoadHtml(pageContents);

            var items = pageDocument.DocumentNode.SelectSingleNode("//*[@id='list']").LastChild.ChildNodes.Where(x => x.InnerLength > 1 && x.Name != "#text").ToList();

            if (items != null && items.Any())
                HandlePageItems(baseUrl, basePath, items);
        }

        private static void HandlePageItems(string baseUrl, string basePath, List<HtmlNode> items)
        {
            var files = new Dictionary<string, string>();
            foreach (HtmlNode item in items)
            {
                if (isCancelled)
                    return;

                ListedItem listedItem = GetListedItem(item);

                _logger.LogInformation($"Checking Item {listedItem.Name}");

                if (!IsValidItem(listedItem, baseUrl))
                    continue;

                if (listedItem.IsFile)
                {
                    if (!IgnoredTypes.Any(a => listedItem.Name.Contains(a)))
                        files.Add(baseUrl + listedItem.Link,
                            $"{HandlePathName(basePath)}\\{HandleFileName(listedItem.Name)}");
                }
                else
                {
                    LoadPage($"{baseUrl}/{listedItem.Link}", Path.Combine(basePath, listedItem.Name));
                }
            }

            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = _parallel },
                file => {
                    (string key, string value) = file;
                    Task.WaitAll(DownloadFile(key, value));
                });
        }

        private static string HandleFileName(string fileName)
        {
            byte[] file = Encoding.Default.GetBytes(fileName);
            return Encoding.UTF8.GetString(file).Replace('?', ' ').Trim();
        }

        private static string HandlePathName(string pathName)
        {
            byte[] file = Encoding.Default.GetBytes(pathName);
            var fullPath = Path.GetFullPath(Encoding.UTF8.GetString(file).Replace('?', ' '));
            var pathRes = "";

            if (System.Runtime.InteropServices.RuntimeInformation
                                               .IsOSPlatform(OSPlatform.Windows))
                foreach (var path in fullPath.Split('\\'))
                    pathRes = Path.Combine(pathRes, path.Trim());
            else
                foreach (var path in fullPath.Split('/'))
                    pathRes = Path.Combine(pathRes, path.Trim());

            return pathRes;
        }

        private static ListedItem GetListedItem(HtmlNode item)
        {
            var linkElement = item.ChildNodes.FirstOrDefault(x => x.GetClasses().Contains("link"))?.FirstChild;
            var size = item.ChildNodes.FirstOrDefault(x => x.GetClasses().Contains("size"))?.InnerText;
            var date = item.ChildNodes.FirstOrDefault(x => x.GetClasses().Contains("date"))?.InnerText;

            if (linkElement == null || size == null || IsNullOrWhiteSpace(size))
            {
                throw new InvalidLinkItemException($"Error reading item: {item.InnerHtml}");
            }

            var listedItem = new ListedItem
            {
                Name = linkElement.InnerText,
                IsFile = size != "-",
                FileSize = size,
                PublishedDate = DateTime.TryParse(date, out var parsedDate) ? parsedDate : (DateTime?)null,
                Link = linkElement.Attributes[0].Value
            };

            return listedItem;
        }

        private static bool IsValidItem(ListedItem item, string baseUrl)
        {
            return IgnoredNames.All(x => x != item.Name) &&
                   !(OnlyIncludedNames.Any() && OnlyIncludedNames.All(x => !string.Equals(x, item.Name, StringComparison.CurrentCultureIgnoreCase))
                                             && !OnlyIncludedNames.Any(x => HttpUtility.UrlDecode(baseUrl).ToLower().Contains(x.ToLower())))
                                             && !IsNullOrWhiteSpace(item.Link);
        }
        #endregion

        #region Download File
        public static async Task DownloadFile(string url, string path)
        {
            var downloading = true;
            var exceptionCount = 0;

            while (downloading)
            {
                try
                {
                    _logger.LogInformation($"Downloading File {path}");
                    CheckIfDirectoryExists(Path.GetDirectoryName(path));

                    if (isCancelled)
                        return;

                    if (FileExists(path))
                    {
                        _logger.LogInformation($"File {path} already exists. Skipping download.");
                    }
                    else
                    {
                        using var wc = new HttpClient();
                        var response = await wc.GetAsync(url, GetHttpCompletionOption());
                        using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                        {
                            Task.WaitAll(response.Content.CopyToAsync(fs));
                        }
                    }

                    downloading = false;
                }
                catch (Exception ex)
                {
                    if (exceptionCount > 10)
                        throw;

                    if (isCancelled)
                        throw;

                    exceptionCount++;
                    Thread.Sleep(5000);
                    _logger.LogInformation($"Error Downloading Fila {path}. Trying again.");
                    _lastDownloadError = $"Error downloading file {path}. Trycount: {exceptionCount}.\n\rException:{GetExceptionLog(ex)}";
                }
            }
        }

        private static bool FileExists(string path)
        {
            var file = new FileInfo(path);

            return file.Exists && file.Length > 0;
        }

        private static void CheckIfDirectoryExists(string path)
        {
            if (Directory.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(path);
        }

        private static HttpCompletionOption GetHttpCompletionOption()
        {
            return HttpCompletionOption.ResponseHeadersRead;
        }
        #endregion

        #region Error Logging
        private static void GenerateLogError(Exception ex)
        {
            _logger.LogError($"OS Description: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            _logger.LogError($"OS Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
            _logger.LogError($"TheTroveDownloader Version: {Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion}");

            _logger.LogError($"IgnoredNames: {Join(", ", IgnoredNames)}");
            _logger.LogError($"OnlyIncludedNames: {Join(", ", OnlyIncludedNames)}");
            _logger.LogError($"IgnoredTypes: {Join(", ", IgnoredTypes)}");
            _logger.LogError($"_basePath: {_basePath}");
            _logger.LogError($"_parallel: {_parallel}");
            _logger.LogError($"_theTroveUrl: {_theTroveUrl}");

            _logger.LogError($"---------------------------------- LAST DOWNLOAD ERROR --------------------------------------");

            _logger.LogError(_lastDownloadError);

            _logger.LogError($"---------------------------------- MAIN EXCEPTION -------------------------------------------");

            _logger.LogError(GetExceptionLog(ex));

        }

        private static string GetExceptionLog(Exception ex)
        {
            StringBuilder sb = new StringBuilder();

            while (ex != null)
            {
                sb.AppendLine(ex.GetType().FullName);
                sb.AppendLine("Message : " + ex.Message);
                sb.AppendLine("StackTrace : " + ex.StackTrace);

                ex = ex.InnerException;
            }

            return sb.ToString();
        }
        #endregion


    }
    public class ListedItem
    {
        public string Name { get; set; }
        public string Link { get; set; }
        public string AbsoluteLink { get; set; }
        public bool IsFile { get; set; }
        public string FileSize { get; set; }
        public DateTime? PublishedDate { get; set; }

    }

    [Serializable()]
    public class InvalidLinkItemException : Exception
    {
        public InvalidLinkItemException() : base() { }
        public InvalidLinkItemException(string message) : base(message) { }
        public InvalidLinkItemException(string message, System.Exception inner) : base(message, inner) { }

        protected InvalidLinkItemException(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    };
}

