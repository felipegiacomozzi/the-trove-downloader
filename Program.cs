using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace TheDroveDownloader
{
    static class Program
    {
        private static readonly List<string> IgnoredNames = new List<string>();
        private static readonly List<string> OnlyIncludedNames = new List<string>();
        private static readonly List<string> IgnoredTypes = new List<string>();
        private static string BasePath = string.Empty;
        private static int parallel = 5;
        private static readonly string theTroveUrl = "https://thetrove.net/Books";

        static void Main()
        {
            IgnoredNames.Add("Parent Directory");
            IgnoredNames.Add("?one");            
            IgnoredTypes.Add(".DS_Store");
            ReadOptions();
                        
            try
            { 
                LoadPage(theTroveUrl, BasePath);
                Console.WriteLine("Finished.");
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static void ReadOptions()
        {
            Console.Write("Enter the path to save files:");
            BasePath = Console.ReadLine();

            if(string.IsNullOrWhiteSpace(BasePath))
            {
                throw new ArgumentNullException(BasePath, "Invalid path");
            }

            Console.WriteLine("Choose a download mode:");
            Console.WriteLine("1. Download All (Default)");
            Console.WriteLine("2. Enter ignored folders");
            Console.WriteLine("3. Download specific folder");
            int.TryParse(Console.ReadLine(), out int optionSelected);

            switch(optionSelected)
            {
                case 2:
                    Console.Write("(Optional) Inform the ignored directories (separated by comma):");
                    var ignoredNames = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(ignoredNames))
                        IgnoredNames.AddRange(ignoredNames.Split(',').Select(s => s.Trim()));
                    break;
                case 3:
                    Console.Write("(Optional) Inform the only directories to download (separated by comma):");
                    var onlyIncludedNames = Console.ReadLine();

                    if (!string.IsNullOrWhiteSpace(onlyIncludedNames))
                        OnlyIncludedNames.AddRange(onlyIncludedNames.Split(',').Select(s => s.Trim()));
                    break;
                default:
                    break;
            }

            Console.Write("(Optional) Inform the ignored file extensions (separated by comma):");
            var ignoredTypes = Console.ReadLine();
                       
            if (!string.IsNullOrWhiteSpace(ignoredTypes))
                IgnoredTypes.AddRange(ignoredTypes.Split(',').Select(s => s.Trim()));

            Console.Write("(Optional) Max concurrent downloads (Default 5):");
            var maxParallelStr = Console.ReadLine();

            if (int.TryParse(maxParallelStr, out int maxParallel) && maxParallel > 0)
                parallel = maxParallel;
        }

        public static void LoadPage(string baseUrl, string basePath)
        {
            Console.WriteLine($"Loading Page {baseUrl}");

            using HttpClient client = new HttpClient();
            var response = client.GetAsync(baseUrl).Result;
            var pageContents = response.Content.ReadAsStringAsync().Result;

            HtmlDocument pageDocument = new HtmlDocument();
            pageDocument.LoadHtml(pageContents);

            var items = pageDocument.DocumentNode.SelectNodes("(//td[contains(@class,'litem_name')])");

            if (items != null && items.Any())
                HandlePageItems(baseUrl, basePath, items);

        }

        private static void HandlePageItems(string baseUrl, string basePath, HtmlNodeCollection items)
        {
            Dictionary<string, string> files = new Dictionary<string, string>();
            foreach (var item in items)
            {
                ListedItem listedItem = GetListedItem(item);

                Console.WriteLine($"Checking Item {listedItem.Name}");

                if (!IsValidItem(listedItem, baseUrl))
                    continue;
                
                if (listedItem.IsFile)
                {
                    if (!IgnoredTypes.Any(a => listedItem.Name.Contains(a)))
                        files.Add(baseUrl + listedItem.Link[1..item.FirstChild.Attributes[1].Value.Length], $"{basePath}\\{HandleFileName(listedItem.Name)}");
                }
                else
                {
                    LoadPage($"{baseUrl}/{listedItem.Link}", Path.Combine(basePath, listedItem.Name));
                }
            }

            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = parallel }, file => {
                DownloadFile(file.Key, file.Value);
            });
        }

        private static ListedItem GetListedItem(HtmlNode item)
        {
            var listedItem =  new ListedItem
            {
                Name = item.InnerText.Trim(),
                IsFile = item.ParentNode.Attributes[0].Value != "litem dir"
            };
            var firstChild = item.FirstChild;

            if (firstChild.Attributes.Count == 2 && !string.IsNullOrWhiteSpace(item.FirstChild.Attributes[1].Value))
                listedItem.Link = item.FirstChild.Attributes[1].Value;

            return listedItem;
        }

        private static bool IsValidItem(ListedItem item, string baseUrl)
        {
            return !IgnoredNames.Any(x => x == item.Name) &&
                !(OnlyIncludedNames.Any() &&
                    !OnlyIncludedNames.Any(x => x.ToLower() == item.Name.ToLower())
                    && !OnlyIncludedNames.Any(x => HttpUtility.UrlDecode(baseUrl).ToLower().Contains(x.ToLower()))) &&
                !string.IsNullOrWhiteSpace(item.Link);
        }

        public static void DownloadFile(string url, string path)
        {
            bool downloading = true;
            int exceptionCount = 0;

            while(downloading)
            {
                try
                {
                    Console.WriteLine($"Downloading Fila {path}");
                    CheckIfDirectoryExists(Path.GetDirectoryName(path));

                    if (FileExists(path))
                    {
                        Console.WriteLine($"File {path} already exists. Skipping download.");
                    }
                    else
                    {
                        using var wc = new System.Net.WebClient();
                        wc.DownloadFile(url, path);
                    }
                        
                    downloading = false;
                }
                catch
                {
                    if (exceptionCount > 10)
                        throw;

                    exceptionCount++;
                    Thread.Sleep(5000);
                    Console.WriteLine($"Error Downloading Fila {path}. Trying again.");
                }
            }
        }

        private static bool FileExists(string path)
        {
            var file = new System.IO.FileInfo(path);

            if (file.Exists && file.Length > 0)
                return true;

            return false;
        }

        public static void CheckIfDirectoryExists(string path)
        {
            if (Directory.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(path);
        }

        public static string HandleFileName(string fileName)
        {
            var file = Encoding.Default.GetBytes(fileName);
            return Encoding.UTF8.GetString(file).Replace('?', ' ');
        }
    }
    
    public class ListedItem
    {
        public string Name { get; set; }
        public string Link { get; set; }
        public bool IsFile { get; set; } 
    }
}
