using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace Scherf.CorrectieTool
{
    public class CorrectNamingConventionHandler
    {
        FileInfo _archivePackage;

        private const string ALGEMEEN_TEKSTBLOKKEN_SCHERF = @"profile/client/folders/tekstblokken/SCHERF";
        private const string MODEL_BRIEVEN_TEKSTBLOKKEN_SCHERF = @"profile/client/folders/SCHERF";
        private const string REFERENTIE_SOURCE = @"$/tekstblokken/SCHERF/";

        private List<NameConventionItem> _currentChangedItemList;
        private List<TrackUpdateItem> _currentTrackList;

        public CorrectNamingConventionHandler(FileInfo package)
        {
            _archivePackage = package;
            _currentChangedItemList = new List<NameConventionItem>();
            _currentTrackList = new List<TrackUpdateItem>();
        }

        public void Update()
        {
            ReadExtractUpdatePackage();
            UpdateManifest();
        }

        public String ExportOutput()
        {
            string tmpExtractionFilename = Path.GetTempFileName();
            File.WriteAllLines(tmpExtractionFilename, this._currentTrackList.Select(item => item.ToString()));

            return tmpExtractionFilename;
        }

        private void ReadExtractUpdatePackage()
        {
            if (!_archivePackage.Exists)
                throw new FileNotFoundException(String.Format("DSOP pakket niet gevonden in {0}!", _archivePackage.FullName));

            //get list of scherf algemene tekstblokken
            using (var zip = ZipFile.OpenRead(_archivePackage.FullName))
            {
                var algTekstScherfCollectie = zip.Entries.Where(item => item.FullName.StartsWith(ALGEMEEN_TEKSTBLOKKEN_SCHERF)).Select(item => new NameConventionItem(item)).ToList();
                _currentChangedItemList.Clear();
                _currentChangedItemList.AddRange(algTekstScherfCollectie);
                _currentChangedItemList.ForEach(item => item.ExtractMe());
            }
            //update the file names with correct convention
            using (var zip = ZipFile.Open(_archivePackage.FullName, ZipArchiveMode.Update))
            {
                _currentChangedItemList.ForEach(item => item.UpdateMe(zip));
            }
            //cleanup temp files
            _currentChangedItemList.ForEach(item => item.CleanUp());
            //update brieven modellen

            using (var zip = ZipFile.Open(_archivePackage.FullName, ZipArchiveMode.Update))
            {
                var brievenCollectie = zip.Entries.Where(item => item.FullName.StartsWith(MODEL_BRIEVEN_TEKSTBLOKKEN_SCHERF) && item.Name.EndsWith(".xml")).ToList();
                brievenCollectie.ForEach(entry =>
                {
                    string tmpExtractionFilename = Path.GetTempFileName();
                    ZipFileExtensions.ExtractToFile(entry, tmpExtractionFilename, true);
                    var miniTrackList = UpdateReferenceNameConvention(tmpExtractionFilename);

                    if (miniTrackList.Count > 0)
                    {                        
                        entry.Delete();

                        ZipFileExtensions.CreateEntryFromFile(zip, tmpExtractionFilename, entry.FullName);
                        miniTrackList.ForEach(item => item.XmlFile = entry.FullName);
                        _currentTrackList.AddRange(miniTrackList);
                    }

                    try
                    {
                        if (File.Exists(tmpExtractionFilename))
                            File.Delete(tmpExtractionFilename);
                    }
                    catch { }
                });
            }
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
            XmlNodeList? nodeList = xmlDoc.SelectNodes(String.Format("/a:Manifest/a:StorageChanges/a:StorageChange/a:Path/a:Value[starts-with(., '{0}')]", ALGEMEEN_TEKSTBLOKKEN_SCHERF), nsmgr);

            if (nodeList == null)
                return;

            foreach (XmlNode node in nodeList)
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                XmlNode storageChange = node.ParentNode.ParentNode;
                XmlNode copyStorageChange = storageChange.CloneNode(true);
                //copyStorageChange.FirstChild.InnerText = "Delete";
                var captionNode = copyStorageChange.SelectSingleNode("a:Translations/a:TranslationChange/a:Caption", nsmgr);
                if(captionNode != null)
                {
                    captionNode.InnerText = String.Concat(captionNode.InnerText.Trim(), " - VERWIJDEREN");
                }
                storageChange.ParentNode.InsertBefore(copyStorageChange, storageChange);
#pragma warning restore CS8602 // Dereference of a possibly null reference.


                string currentValue = node.InnerText;
                if (String.IsNullOrEmpty(currentValue))
                    continue;

                var xmlReferenceName = currentValue.Split("/", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(item => item.EndsWith(".xml"));
                if (String.IsNullOrEmpty(xmlReferenceName))
                    continue;

                var partsWithoutFileNameCollection = currentValue.Split("/", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(item => !item.EndsWith(".xml")).ToList();
                var updatedName = NameConventionItem.FixedNamingConvention(xmlReferenceName);
                partsWithoutFileNameCollection.Add(updatedName);
                string completedAlteredReferenceSource = String.Join("/", partsWithoutFileNameCollection);

                node.InnerText = completedAlteredReferenceSource;
            }

            using (XmlTextWriter wr = new XmlTextWriter(filename, Encoding.UTF8))
            {
                wr.Formatting = Formatting.None; // here's the trick !
                xmlDoc.Save(wr);
            }
        }

        private List<TrackUpdateItem> UpdateReferenceNameConvention(string filename)
        {
            List<TrackUpdateItem> miniTrackList = new List<TrackUpdateItem>();

            XmlDocument xmlDoc = new XmlDocument();
            var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsmgr.AddNamespace("ds", "http://namespaces.docsys.nl/content");
            
            xmlDoc.Load(filename);

            XmlNodeList? nodeList = xmlDoc.SelectNodes(String.Format("//*/ds:include[starts-with(@src, '{0}')]", REFERENTIE_SOURCE), nsmgr);
            if (nodeList == null)
                return miniTrackList;

            foreach(XmlNode node in nodeList)
            {
                string currentValue = node.Attributes["src"].Value;
                
                if (String.IsNullOrEmpty(currentValue))
                    continue;

                var xmlReferenceName = currentValue.Split("/", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(item => item.EndsWith(".xml"));
                if (String.IsNullOrEmpty(xmlReferenceName))
                    continue;

                var partsWithoutFileNameCollection = currentValue.Split("/", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(item => !item.EndsWith(".xml")).ToList();
                var updatedName = NameConventionItem.FixedNamingConvention(xmlReferenceName);
                partsWithoutFileNameCollection.Add(updatedName);
                string completedAlteredReferenceSource = String.Join("/", partsWithoutFileNameCollection);

                node.Attributes["src"].Value = completedAlteredReferenceSource;
                miniTrackList.Add(new TrackUpdateItem(filename, currentValue, completedAlteredReferenceSource));
            }

            using (XmlTextWriter wr = new XmlTextWriter(filename, Encoding.UTF8))
            {
                wr.Formatting = Formatting.None; // here's the trick !
                xmlDoc.Save(wr);
            }

            return miniTrackList;
        }

        internal class NameConventionItem : IDisposable
        {
            private ZipArchiveEntry _entryKey;
            private string _tmpExtractionFilename;
            public static String FixedNamingConvention(String orignalName)
            {
                string result = orignalName.ToLowerInvariant();

                result = result.Replace(" ", "_");
                result = result.Replace("(", "");
                result = result.Replace(")", "");
                result = result.Replace("-", "_");
                result = Regex.Replace(result, @"\d+", m => int.Parse(m.Value).ToString("0#"));
                return result;
            }

            public NameConventionItem(ZipArchiveEntry entryKey)
            {
                Original = entryKey.Name;
                _entryKey = entryKey;
                _tmpExtractionFilename = Path.GetTempFileName();
            }

            public ZipArchiveEntry OriginalArchiveEntryKey { get => _entryKey; }
            public String Original { get; set; }
            public String Updated { get => NameConventionItem.FixedNamingConvention(Original); }
            public String UpdatedEntryKey
            {
                get
                {
                    string firstPart = OriginalArchiveEntryKey.FullName.Replace(Original, String.Empty);
                    string newKey = String.Concat(firstPart, Updated);
                    return newKey;
                }
            }
            public void ExtractMe()
            {
                ZipFileExtensions.ExtractToFile(OriginalArchiveEntryKey, _tmpExtractionFilename, true);
            }

            public void UpdateMe(ZipArchive archive)
            {
                //var entry = archive.GetEntry(OriginalArchiveEntryKey.FullName);
                //if (entry != null)
                    //entry.Delete();

                ZipFileExtensions.CreateEntryFromFile(archive, _tmpExtractionFilename, UpdatedEntryKey);
            }

            public override string ToString()
            {
                return String.Format("NameConventionItem: {0} >> {1}", Original, Updated);
            }

            public void CleanUp()
            {
                if (File.Exists(_tmpExtractionFilename))
                    try
                    {
                        File.Delete(_tmpExtractionFilename);
                    }
                    catch { }
            }

            public void Dispose()
            {
                CleanUp();
            }
        }

        internal class TrackUpdateItem
        {
            public TrackUpdateItem (String xml, String originalSource, String newSource)
            {
                XmlFile = xml;
                OriginalSourceValue = originalSource;
                NewSourceValue = newSource;
            }

            public String XmlFile { get; set; }
            public String OriginalSourceValue { get; set; } 

            public String NewSourceValue { get; set; }

            public override string ToString()
            {
                return String.Format("File: {0} | Original: {1} | New: {2}", XmlFile, OriginalSourceValue, NewSourceValue);
            }

            public bool IsSameValue
            {
                get
                {
                    return this.OriginalSourceValue.Equals(NewSourceValue);
                }
            }
        }
    }
}
