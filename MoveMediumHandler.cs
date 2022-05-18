using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Scherf.CorrectieTool
{
    internal class MoveMediumHandler
    {
        FileInfo _archivePackage;

        private ObservableCollection<TrackUpdateItem> _currentTrackList;

        public MoveMediumHandler(FileInfo package)
        {
            _archivePackage = package;
            _currentTrackList = new ObservableCollection<TrackUpdateItem>();
            ToMedium = String.Empty;
            FromMedium = String.Empty;
        }

        public string ExportOutput()
        {
            string tmpExtractionFilename = Path.GetTempFileName();
            File.WriteAllLines(tmpExtractionFilename, this._currentTrackList.Select(item => item.ToString()));

            return tmpExtractionFilename;
        }

        private String ToMedium { get; set; }
        private String FromMedium { get; set; }

        public void Move(String fromMedium = "SCHERF", String toMedium = "_training/Wai/SCHERF")
        {
            FromMedium = fromMedium;
            ToMedium = toMedium;

            ReadExtractUpdatePackage();
            UpdateManifest();
        }

        private void ReadExtractUpdatePackage()
        {
            if (!_archivePackage.Exists)
                throw new FileNotFoundException(String.Format("DSOP pakket niet gevonden in {0}!", _archivePackage.FullName));

            using (var zip = ZipFile.Open(_archivePackage.FullName, ZipArchiveMode.Update))
            {
                string path = String.Concat(@"profile/client/folders/", FromMedium);
                var xmlBlocksCollection = zip.Entries.Where(item => item.FullName.StartsWith(path) && item.FullName.EndsWith(".xml")).ToList();
                xmlBlocksCollection.ForEach(entry =>
                {
                    string tmpExtractionFilename = Path.GetTempFileName();
                    ZipFileExtensions.ExtractToFile(entry, tmpExtractionFilename, true);

                    var results = UpdateReferences(tmpExtractionFilename);
                   
                    string newEntryReference = entry.FullName.Replace(path, String.Concat(@"profile/client/folders/", ToMedium));

                    entry.Delete();
                    ZipFileExtensions.CreateEntryFromFile(zip, tmpExtractionFilename, newEntryReference);

                    results.ForEach(item =>
                    {
                        item.CurrentXmlFullName = entry.FullName;
                        item.NewXmlFullName = newEntryReference;
                        _currentTrackList.Add(item);
                    });

                    try
                    {
                        if (File.Exists(tmpExtractionFilename))
                            File.Delete(tmpExtractionFilename);
                    }
                    catch { }
                });

                //remove loose folders
                zip.Entries.Where(item => item.FullName.StartsWith(path) && !item.FullName.EndsWith(".xml")).ToList().ForEach(item => item.Delete());
            }
        }

        private List<TrackUpdateItem> UpdateReferences(String tmpXmlFile)
        {
            List<TrackUpdateItem> results = new List<TrackUpdateItem>();
           XmlDocument xmlDoc = new XmlDocument();
            var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsmgr.AddNamespace("ds", "http://namespaces.docsys.nl/content");
            xmlDoc.Load(tmpXmlFile);

            XmlNodeList? nodeList = xmlDoc.SelectNodes(String.Format("//*/ds:include[starts-with(@src, '$/{0}')]", FromMedium), nsmgr);
            if (nodeList == null)
                return results;

            foreach (XmlNode node in nodeList)
            {
                string currentValue = node.Attributes["src"].Value;

                if (String.IsNullOrEmpty(currentValue))
                    continue;

                string newValue = currentValue.Replace(String.Format("$/{0}", FromMedium), String.Format("$/{0}", ToMedium));
                node.Attributes["src"].Value = newValue;

                results.Add(new TrackUpdateItem(tmpXmlFile, currentValue, newValue));
            }

            using (XmlTextWriter wr = new XmlTextWriter(tmpXmlFile, Encoding.UTF8))
            {
                wr.Formatting = Formatting.None; // here's the trick !
                xmlDoc.Save(wr);
            }

            return results;
        }

        private void UpdateManifest()
        {
            using (var zip = ZipFile.Open(_archivePackage.FullName, ZipArchiveMode.Update))
            {
                var manifest = zip.Entries.FirstOrDefault(item => item.FullName == "manifest.xml");

                if (manifest == null)
                    return;

                string tmpExtractionFilename = Path.GetTempFileName();
                ZipFileExtensions.ExtractToFile(manifest, tmpExtractionFilename, true);

                UpdateXmlManifest(tmpExtractionFilename);

                manifest.Delete();
                ZipFileExtensions.CreateEntryFromFile(zip, tmpExtractionFilename, manifest.FullName);

                try
                {
                    if (File.Exists(tmpExtractionFilename))
                        File.Delete(tmpExtractionFilename);
                }
                catch { }
            }
        }

        private void UpdateXmlManifest(string filename)
        {
            XmlDocument xmlDoc = new XmlDocument();
            var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsmgr.AddNamespace("a", "http://schemas.datacontract.org/2004/07/DsoPackages.Manifest");

            xmlDoc.Load(filename);
            // /a:Manifest/a:StorageChanges/a:StorageChange/a:Path/a:Value[starts-with(., 'profile/client/folders/tekstblokken/SCHERF/')]
            XmlNodeList? nodeList = xmlDoc.SelectNodes(String.Format("/a:Manifest/a:StorageChanges/a:StorageChange/a:Path/a:Value[starts-with(., 'profile/client/folders/{0}')]", FromMedium), nsmgr);

            if (nodeList == null)
                return;

            foreach (XmlNode node in nodeList)
            {
                string path = node.InnerText;
                TrackUpdateItem? item = this._currentTrackList.FirstOrDefault(item => item.CurrentXmlFullName == path);

                if (item == null)
                {
                    //empty folder

                }
                else
                {
                    //blocks
                    node.InnerText = item.NewXmlFullName;
                }
            }

            using (XmlTextWriter wr = new XmlTextWriter(filename, Encoding.UTF8))
            {
                wr.Formatting = Formatting.None; // here's the trick !
                xmlDoc.Save(wr);
            }
        }

        internal class TrackUpdateItem
        {
            public TrackUpdateItem(String currentXml, String originalSource, String newSource)
            {
                CurrentXmlFullName = currentXml;
                OriginalSourceValue = originalSource;
                NewSourceValue = newSource;
                NewXmlFullName = String.Empty;
            }

            public String CurrentXmlFullName { get; set; }

            public String NewXmlFullName { get; set; }

            public String OriginalSourceValue { get; set; }

            public String NewSourceValue { get; set; }

            public override string ToString()
            {
                return String.Format("Current path: {0} | Original source: {1} | New source: {2} >> New path:{3}", CurrentXmlFullName, OriginalSourceValue, NewSourceValue, NewXmlFullName);
            }
        }
    }
}
