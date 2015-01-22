using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FileWatchCopier
{
    class Program
    {
        static readonly object ObjLock = new object();
        static void Main(string[] args)
        {

            JToken configuration;
            using (var textReader = File.OpenText(args[0]))
            using (var jsonTextReader = new JsonTextReader(textReader))
            {
                configuration = JToken.ReadFrom(jsonTextReader);
            }

            IEnumerable<dynamic> directoryConfigurations;
            switch (configuration.Type)
            {
                case JTokenType.Array:
                    directoryConfigurations = ((JArray)configuration);
                    break;
                case JTokenType.Object:
                    directoryConfigurations = new[] { (dynamic)configuration };
                    break;
                default:
                    return;
            }

            var fileSystemWatchers = new List<IDisposable>();

            foreach (var directoryConfig in directoryConfigurations)
            {
                string destinationRootDirectoryFullName = directoryConfig.destinationDirectory;
                var filters = ((string)directoryConfig.filters).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                var filtersPattern = filters.Length == 0
                    ? "^.*?$"
                    : string.Join("|",
                        filters.Select(filter => string.Format("(^{0}$)", Regex.Replace(filter, "[^|?*]+|[|?*]", match =>
                        {
                            switch (match.Value)
                            {
                                case "?":
                                    return ".";
                                case "*":
                                    return ".*?";
                                default:
                                    return Regex.Escape(match.Value);
                            }
                        }))));

                var filtersRegex = new Regex(filtersPattern, RegexOptions.IgnoreCase);

                FileSystemEventHandler fileHandler = (sender, e) =>
                {
                    if (!filtersRegex.IsMatch(new FileInfo(e.FullPath).Name))
                        return;

                    lock (ObjLock)
                    {
                        var destinationFileFullName = Path.Combine(destinationRootDirectoryFullName, e.Name);
                        /* if (e.ChangeType == WatcherChangeTypes.Renamed)
                        {
                            var renamedEventArgs = (RenamedEventArgs)e;
                            File.Move(Path.Combine(destinationRootDirectoryFullName, renamedEventArgs.OldName),
                                destinationFileFullName);

                            Console.WriteLine("Renamed {0} to {1}", renamedEventArgs.OldName, e.Name);
                        } */


                        if (e.ChangeType == WatcherChangeTypes.Changed || e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Renamed)
                        {
                            File.Copy(e.FullPath, destinationFileFullName, true);
                            Console.WriteLine("Copied {0}", e.Name);
                        }


                        /* if (e.ChangeType == WatcherChangeTypes.Deleted)
                        {
                            File.Delete(destinationFileFullName);

                            Console.WriteLine("Deleted {0}", e.Name);
                        } */
                    }
                };


                var watcher = new FileSystemWatcher((string)directoryConfig.watchDirectory)
                {
                    IncludeSubdirectories = (bool)directoryConfig.includeSubDirectories,
                    NotifyFilter =  NotifyFilters.FileName | NotifyFilters.LastWrite
                };

                watcher.Changed += fileHandler;
                watcher.Deleted += fileHandler;
                watcher.Created += fileHandler;
                watcher.Renamed += (sender, e) =>
                {
                    fileHandler(sender, e);
                };
                watcher.EnableRaisingEvents = true;
                fileSystemWatchers.Add(watcher);


            }
            Console.Read();

            fileSystemWatchers.ForEach(d => d.Dispose());
        }
    }
}
