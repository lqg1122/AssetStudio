﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Drawing.Imaging;
using Tao.DevIl;
using System.Web.Script.Serialization;


//Load parent nodes even if they are not selected to provide transformations?
//For extracting bundles, first check if file exists then decompress

//rigurous search for search files; look into Path.Combine

namespace Unity_Studio
{
    public partial class UnityStudioForm : Form
    {
        private List<string> unityFiles = new List<string>(); //files to load
        public static List<AssetsFile> assetsfileList = new List<AssetsFile>(); //loaded files
        private List<AssetPreloadData> exportableAssets = new List<AssetPreloadData>(); //used to hold all listItems while the list is being filtered
        private List<AssetPreloadData> visibleAssets = new List<AssetPreloadData>(); //used to build the listView
        private AssetPreloadData lastSelectedItem = null;
        private AssetPreloadData lastLoadedAsset = null;
        //private AssetsFile mainDataFile = null;
        private string mainPath = "";
        private string productName = "";
        
        Dictionary<string, Dictionary<string, string>> jsonMats;
        
        private FMOD.System system = null;
        private FMOD.Sound sound = null;
        private FMOD.Channel channel = null;
        private FMOD.SoundGroup masterSoundGroup = null;
        
        private FMOD.MODE loopMode = FMOD.MODE.LOOP_OFF;
        private uint FMODlenms = 0;
        private float FMODVolume = 0.8f;
        private float FMODfrequency;

        private Bitmap imageTexture = null;

        private bool startFilter = false;
        private bool isNameSorted = false;
        private bool isTypeSorted = false;
        private bool isSizeSorted = false;

        //return-to indices for tree search
        private int lastAFile = 0;
        private int lastGObject = 0;

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

        [DllImport("PVRTexLib.dll")]
        private static extern void test();


        private void loadFile_Click(object sender, System.EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                resetForm();
                mainPath = Path.GetDirectoryName(openFileDialog1.FileNames[0]);

                if (openFileDialog1.FilterIndex == 1)
                {
                    MergeSplitAssets(mainPath);

                    unityFiles.AddRange(openFileDialog1.FileNames);
                    progressBar1.Value = 0;
                    progressBar1.Maximum = unityFiles.Count;

                    foreach (var filename in openFileDialog1.FileNames)
                    {
                        StatusStripUpdate("Loading " + Path.GetFileName(filename));
                        LoadAssetsFile(filename);
                    }
                }
                else
                {
                    progressBar1.Value = 0;
                    progressBar1.Maximum = openFileDialog1.FileNames.Length;

                    foreach (var filename in openFileDialog1.FileNames)
                    {
                        LoadBundleFile(filename);
                        progressBar1.PerformStep();
                    }
                }

                BuildAssetStrucutres();
            }
        }

        private void loadFolder_Click(object sender, System.EventArgs e)
        {
            /*FolderBrowserDialog folderBrowserDialog1 = new FolderBrowserDialog();

            folderBrowserDialog1.Description = "Load all Unity assets from folder and subfolders";
            folderBrowserDialog1.ShowNewFolderButton = false;
            //folderBrowserDialog1.SelectedPath = "E:\\Assets\\Unity";
            folderBrowserDialog1.SelectedPath = "E:\\Assets\\Unity\\WebPlayer\\Porsche\\92AAF1\\defaultGeometry";*/

            if (openFolderDialog1.ShowDialog() == DialogResult.OK)
            {
                //mainPath = folderBrowserDialog1.SelectedPath;
                mainPath = openFolderDialog1.FileName;
                if (Path.GetFileName(mainPath) == "Select folder")
                { mainPath = Path.GetDirectoryName(mainPath); }

                if (Directory.Exists(mainPath))
                {
                    resetForm();
                    
                    //TODO find a way to read data directly instead of merging files
                    MergeSplitAssets(mainPath);
                    
                    string[] fileTypes = new string[7] { "maindata.", "level*.", "*.assets", "*.sharedAssets", "CustomAssetBundle-*", "CAB-*", "BuildPlayer-*" };
                    for (int t = 0; t < fileTypes.Length; t++)
                    {
                        string[] fileNames = Directory.GetFiles(mainPath, fileTypes[t], SearchOption.AllDirectories);
                        #region  sort specific types alphanumerically
                        if (fileNames.Length > 0 && (t == 1 || t == 2))
                        {
                            var sortedList = fileNames.ToList();
                            sortedList.Sort((s1, s2) =>
                            {
                                string pattern = "([A-Za-z\\s]*)([0-9]*)";
                                string h1 = Regex.Match(Path.GetFileNameWithoutExtension(s1), pattern).Groups[1].Value;
                                string h2 = Regex.Match(Path.GetFileNameWithoutExtension(s2), pattern).Groups[1].Value;
                                if (h1 != h2)
                                    return h1.CompareTo(h2);
                                string t1 = Regex.Match(Path.GetFileNameWithoutExtension(s1), pattern).Groups[2].Value;
                                string t2 = Regex.Match(Path.GetFileNameWithoutExtension(s2), pattern).Groups[2].Value;
                                if (t1 != "" && t2 != "")
                                    return int.Parse(t1).CompareTo(int.Parse(t2));
                                return 0;
                            });
                            unityFiles.AddRange(sortedList);
                        }
                        #endregion
                        else { unityFiles.AddRange(fileNames); }
                    }

                    unityFiles = unityFiles.Distinct().ToList();
                    progressBar1.Value = 0;
                    progressBar1.Maximum = unityFiles.Count;

                    //use a for loop because list size can change
                    for (int f = 0; f < unityFiles.Count; f++)
                    {
                        var fileName = unityFiles[f];
                        StatusStripUpdate("Loading " + Path.GetFileName(fileName));
                        LoadAssetsFile(fileName);
                    }

                    BuildAssetStrucutres();
                }
                else { StatusStripUpdate("Selected path deos not exist."); }
            }
        }

        private void MergeSplitAssets(string dirPath)
        {
            string[] splitFiles = Directory.GetFiles(dirPath, "*.split0");
            foreach (var splitFile in splitFiles)
            {
                string destFile = Path.GetFileNameWithoutExtension(splitFile);
                string destPath = Path.GetDirectoryName(splitFile) + "\\";
                if (!File.Exists(destPath + destFile))
                {
                    StatusStripUpdate("Merging " + destFile + " split files...");

                    string[] splitParts = Directory.GetFiles(destPath, destFile + ".split*");
                    using (var destStream = File.Create(destPath + destFile))
                    {
                        for (int i = 0; i < splitParts.Length; i++)
                        {
                            string splitPart = destPath + destFile + ".split" + i.ToString();
                            using (var sourceStream = File.OpenRead(splitPart))
                                sourceStream.CopyTo(destStream); // You can pass the buffer size as second argument.
                        }
                    }
                }
            }
        }

        private void LoadAssetsFile(string fileName)
        {
            var loadedAssetsFile = assetsfileList.Find(aFile => aFile.filePath == fileName);
            if (loadedAssetsFile == null)
            {
                //open file here and pass the stream to facilitate loading memory files
                //also by keeping the stream as a property of AssetsFile, it can be used later on to read assets
                AssetsFile assetsFile = new AssetsFile(fileName, new EndianStream(File.OpenRead(fileName), EndianType.BigEndian));
                //if (Path.GetFileName(fileName) == "mainData") { mainDataFile = assetsFile; }

                assetsfileList.Add(assetsFile);
                #region for 2.6.x find mainData and get string version
                if (assetsFile.fileGen == 6 && Path.GetFileName(fileName) != "mainData")
                {
                    AssetsFile mainDataFile = assetsfileList.Find(aFile => aFile.filePath == Path.GetDirectoryName(fileName) + "\\mainData");
                    if (mainDataFile != null)
                    {
                        assetsFile.m_Version = mainDataFile.m_Version;
                        assetsFile.version = mainDataFile.version;
                        assetsFile.buildType = mainDataFile.buildType;
                    }
                    else if (File.Exists(Path.GetDirectoryName(fileName) + "\\mainData"))
                    {
                        mainDataFile = new AssetsFile(Path.GetDirectoryName(fileName) + "\\mainData", new EndianStream(File.OpenRead(Path.GetDirectoryName(fileName) + "\\mainData"), EndianType.BigEndian));

                        assetsFile.m_Version = mainDataFile.m_Version;
                        assetsFile.version = mainDataFile.version;
                        assetsFile.buildType = mainDataFile.buildType;
                    }
                }
                #endregion
                progressBar1.PerformStep();

                foreach (var sharedFile in assetsFile.sharedAssetsList)
                {
                    string sharedFilePath = Path.GetDirectoryName(fileName) + "\\" + sharedFile.fileName;

                    //TODO add extra code to search for the shared file in case it doesn't exist in the main folder
                    //or if it exists or the path is incorrect

                    //var loadedSharedFile = assetsfileList.Find(aFile => aFile.filePath == sharedFilePath);
                    /*var loadedSharedFile = assetsfileList.Find(aFile => aFile.filePath.EndsWith(Path.GetFileName(sharedFile.fileName)));
                    if (loadedSharedFile != null) { sharedFile.Index = assetsfileList.IndexOf(loadedSharedFile); }
                    else if (File.Exists(sharedFilePath))
                    {
                        //progressBar1.Maximum += 1;
                        sharedFile.Index = assetsfileList.Count;
                        LoadAssetsFile(sharedFilePath);
                    }*/

                    //searching in unityFiles would preserve desired order, but...
                    var quedSharedFile = unityFiles.Find(uFile => uFile.EndsWith(Path.GetFileName(sharedFile.fileName)));
                    if (quedSharedFile == null && File.Exists(sharedFilePath))
                    {
                        sharedFile.Index = unityFiles.Count;//this would get screwed if the file fails to load
                        unityFiles.Add(sharedFilePath);
                        progressBar1.Maximum++;
                    }
                    else { sharedFile.Index = unityFiles.IndexOf(quedSharedFile); }
                }
                
            }
        }

        private void LoadBundleFile(string bundleFileName)
        {
            StatusStripUpdate("Decompressing " + Path.GetFileName(bundleFileName) + "...");

            using (BundleFile b_File = new BundleFile(bundleFileName))
            {
                List<AssetsFile> b_assetsfileList = new List<AssetsFile>();

                foreach (var memFile in b_File.MemoryAssetsFileList) //filter unity files
                {
                    bool validAssetsFile = false;
                    switch (Path.GetExtension(memFile.fileName))
                    {
                        case ".assets":
                        case ".sharedAssets":
                            validAssetsFile = true;
                            break;
                        case "":
                            if (memFile.fileName == "mainData" || Regex.IsMatch(memFile.fileName, "level.*?") || Regex.IsMatch(memFile.fileName, "CustomAssetBundle-.*?") || Regex.IsMatch(memFile.fileName, "CAB-.*?") || Regex.IsMatch(memFile.fileName, "BuildPlayer-.*?")) { validAssetsFile = true; }
                            break;
                    }

                    if (validAssetsFile)
                    {
                        StatusStripUpdate("Loading " + memFile.fileName);
                        memFile.fileName = Path.GetDirectoryName(bundleFileName) + "\\" + memFile.fileName; //add path for extract location

                        AssetsFile assetsFile = new AssetsFile(memFile.fileName, new EndianStream(memFile.memStream, EndianType.BigEndian));
                        if (assetsFile.fileGen == 6 && Path.GetFileName(bundleFileName) != "mainData") //2.6.x and earlier don't have a string version before the preload table
                        {
                            //make use of the bundle file version
                            assetsFile.m_Version = b_File.ver3;
                            assetsFile.version = Array.ConvertAll((b_File.ver3.Split(new string[] { ".", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "\n" }, StringSplitOptions.RemoveEmptyEntries)), int.Parse);
                            assetsFile.buildType = b_File.ver3.Split(new string[] { ".", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" }, StringSplitOptions.RemoveEmptyEntries);
                        }

                        b_assetsfileList.Add(assetsFile);
                    }
                    else
                    {
                        memFile.memStream.Close();
                    }
                }

                assetsfileList.AddRange(b_assetsfileList);//will the streams still be available for reading data?

                foreach (var assetsFile in b_assetsfileList)
                {
                    foreach (var sharedFile in assetsFile.sharedAssetsList)
                    {
                        sharedFile.fileName = Path.GetDirectoryName(bundleFileName) + "\\" + sharedFile.fileName;
                        var loadedSharedFile = b_assetsfileList.Find(aFile => aFile.filePath == sharedFile.fileName);
                        if (loadedSharedFile != null) { sharedFile.Index = assetsfileList.IndexOf(loadedSharedFile); }
                    }
                }
            }
        }


        private void extractBundleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openBundleDialog = new OpenFileDialog();
            openBundleDialog.Filter = "Unity bundle files|*.unity3d; *.unity3d.lz4; *.assetbundle; *.bundle; *.bytes|All files (use at your own risk!)|*.*";
            openBundleDialog.FilterIndex = 1;
            openBundleDialog.RestoreDirectory = true;
            openBundleDialog.Multiselect = true;

            if (openBundleDialog.ShowDialog() == DialogResult.OK)
            {
                int extractedCount = extractBundleFile(openBundleDialog.FileName);

                StatusStripUpdate("Finished extracting " + extractedCount.ToString() + " files.");
            }
        }

        private void extractFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int extractedCount = 0;
            List<string> bundleFiles = new List<string>();

            /*FolderBrowserDialog folderBrowserDialog1 = new FolderBrowserDialog();
            folderBrowserDialog1.Description = "Extract all Unity bundles from folder and subfolders";
            folderBrowserDialog1.ShowNewFolderButton = false;*/

            if (openFolderDialog1.ShowDialog() == DialogResult.OK)
            {
                string startPath = openFolderDialog1.FileName;
                if (Path.GetFileName(startPath) == "Select folder")
                { startPath = Path.GetDirectoryName(startPath); }

                string[] fileTypes = new string[6] { "*.unity3d", "*.unity3d.lz4", "*.assetbundle", "*.assetbundle-*", "*.bundle", "*.bytes" };
                foreach (var fileType in fileTypes)
                {
                    string[] fileNames = Directory.GetFiles(startPath, fileType, SearchOption.AllDirectories);
                    bundleFiles.AddRange(fileNames);
                }

                foreach (var fileName in bundleFiles)
                {
                    extractedCount += extractBundleFile(fileName);
                }

                StatusStripUpdate("Finished extracting " + extractedCount.ToString() + " files.");
            }
        }

        private int extractBundleFile(string bundleFileName)
        {
            int extractedCount = 0;

            StatusStripUpdate("Decompressing " + Path.GetFileName(bundleFileName) + " ,,,");

            string extractPath = bundleFileName + "_unpacked\\";
            Directory.CreateDirectory(extractPath);

            using (BundleFile b_File = new BundleFile(bundleFileName))
            {
                foreach (var memFile in b_File.MemoryAssetsFileList)
                {
                    string filePath = extractPath + memFile.fileName.Replace('/','\\');
                    if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                    }
                    if (File.Exists(filePath))
                    {
                        StatusStripUpdate("File " + memFile.fileName + " already exists");
                    }
                    else
                    {
                        StatusStripUpdate("Extracting " + Path.GetFileName(memFile.fileName));
                        extractedCount += 1;

                        using (FileStream file = new FileStream(filePath, FileMode.Create, System.IO.FileAccess.Write))
                        {
                            memFile.memStream.WriteTo(file);
                        }
                    }
                }
            }

            return extractedCount;
        }



        private void enablePreview_Check(object sender, EventArgs e)
        {
            if (lastLoadedAsset != null)
            {
                switch (lastLoadedAsset.Type2)
                {
                    case 28:
                        {
                            if (enablePreview.Checked && imageTexture != null)
                            {
                                previewPanel.BackgroundImage = imageTexture;
                                previewPanel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
                            }
                            else
                            {
                                previewPanel.BackgroundImage = global::Unity_Studio.Properties.Resources.preview;
                                previewPanel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
                            }
                        }
                        break;
                    case 48:
                    case 49:
                        textPreviewBox.Visible = !textPreviewBox.Visible;
                        break;
                    case 128:
                        fontPreviewBox.Visible = !fontPreviewBox.Visible;
                        break;
                    case 83:
                        {
                            FMODpanel.Visible = !FMODpanel.Visible;

                            FMOD.RESULT result;

                            if (channel != null)
                            {
                                bool playing = false;
                                result = channel.isPlaying(ref playing);
                                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                                {
                                    ERRCHECK(result);
                                }

                                if (playing)
                                {
                                    result = channel.stop();
                                    ERRCHECK(result);

                                    //channel = null;
                                    timer.Stop();
                                    FMODtimerLabel.Text = "0:00.0 / " + (FMODlenms / 1000 / 60) + ":" + (FMODlenms / 1000 % 60) + "." + (FMODlenms / 10 % 100); ;
                                    FMODstatusLabel.Text = "Stopped";
                                    FMODprogressBar.Value = 0;
                                    FMODpauseButton.Text = "Pause";
                                    //FMODinfoLabel.Text = "";
                                }
                                else if (enablePreview.Checked)
                                {
                                    result = system.playSound(FMOD.CHANNELINDEX.FREE, sound, false, ref channel);
                                    ERRCHECK(result);

                                    timer.Start();
                                    FMODstatusLabel.Text = "Playing";
                                    //FMODinfoLabel.Text = FMODfrequency.ToString();
                                }
                            }

                            break;
                        }

                }

            }
            else if (lastSelectedItem != null && enablePreview.Checked)
            {
                lastLoadedAsset = lastSelectedItem;
                PreviewAsset(lastLoadedAsset);
            }

            Properties.Settings.Default["enablePreview"] = enablePreview.Checked;
            Properties.Settings.Default.Save();
        }

        private void displayAssetInfo_Check(object sender, EventArgs e)
        {
            if (displayInfo.Checked && assetInfoLabel.Text != null) { assetInfoLabel.Visible = true; }
            else { assetInfoLabel.Visible = false; }

            Properties.Settings.Default["displayInfo"] = displayInfo.Checked;
            Properties.Settings.Default.Save();
        }

        private void MenuItem_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default[((ToolStripMenuItem)sender).Name] = ((ToolStripMenuItem)sender).Checked;
            Properties.Settings.Default.Save();
        }

        private void assetGroupOptions_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default["assetGroupOption"] = ((ToolStripComboBox)sender).SelectedIndex;
            Properties.Settings.Default.Save();
        }

        private void showExpOpt_Click(object sender, EventArgs e)
        {
            ExportOptions exportOpt = new ExportOptions();
            exportOpt.ShowDialog();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox aboutWindow = new AboutBox();
            aboutWindow.ShowDialog();
        }
        

        private void BuildAssetStrucutres()
        {
            #region first loop - read asset data & create list
            StatusStripUpdate("Building asset list...");
            assetListView.BeginUpdate();

            string fileIDfmt = "D" + assetsfileList.Count.ToString().Length.ToString();

            foreach (var assetsFile in assetsfileList)
            {
                var a_Stream = assetsFile.a_Stream;
                var fileGen = assetsFile.fileGen;
                //var m_version = assetsFile.m_version;
                var version = assetsFile.version;
                string fileID = "1" + assetsfileList.IndexOf(assetsFile).ToString(fileIDfmt);

                //ListViewGroup assetGroup = new ListViewGroup(Path.GetFileName(assetsFile.filePath));

                foreach (var asset in assetsFile.preloadTable.Values)
                {
                    asset.uniqueID = fileID + asset.uniqueID;
                    a_Stream.Position = asset.Offset;

                    switch (asset.Type2)
                    {
                        case 1: //GameObject
                            {
                                GameObject m_GameObject = new GameObject(asset);
                                
                                //asset.Text = m_GameObject.m_Name;
                                asset.specificIndex = assetsFile.GameObjectList.Count;
                                assetsFile.GameObjectList.Add(m_GameObject);
                                break;
                            }
                        case 4: //Transform
                            {
                                Transform m_Transform = new Transform(asset);

                                asset.specificIndex = assetsFile.TransformList.Count;
                                assetsFile.TransformList.Add(m_Transform);
                                break;
                            }
                        case 224: //RectTransform
                            {
                                RectTransform m_Rect = new RectTransform(asset);

                                asset.specificIndex = assetsFile.TransformList.Count;
                                assetsFile.TransformList.Add(m_Rect.m_Transform);
                                break;
                            }
                        //case 21: //Material
                        case 28: //Texture2D
                            {
                                Texture2D m_Texture2D = new Texture2D(asset, false);

                                asset.Text = m_Texture2D.m_Name;
                                asset.exportSize = 128 + m_Texture2D.image_data_size;

                                #region Get Info Text
                                asset.InfoText = "Width: " + m_Texture2D.m_Width.ToString() + "\nHeight: " + m_Texture2D.m_Height.ToString() + "\nFormat: ";

                                switch (m_Texture2D.m_TextureFormat)
                                {
                                    case 1: asset.InfoText += "Alpha8"; break;
                                    case 2: asset.InfoText += "ARGB 4.4.4.4"; break;
                                    case 3: asset.InfoText += "BGR 8.8.8"; break;
                                    case 4: asset.InfoText += "GRAB 8.8.8.8"; break;
                                    case 5: asset.InfoText += "BGRA 8.8.8.8"; break;
                                    case 7: asset.InfoText += "RGB 5.6.5"; break;
                                    case 10: asset.InfoText += "RGB DXT1"; break;
                                    case 12: asset.InfoText += "ARGB DXT5"; break;
                                    case 13: asset.InfoText += "RGBA 4.4.4.4"; break;
                                    case 30: asset.InfoText += "PVRTC_RGB2"; asset.exportSize -= 76; break;
                                    case 31: asset.InfoText += "PVRTC_RGBA2"; asset.exportSize -= 76; break;
                                    case 32: asset.InfoText += "PVRTC_RGB4"; asset.exportSize = 52; break;
                                    case 33: asset.InfoText += "PVRTC_RGBA4"; asset.exportSize -= 76; break;
                                    case 34: asset.InfoText += "ETC_RGB4"; asset.exportSize -= 76; break;
                                    default: asset.InfoText += "unknown"; asset.exportSize -= 128; break;
                                }

                                switch (m_Texture2D.m_FilterMode)
                                {
                                    case 0: asset.InfoText += "\nFilter Mode: Point "; break;
                                    case 1: asset.InfoText += "\nFilter Mode: Bilinear "; break;
                                    case 2: asset.InfoText += "\nFilter Mode: Trilinear "; break;

                                }

                                asset.InfoText += "\nAnisotropic level: " + m_Texture2D.m_Aniso.ToString() + "\nMip map bias: " + m_Texture2D.m_MipBias.ToString();

                                switch (m_Texture2D.m_WrapMode)
                                {
                                    case 0: asset.InfoText += "\nWrap mode: Repeat"; break;
                                    case 1: asset.InfoText += "\nWrap mode: Clamp"; break;
                                }
                                #endregion

                                assetsFile.exportableAssets.Add(asset);
                                break;
                            }
                        case 49: //TextAsset
                            {
                                TextAsset m_TextAsset = new TextAsset(asset, false);

                                asset.Text = m_TextAsset.m_Name;
                                asset.exportSize = m_TextAsset.exportSize;
                                assetsFile.exportableAssets.Add(asset);
                                break;
                            }
                        case 83: //AudioClip
                            {
                                AudioClip m_AudioClip = new AudioClip(asset, false);

                                asset.Text = m_AudioClip.m_Name;
                                asset.exportSize = (int)m_AudioClip.m_Size;
                                assetsFile.exportableAssets.Add(asset);
                                break;
                            }
                        case 48: //Shader
                        case 89: //CubeMap
                        case 128: //Font
                            {
                                asset.Text = a_Stream.ReadAlignedString(a_Stream.ReadInt32());
                                assetsFile.exportableAssets.Add(asset);
                                break;
                            }
                        case 129: //PlayerSettings
                            {
                                PlayerSettings plSet = new PlayerSettings(asset);
                                productName = plSet.productName;
                                base.Text = "Unity Studio - " + productName + " - " + assetsFile.m_Version ;
                                break;
                            }

                    }

                    if (asset.Text == "") { asset.Text = asset.TypeString + " #" + asset.uniqueID; }
                    asset.SubItems.AddRange(new string[] { asset.TypeString, asset.exportSize.ToString() });
                }

                exportableAssets.AddRange(assetsFile.exportableAssets);
                //if (assetGroup.Items.Count > 0) { listView1.Groups.Add(assetGroup); }
            }

            visibleAssets = exportableAssets;
            assetListView.VirtualListSize = visibleAssets.Count;

            assetListView.AutoResizeColumn(1, ColumnHeaderAutoResizeStyle.ColumnContent);
            assetListView.AutoResizeColumn(2, ColumnHeaderAutoResizeStyle.ColumnContent);
            resizeNameColumn();

            assetListView.EndUpdate();
            #endregion

            #region second loop - build tree structure
            StatusStripUpdate("Building tree structure...");
            sceneTreeView.BeginUpdate();
            foreach (var assetsFile in assetsfileList)
            {
                GameObject fileNode = new GameObject(null);
                fileNode.Text = Path.GetFileName(assetsFile.filePath);

                foreach (var m_GameObject in assetsFile.GameObjectList)
                {
                    var parentNode = fileNode;
                    
                    Transform m_Transform;
                    if (assetsfileList.TryGetTransform(m_GameObject.m_Transform, out m_Transform))
                    {
                        Transform m_Father;
                        if (assetsfileList.TryGetTransform(m_Transform.m_Father, out m_Father))
                        {
                            //GameObject Parent;
                            if (assetsfileList.TryGetGameObject(m_Father.m_GameObject, out parentNode))
                            {
                                //parentNode = Parent;
                            }
                        }
                    }

                    parentNode.Nodes.Add(m_GameObject);
                }


                if (fileNode.Nodes.Count == 0) { fileNode.Text += " (no children)"; }
                sceneTreeView.Nodes.Add(fileNode);
            }
            sceneTreeView.EndUpdate();
            #endregion

            if (File.Exists(mainPath + "\\materials.json"))
            {
                string matLine = "";
                using (StreamReader reader = File.OpenText(mainPath + "\\materials.json"))
                { matLine = reader.ReadToEnd(); }

                jsonMats = new JavaScriptSerializer().Deserialize<Dictionary<string, Dictionary<string, string>>>(matLine);
                //var jsonMats = new JavaScriptSerializer().DeserializeObject(matLine);
            }

            StatusStripUpdate("Finished loading " + assetsfileList.Count.ToString() + " files with " + (assetListView.Items.Count + sceneTreeView.Nodes.Count).ToString() + " exportable assets.");
            
            progressBar1.Value = 0;
            treeSearch.Select();
            TexEnv dsd = new TexEnv();
        }

        private void assetListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            e.Item = visibleAssets[e.ItemIndex];
        }



        private void tabPageSelected(object sender, TabControlEventArgs e)
        {
            if (e.TabPageIndex == 0) { treeSearch.Select(); }
            else if (e.TabPageIndex == 1) { listSearch.Select(); }
        }

        private void recurseTreeCheck(TreeNodeCollection start)
        {
            foreach (GameObject GObject in start)
            {
                if (GObject.Text.Like(treeSearch.Text))
                {
                    GObject.Checked = !GObject.Checked;
                    if (GObject.Checked) { GObject.EnsureVisible(); }
                }
                else { recurseTreeCheck(GObject.Nodes); }
            }
        }

        private void treeSearch_MouseEnter(object sender, EventArgs e)
        {
            treeTip.Show("Search with * ? widcards. Enter to scroll through results, Ctrl+Enter to select all results.", treeSearch, 5000);
        }

        private void treeSearch_Enter(object sender, EventArgs e)
        {
            if (treeSearch.Text == " Search ")
            {
                treeSearch.Text = "";
                treeSearch.ForeColor = System.Drawing.SystemColors.WindowText;
            }
        }

        private void treeSearch_Leave(object sender, EventArgs e)
        {
            if (treeSearch.Text == "")
            {
                treeSearch.Text = " Search ";
                treeSearch.ForeColor = System.Drawing.SystemColors.GrayText;
            }
        }

        private void treeSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (e.Modifiers == Keys.Control) //toggle all matching nodes //skip children?
                {
                    sceneTreeView.BeginUpdate();
                    //loop assetsFileList?
                    /*foreach (var AFile in assetsfileList)
                    {
                        foreach (var GObject in AFile.GameObjectList)
                        {
                            if (GObject.Text.Like(treeSearch.Text))
                            {
                                GObject.Checked = true;
                                GObject.EnsureVisible();
                            }
                        }
                    }*/

                    //loop TreeView to avoid checking children already checked by parent
                    recurseTreeCheck(sceneTreeView.Nodes);
                    sceneTreeView.EndUpdate();
                }
                else //make visible one by one
                {
                    bool foundNode = false;

                    while (!foundNode && lastAFile < assetsfileList.Count)
                    {
                        var AFile = assetsfileList[lastAFile];

                        while (!foundNode && lastGObject < AFile.GameObjectList.Count)
                        {
                            var GObject = AFile.GameObjectList[lastGObject];
                            if (GObject.Text.Like(treeSearch.Text))
                            {
                                foundNode = true;

                                GObject.EnsureVisible();
                                sceneTreeView.SelectedNode = GObject;

                                lastGObject++;
                                return;
                            }

                            lastGObject++;
                        }

                        lastAFile++;
                        lastGObject = 0;
                    }
                    lastAFile = 0;
                }
            }
        }

        private void sceneTreeView_AfterCheck(object sender, TreeViewEventArgs e)
        {
            foreach (GameObject childNode in e.Node.Nodes)
            {
                childNode.Checked = e.Node.Checked;
            }
        }

        
        private void ListSearchTextChanged(object sender, EventArgs e)
        {
            if (startFilter)
            {
                assetListView.BeginUpdate();
                assetListView.SelectedIndices.Clear();
                //visibleListAssets = exportableAssets.FindAll(ListAsset => ListAsset.Text.StartsWith(ListSearch.Text, System.StringComparison.CurrentCultureIgnoreCase));
                visibleAssets = exportableAssets.FindAll(ListAsset => ListAsset.Text.IndexOf(listSearch.Text, System.StringComparison.CurrentCultureIgnoreCase) >= 0);
                assetListView.VirtualListSize = visibleAssets.Count;
                assetListView.EndUpdate();
            }
        }

        private void listSearch_Enter(object sender, EventArgs e)
        {
            if (listSearch.Text == " Filter ")
            {
                listSearch.Text = "";
                listSearch.ForeColor = System.Drawing.SystemColors.WindowText;
                startFilter = true;
            }
        }

        private void listSearch_Leave(object sender, EventArgs e)
        {
            if (listSearch.Text == "")
            {
                startFilter = false;
                listSearch.Text = " Filter ";
                listSearch.ForeColor = System.Drawing.SystemColors.GrayText;
            }
        }

        private void assetListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            assetListView.BeginUpdate();
            assetListView.SelectedIndices.Clear();
            switch (e.Column)
            {
                case 0:
                    if (isNameSorted) { visibleAssets.Reverse(); }
                    else
                    {
                        visibleAssets.Sort((x, y) => x.Text.CompareTo(y.Text));
                        isNameSorted = true;
                    }

                    break;
                case 1:
                    if (isTypeSorted) { visibleAssets.Reverse(); }
                    else
                    {
                        visibleAssets.Sort((x, y) => x.TypeString.CompareTo(y.TypeString));
                        isTypeSorted = true;
                    }
                    break;
                case 2:
                    if (isSizeSorted) { visibleAssets.Reverse(); }
                    else
                    {
                        visibleAssets.Sort((x, y) => x.exportSize.CompareTo(y.exportSize));
                        isSizeSorted = true;
                    }
                    break;
            }
            assetListView.EndUpdate();
        }

        private void resizeNameColumn()
        {
            var vscroll = ((float)assetListView.VirtualListSize / (float)assetListView.Height) > 0.0567f;
            columnHeaderName.Width = assetListView.Width - columnHeaderType.Width - columnHeaderSize.Width - (vscroll ? 25 : 5);
        }

        private void selectAsset(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            previewPanel.BackgroundImage = global::Unity_Studio.Properties.Resources.preview;
            previewPanel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            assetInfoLabel.Visible = false;
            assetInfoLabel.Text = null;
            textPreviewBox.Visible = false;
            fontPreviewBox.Visible = false;
            FMODpanel.Visible = false;
            lastLoadedAsset = null;

            FMOD.RESULT result;
            if (sound != null)
            {
                result = sound.release();
                ERRCHECK(result);
            }
            sound = null;
            timer.Stop();
            FMODtimerLabel.Text = "0:00.0 / 0:00.0";
            FMODstatusLabel.Text = "Stopped";
            FMODprogressBar.Value = 0;
            FMODinfoLabel.Text = "";

            lastSelectedItem = (AssetPreloadData)e.Item;

            if (e.IsSelected)
            {
                assetInfoLabel.Text = lastSelectedItem.InfoText;
                if (displayInfo.Checked && assetInfoLabel.Text != null) { assetInfoLabel.Visible = true; } //only display the label if asset has info text

                if (enablePreview.Checked)
                {
                    lastLoadedAsset = lastSelectedItem;
                    PreviewAsset(lastLoadedAsset);
                }
            }
        }


        private void splitContainer1_Resize(object sender, EventArgs e)
        {
            resizeNameColumn();
        }

        private void splitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
        {
            resizeNameColumn();
        }


        private void PreviewAsset(AssetPreloadData asset)
        {
            switch (asset.Type2)
            {
                #region Texture2D
                case 28: //Texture2D
                    {
                        Texture2D m_Texture2D = new Texture2D(asset, true);
                        
                        if (m_Texture2D.m_TextureFormat < 30)
                        {
                            byte[] imageBuffer = new byte[128 + m_Texture2D.image_data_size];

                            imageBuffer[0] = 0x44;
                            imageBuffer[1] = 0x44;
                            imageBuffer[2] = 0x53;
                            imageBuffer[3] = 0x20;
                            imageBuffer[4] = 0x7c;
                            
                            BitConverter.GetBytes(m_Texture2D.dwFlags).CopyTo(imageBuffer, 8);
                            BitConverter.GetBytes(m_Texture2D.m_Height).CopyTo(imageBuffer, 12);
                            BitConverter.GetBytes(m_Texture2D.m_Width).CopyTo(imageBuffer, 16);
                            BitConverter.GetBytes(m_Texture2D.dwPitchOrLinearSize).CopyTo(imageBuffer, 20);
                            BitConverter.GetBytes(m_Texture2D.dwMipMapCount).CopyTo(imageBuffer, 28);
                            BitConverter.GetBytes(m_Texture2D.dwSize).CopyTo(imageBuffer, 76);
                            BitConverter.GetBytes(m_Texture2D.dwFlags2).CopyTo(imageBuffer, 80);
                            BitConverter.GetBytes(m_Texture2D.dwFourCC).CopyTo(imageBuffer, 84);
                            BitConverter.GetBytes(m_Texture2D.dwRGBBitCount).CopyTo(imageBuffer, 88);
                            BitConverter.GetBytes(m_Texture2D.dwRBitMask).CopyTo(imageBuffer, 92);
                            BitConverter.GetBytes(m_Texture2D.dwGBitMask).CopyTo(imageBuffer, 96);
                            BitConverter.GetBytes(m_Texture2D.dwBBitMask).CopyTo(imageBuffer, 100);
                            BitConverter.GetBytes(m_Texture2D.dwABitMask).CopyTo(imageBuffer, 104);
                            BitConverter.GetBytes(m_Texture2D.dwCaps).CopyTo(imageBuffer, 108);
                            BitConverter.GetBytes(m_Texture2D.dwCaps2).CopyTo(imageBuffer, 112);
                            
                            m_Texture2D.image_data.CopyTo(imageBuffer, 128);

                            imageTexture = DDSDataToBMP(imageBuffer);
                            imageTexture.RotateFlip(RotateFlipType.RotateNoneFlipY);
                            previewPanel.BackgroundImage = imageTexture;
                            previewPanel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
                        }
                        break;
                    }
                #endregion
                #region AudioClip
                case 83: //AudioClip
                    {
                        AudioClip m_AudioClip = new AudioClip(asset, true);

                        if (m_AudioClip.m_Type != 22 && m_AudioClip.m_Type != 1)
                        {
                            //MemoryStream memoryStream = new MemoryStream(m_AudioData, true);
                            //System.Media.SoundPlayer soundPlayer = new System.Media.SoundPlayer(memoryStream);
                            //soundPlayer.Play();

                            //uint version = 0;
                            FMOD.RESULT result;
                            FMOD.CREATESOUNDEXINFO exinfo = new FMOD.CREATESOUNDEXINFO();

                            /*result = FMOD.Factory.System_Create(ref system);
                            ERRCHECK(result);

                            result = system.getVersion(ref version);
                            ERRCHECK(result);
                            if (version < FMOD.VERSION.number)
                            {
                                MessageBox.Show("Error!  You are using an old version of FMOD " + version.ToString("X") + ".  This program requires " + FMOD.VERSION.number.ToString("X") + ".");
                                Application.Exit();
                            }

                            result = system.init(1, FMOD.INITFLAGS.NORMAL, (IntPtr)null);
                            ERRCHECK(result);*/

                            exinfo.cbsize = Marshal.SizeOf(exinfo);
                            exinfo.length = (uint)m_AudioClip.m_Size;

                            result = system.createSound(m_AudioClip.m_AudioData, (FMOD.MODE.HARDWARE | FMOD.MODE.OPENMEMORY | loopMode), ref exinfo, ref sound);
                            ERRCHECK(result);

                            result = system.playSound(FMOD.CHANNELINDEX.FREE, sound, false, ref channel);
                            ERRCHECK(result);

                            result = sound.getLength(ref FMODlenms, FMOD.TIMEUNIT.MS);
                            if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                            {
                                ERRCHECK(result);
                            }

                            timer.Start();
                            FMODstatusLabel.Text = "Playing";
                            FMODpanel.Visible = true;

                            result = channel.getFrequency(ref FMODfrequency);
                            ERRCHECK(result);
                            FMODinfoLabel.Text = FMODfrequency.ToString() + " Hz";
                        }
                        else { StatusStripUpdate("Unsuported format"); }
                        break;
                    }
                #endregion
                #region Shader & TextAsset
                case 48:
                case 49:
                    {
                        TextAsset m_TextAsset = new TextAsset(asset, true);
                        
                        string m_Script_Text = UnicodeEncoding.UTF8.GetString(m_TextAsset.m_Script);
                        m_Script_Text = Regex.Replace(m_Script_Text, "(?<!\r)\n", "\r\n");
                        textPreviewBox.Text = m_Script_Text;
                        textPreviewBox.Visible = true;

                        break;
                    }
                #endregion
                #region Font
                case 128: //Font
                    {
                        unityFont m_Font = new unityFont(asset);
                        
                        if (m_Font.extension != ".otf" && m_Font.m_FontData.Length > 0)
                        {
                            IntPtr data = Marshal.AllocCoTaskMem(m_Font.m_FontData.Length);
                            Marshal.Copy(m_Font.m_FontData, 0, data, m_Font.m_FontData.Length);

                            System.Drawing.Text.PrivateFontCollection pfc = new System.Drawing.Text.PrivateFontCollection();
                            // We HAVE to do this to register the font to the system (Weird .NET bug !)
                            uint cFonts = 0;
                            AddFontMemResourceEx(data, (uint)m_Font.m_FontData.Length, IntPtr.Zero, ref cFonts);

                            pfc.AddMemoryFont(data, m_Font.m_FontData.Length);
                            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(data);

                            //textPreviewBox.Font = new Font(pfc.Families[0], 16, FontStyle.Regular);
                            //textPreviewBox.Text = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWYZ\r\n1234567890.:,;'\"(!?)+-*/=\r\nThe quick brown fox jumps over the lazy dog. 1234567890";
                            fontPreviewBox.SelectionStart = 0;
                            fontPreviewBox.SelectionLength = 80;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 16, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 81;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 12, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 138;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 18, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 195;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 24, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 252;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 36, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 309;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 48, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 366;
                            fontPreviewBox.SelectionLength = 56;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 60, FontStyle.Regular);
                            fontPreviewBox.SelectionStart = 423;
                            fontPreviewBox.SelectionLength = 55;
                            fontPreviewBox.SelectionFont = new Font(pfc.Families[0], 72, FontStyle.Regular);
                            fontPreviewBox.Visible = true;
                        }

                        break;
                    }
                #endregion
            }
        }

        public static Bitmap DDSDataToBMP(byte[] DDSData)
        {
            // Create a DevIL image "name" (which is actually a number)
            int img_name;
            Il.ilGenImages(1, out img_name);
            Il.ilBindImage(img_name);

            // Load the DDS file into the bound DevIL image
            Il.ilLoadL(Il.IL_DDS, DDSData, DDSData.Length);

            // Set a few size variables that will simplify later code

            int ImgWidth = Il.ilGetInteger(Il.IL_IMAGE_WIDTH);
            int ImgHeight = Il.ilGetInteger(Il.IL_IMAGE_HEIGHT);
            Rectangle rect = new Rectangle(0, 0, ImgWidth, ImgHeight);

            // Convert the DevIL image to a pixel byte array to copy into Bitmap
            Il.ilConvertImage(Il.IL_BGRA, Il.IL_UNSIGNED_BYTE);

            // Create a Bitmap to copy the image into, and prepare it to get data
            Bitmap bmp = new Bitmap(ImgWidth, ImgHeight);
            BitmapData bmd =
              bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            // Copy the pixel byte array from the DevIL image to the Bitmap
            Il.ilCopyPixels(0, 0, 0,
              Il.ilGetInteger(Il.IL_IMAGE_WIDTH),
              Il.ilGetInteger(Il.IL_IMAGE_HEIGHT),
              1, Il.IL_BGRA, Il.IL_UNSIGNED_BYTE,
              bmd.Scan0);

            // Clean up and return Bitmap
            Il.ilDeleteImages(1, ref img_name);
            bmp.UnlockBits(bmd);
            return bmp;
        }

        private void FMODinit()
        {
            FMOD.RESULT result;
            timer.Stop();
            FMODtimerLabel.Text = "0:00.0 / 0:00.0";
            FMODstatusLabel.Text = "Stopped";
            FMODprogressBar.Value = 0;

            if (sound != null)
            {
                result = sound.release();
                ERRCHECK(result);
                sound = null;
            }

            uint version = 0;

            result = FMOD.Factory.System_Create(ref system);
            ERRCHECK(result);

            result = system.getVersion(ref version);
            ERRCHECK(result);
            if (version < FMOD.VERSION.number)
            {
                MessageBox.Show("Error!  You are using an old version of FMOD " + version.ToString("X") + ".  This program requires " + FMOD.VERSION.number.ToString("X") + ".");
                Application.Exit();
            }

            result = system.init(1, FMOD.INITFLAGS.NORMAL, (IntPtr)null);
            ERRCHECK(result);

            result = system.getMasterSoundGroup(ref masterSoundGroup);
            ERRCHECK(result);

            result = masterSoundGroup.setVolume(FMODVolume);
            ERRCHECK(result);
        }

        private void FMODplayButton_Click(object sender, EventArgs e)
        {
            FMOD.RESULT result;
            if (channel != null)
            {
                timer.Start();
                bool playing = false;
                result = channel.isPlaying(ref playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                if (playing)
                {
                    result = channel.stop();
                    ERRCHECK(result);

                    result = system.playSound(FMOD.CHANNELINDEX.FREE, sound, false, ref channel);
                    ERRCHECK(result);

                    FMODpauseButton.Text = "Pause";
                }
                else
                {
                    result = system.playSound(FMOD.CHANNELINDEX.FREE, sound, false, ref channel);
                    ERRCHECK(result);
                    FMODstatusLabel.Text = "Playing";
                    //FMODinfoLabel.Text = FMODfrequency.ToString();

                    uint newms = 0;

                    newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;

                    result = channel.setPosition(newms, FMOD.TIMEUNIT.MS);
                    if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                    {
                        ERRCHECK(result);
                    }
                }
            }
        }

        private void FMODpauseButton_Click(object sender, EventArgs e)
        {
            FMOD.RESULT result;

            if (channel != null)
            {
                bool playing = false;
                bool paused = false;

                result = channel.isPlaying(ref playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                if (playing)
                {
                    result = channel.getPaused(ref paused);
                    ERRCHECK(result);
                    result = channel.setPaused(!paused);
                    ERRCHECK(result);

                    //FMODstatusLabel.Text = (!paused ? "Paused" : playing ? "Playing" : "Stopped");
                    //FMODpauseButton.Text = (!paused ? "Resume" : playing ? "Pause" : "Pause");

                    if (paused)
                    {
                        FMODstatusLabel.Text = (playing ? "Playing" : "Stopped");
                        FMODpauseButton.Text = "Pause";
                        timer.Start();
                    }
                    else
                    {
                        FMODstatusLabel.Text = "Paused";
                        FMODpauseButton.Text = "Resume";
                        timer.Stop();
                    }
                }
            }
        }

        private void FMODstopButton_Click(object sender, EventArgs e)
        {
            FMOD.RESULT result;
            if (channel != null)
            {
                bool playing = false;
                result = channel.isPlaying(ref playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                if (playing)
                {
                    result = channel.stop();
                    ERRCHECK(result);
                    //channel = null;
                    timer.Stop();
                    FMODtimerLabel.Text = "0:00.0 / " + (FMODlenms / 1000 / 60) + ":" + (FMODlenms / 1000 % 60) + "." + (FMODlenms / 10 % 100); ;
                    FMODstatusLabel.Text = "Stopped";
                    FMODprogressBar.Value = 0;
                    FMODpauseButton.Text = "Pause";
                    //FMODinfoLabel.Text = "";
                }
            }
        }

        private void FMODloopButton_CheckedChanged(object sender, EventArgs e)
        {
            FMOD.RESULT result;

            if (FMODloopButton.Checked)
            {
                loopMode = FMOD.MODE.LOOP_NORMAL;
            }
            else
            {
                loopMode = FMOD.MODE.LOOP_OFF;
            }

            if (sound != null)
            {
                result = sound.setMode(loopMode);
                ERRCHECK(result);
            }

            if (channel != null)
            {
                bool playing = false;
                result = channel.isPlaying(ref playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                bool paused = false;
                result = channel.getPaused(ref paused);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                if (playing || paused)
                {
                    result = channel.setMode(loopMode);
                    ERRCHECK(result);
                }
                /*else
                {
                    /esult = system.playSound(FMOD.CHANNELINDEX.FREE, sound, false, ref channel);
                    ERRCHECK(result);

                    result = channel.setMode(loopMode);
                    ERRCHECK(result);
                }*/
            }
        }

        private void FMODvolumeBar_ValueChanged(object sender, EventArgs e)
        {
            FMOD.RESULT result;
            FMODVolume = Convert.ToSingle(FMODvolumeBar.Value) / 10;

            result = masterSoundGroup.setVolume(FMODVolume);
            ERRCHECK(result);

            /*if (channel != null)
            {
                bool playing = false;
                result = channel.isPlaying(ref playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                bool paused = false;
                result = channel.getPaused(ref paused);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                if (playing || paused)
                {
                    result = channel.setVolume(FMODVolume);
                    ERRCHECK(result);
                }
            }*/
        }

        private void FMODprogressBar_Scroll(object sender, EventArgs e)
        {
            uint newms = 0;

            if (channel != null)
            {
                newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;
                FMODtimerLabel.Text = (newms / 1000 / 60) + ":" + (newms / 1000 % 60) + "." + (newms / 10 % 100) + "/" + (FMODlenms / 1000 / 60) + ":" + (FMODlenms / 1000 % 60) + "." + (FMODlenms / 10 % 100);
            }
        }

        private void FMODprogressBar_MouseDown(object sender, MouseEventArgs e)
        {
            timer.Stop();
        }

        private void FMODprogressBar_MouseUp(object sender, MouseEventArgs e)
        {
            FMOD.RESULT result;
            uint newms = 0;

            if (channel != null)
            {
                newms = FMODlenms / 1000 * (uint)FMODprogressBar.Value;

                result = channel.setPosition(newms, FMOD.TIMEUNIT.MS);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                bool playing = false;

                result = channel.isPlaying(ref playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                if (playing) { timer.Start(); }
            }
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            FMOD.RESULT result;
            uint ms = 0;
            bool playing = false;
            bool paused = false;

            if (channel != null)
            {
                result = channel.getPosition(ref ms, FMOD.TIMEUNIT.MS);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                result = channel.isPlaying(ref playing);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }

                result = channel.getPaused(ref paused);
                if ((result != FMOD.RESULT.OK) && (result != FMOD.RESULT.ERR_INVALID_HANDLE))
                {
                    ERRCHECK(result);
                }
            }

            //statusBar.Text = "Time " + (ms / 1000 / 60) + ":" + (ms / 1000 % 60) + ":" + (ms / 10 % 100) + "/" + (lenms / 1000 / 60) + ":" + (lenms / 1000 % 60) + ":" + (lenms / 10 % 100) + " : " + (paused ? "Paused " : playing ? "Playing" : "Stopped");
            FMODtimerLabel.Text = (ms / 1000 / 60) + ":" + (ms / 1000 % 60) + "." + (ms / 10 % 100) + " / " + (FMODlenms / 1000 / 60) + ":" + (FMODlenms / 1000 % 60) + "." + (FMODlenms / 10 % 100);
            FMODprogressBar.Value = (int)(ms * 1000 / FMODlenms);
            FMODstatusLabel.Text = (paused ? "Paused " : playing ? "Playing" : "Stopped");

            if (system != null)
            {
                system.update();
            }
        }

        private void ERRCHECK(FMOD.RESULT result)
        {
            if (result != FMOD.RESULT.OK)
            {
                FMODinit();
                MessageBox.Show("FMOD error! " + result + " - " + FMOD.Error.String(result));
                //Environment.Exit(-1);
            }
        }


        private void Export3DObjects_Click(object sender, EventArgs e)
        {
            if (sceneTreeView.Nodes.Count > 0)
            {
                bool exportSwitch = (((ToolStripItem)sender).Name == "exportAll3DMenuItem") ? true : false;


                var timestamp = DateTime.Now;
                saveFileDialog1.FileName = productName + timestamp.ToString("_yy_MM_dd__HH_mm_ss");
                //extension will be added by the file save dialog

                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    switch ((bool)Properties.Settings.Default["showExpOpt"])
                    {
                        case true:
                            ExportOptions exportOpt = new ExportOptions();
                            if (exportOpt.ShowDialog() == DialogResult.OK) { goto case false; }
                            break;
                        case false:
                            switch (saveFileDialog1.FilterIndex)
                            {
                                case 1:
                                    WriteFBX(saveFileDialog1.FileName, exportSwitch);
                                    break;
                                case 2:
                                    break;
                            }

                            if (openAfterExport.Checked && File.Exists(saveFileDialog1.FileName)) { System.Diagnostics.Process.Start(saveFileDialog1.FileName); }
                            break;
                    }
                }
                
            }
            else { StatusStripUpdate("No Objects available for export"); }
        }

        public void WriteFBX(string FBXfile, bool allNodes)
        {
            var timestamp = DateTime.Now;

            using (StreamWriter FBXwriter = new StreamWriter(FBXfile))
            {
                StringBuilder fbx = new StringBuilder();
                StringBuilder ob = new StringBuilder(); //Objects builder
                StringBuilder cb = new StringBuilder(); //Connections builder
                cb.Append("\n}\n");//Objects end
                cb.Append("\nConnections:  {");

                HashSet<AssetPreloadData> MeshList = new HashSet<AssetPreloadData>();
                HashSet<AssetPreloadData> MaterialList = new HashSet<AssetPreloadData>();
                HashSet<AssetPreloadData> TextureList = new HashSet<AssetPreloadData>();

                int ModelCount = 0;
                int GeometryCount = 0;
                int MaterialCount = 0;
                int TextureCount = 0;

                //using m_PathID as unique ID would fail because it could be a negative number (Hearthstone syndrome)
                //consider using uint
                //no, do something smarter

                #region write generic FBX data
                fbx.Append("; FBX 7.1.0 project file");
                fbx.Append("\nFBXHeaderExtension:  {\n\tFBXHeaderVersion: 1003\n\tFBXVersion: 7100\n\tCreationTimeStamp:  {\n\t\tVersion: 1000");
                fbx.Append("\n\t\tYear: " + timestamp.Year);
                fbx.Append("\n\t\tMonth: " + timestamp.Month);
                fbx.Append("\n\t\tDay: " + timestamp.Day);
                fbx.Append("\n\t\tHour: " + timestamp.Hour);
                fbx.Append("\n\t\tMinute: " + timestamp.Minute);
                fbx.Append("\n\t\tSecond: " + timestamp.Second);
                fbx.Append("\n\t\tMillisecond: " + timestamp.Millisecond);
                fbx.Append("\n\t}\n\tCreator: \"Unity Studio by Chipicao\"\n}\n");

                fbx.Append("\nGlobalSettings:  {");
                fbx.Append("\n\tVersion: 1000");
                fbx.Append("\n\tProperties70:  {");
                fbx.Append("\n\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
                fbx.Append("\n\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
                fbx.Append("\n\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"OriginalUpAxis\", \"int\", \"Integer\", \"\",1");
                fbx.Append("\n\t\tP: \"OriginalUpAxisSign\", \"int\", \"Integer\", \"\",1");
                fbx.AppendFormat("\n\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",{0}", Properties.Settings.Default["scaleFactor"]);
                fbx.Append("\n\t\tP: \"OriginalUnitScaleFactor\", \"double\", \"Number\", \"\",1.0");
                //sb.Append("\n\t\tP: \"AmbientColor\", \"ColorRGB\", \"Color\", \"\",0,0,0");
                //sb.Append("\n\t\tP: \"DefaultCamera\", \"KString\", \"\", \"\", \"Producer Perspective\"");
                //sb.Append("\n\t\tP: \"TimeMode\", \"enum\", \"\", \"\",6");
                //sb.Append("\n\t\tP: \"TimeProtocol\", \"enum\", \"\", \"\",2");
                //sb.Append("\n\t\tP: \"SnapOnFrameMode\", \"enum\", \"\", \"\",0");
                //sb.Append("\n\t\tP: \"TimeSpanStart\", \"KTime\", \"Time\", \"\",0");
                //sb.Append("\n\t\tP: \"TimeSpanStop\", \"KTime\", \"Time\", \"\",153953860000");
                //sb.Append("\n\t\tP: \"CustomFrameRate\", \"double\", \"Number\", \"\",-1");
                //sb.Append("\n\t\tP: \"TimeMarker\", \"Compound\", \"\", \"\"");
                //sb.Append("\n\t\tP: \"CurrentTimeMarker\", \"int\", \"Integer\", \"\",-1");
                fbx.Append("\n\t}\n}\n");

                fbx.Append("\nDocuments:  {");
                fbx.Append("\n\tCount: 1");
                fbx.Append("\n\tDocument: 1234567890, \"\", \"Scene\" {");
                fbx.Append("\n\t\tProperties70:  {");
                fbx.Append("\n\t\t\tP: \"SourceObject\", \"object\", \"\", \"\"");
                fbx.Append("\n\t\t\tP: \"ActiveAnimStackName\", \"KString\", \"\", \"\", \"\"");
                fbx.Append("\n\t\t}");
                fbx.Append("\n\t\tRootNode: 0");
                fbx.Append("\n\t}\n}\n");
                fbx.Append("\nReferences:  {\n}\n");

                fbx.Append("\nDefinitions:  {");
                fbx.Append("\n\tVersion: 100");
                //fbx.AppendFormat("\n\tCount: {0}", 1 + srcModel.nodes.Count + FBXgeometryCount + srcModel.materials.Count + srcModel.usedTex.Count * 2);

                fbx.Append("\n\tObjectType: \"GlobalSettings\" {");
                fbx.Append("\n\t\tCount: 1");
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Model\" {");
                //fbx.AppendFormat("\n\t\tCount: {0}", ModelCount);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Geometry\" {");
                //fbx.AppendFormat("\n\t\tCount: {0}", GeometryCount);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Material\" {");
                //fbx.AppendFormat("\n\t\tCount: {0}", MaterialCount);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Texture\" {");
                //fbx.AppendFormat("\n\t\tCount: {0}", TextureCount);
                fbx.Append("\n\t}");

                fbx.Append("\n\tObjectType: \"Video\" {");
                //fbx.AppendFormat("\n\t\tCount: {0}", TextureCount);
                fbx.Append("\n\t}");
                fbx.Append("\n}\n");
                fbx.Append("\nObjects:  {");

                FBXwriter.Write(fbx);
                fbx.Length = 0;
                #endregion

                #region write Models, collect Mesh & Material objects
                foreach (var assetsFile in assetsfileList)
                {
                    foreach (var m_GameObject in assetsFile.GameObjectList)
                    {
                        if (m_GameObject.Checked || allNodes)
                        {
                            #region write Model and Transform
                            ob.AppendFormat("\n\tModel: {0}, \"Model::{1}\"", m_GameObject.uniqueID, m_GameObject.m_Name);

                            if (m_GameObject.m_MeshFilter != null || m_GameObject.m_SkinnedMeshRenderer != null)
                            {
                                ob.Append(", \"Mesh\" {");
                            }
                            else { ob.Append(", \"Null\" {"); }

                            ob.Append("\n\t\tVersion: 232");
                            ob.Append("\n\t\tProperties70:  {");
                            ob.Append("\n\t\t\tP: \"InheritType\", \"enum\", \"\", \"\",1");
                            ob.Append("\n\t\t\tP: \"ScalingMax\", \"Vector3D\", \"Vector\", \"\",0,0,0");
                            ob.Append("\n\t\t\tP: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");

                            //connect model to parent
                            GameObject parentObject = (GameObject)m_GameObject.Parent;
                            if (parentObject.Checked || allNodes)
                            {
                                cb.AppendFormat("\n\tC: \"OO\",{0},{1}", m_GameObject.uniqueID, parentObject.uniqueID);
                                //if parentObject is a file or folder node, it will have uniqueID 0
                            }
                            else { cb.AppendFormat("\n\tC: \"OO\",{0},0", m_GameObject.uniqueID); }//connect to scene

                            Transform m_Transform;
                            if (assetsfileList.TryGetTransform(m_GameObject.m_Transform, out m_Transform))
                            {
                                float[] m_EulerRotation = QuatToEuler(new float[] { m_Transform.m_LocalRotation[0], -m_Transform.m_LocalRotation[1], -m_Transform.m_LocalRotation[2], m_Transform.m_LocalRotation[3] });

                                ob.AppendFormat("\n\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",{0},{1},{2}", -m_Transform.m_LocalPosition[0], m_Transform.m_LocalPosition[1], m_Transform.m_LocalPosition[2]);
                                ob.AppendFormat("\n\t\t\tP: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",{0},{1},{2}", m_EulerRotation[0], m_EulerRotation[1], m_EulerRotation[2]);//handedness is switched in quat
                                ob.AppendFormat("\n\t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",{0},{1},{2}", m_Transform.m_LocalScale[0], m_Transform.m_LocalScale[1], m_Transform.m_LocalScale[2]);
                            }

                            //mb.Append("\n\t\t\tP: \"UDP3DSMAX\", \"KString\", \"\", \"U\", \"MapChannel:1 = UVChannel_1&cr;&lf;MapChannel:2 = UVChannel_2&cr;&lf;\"");
                            //mb.Append("\n\t\t\tP: \"MaxHandle\", \"int\", \"Integer\", \"UH\",24");
                            ob.Append("\n\t\t}");
                            ob.Append("\n\t\tShading: T");
                            ob.Append("\n\t\tCulling: \"CullingOff\"\n\t}");
                            #endregion

                            #region get MeshFilter
                            AssetPreloadData MeshFilterPD;
                            if (assetsfileList.TryGetPD(m_GameObject.m_MeshFilter, out MeshFilterPD))
                            {
                                MeshFilter m_MeshFilter = new MeshFilter(MeshFilterPD);

                                AssetPreloadData MeshPD;
                                if (assetsfileList.TryGetPD(m_MeshFilter.m_Mesh, out MeshPD))
                                {
                                    MeshList.Add(MeshPD);//first collect meshes in unique list to use instances and avoid duplicate geometry
                                    cb.AppendFormat("\n\tC: \"OO\",{0},{1}", MeshPD.uniqueID, m_GameObject.uniqueID);
                                }
                            }

                            /*if (m_GameObject.m_MeshFilter != null && m_GameObject.m_MeshFilter.m_FileID >= 0)
                            {
                                var MeshFilterAF = assetsfileList[m_GameObject.m_MeshFilter.m_FileID];
                                AssetPreloadData MeshFilterPD;
                                if (MeshFilterAF.preloadTable.TryGetValue(m_GameObject.m_MeshFilter.m_PathID, out MeshFilterPD))
                                {
                                    MeshFilter m_MeshFilter = new MeshFilter(MeshFilterPD);

                                    if (m_MeshFilter.m_Mesh.m_FileID >= 0)
                                    {
                                        var MeshAF = assetsfileList[m_MeshFilter.m_Mesh.m_FileID];
                                        AssetPreloadData MeshPD;
                                        if (MeshAF.preloadTable.TryGetValue(m_MeshFilter.m_Mesh.m_PathID, out MeshPD))
                                        {
                                            MeshList.Add(MeshPD);//first collect meshes in unique list to use instances and avoid duplicate geometry
                                            cb.AppendFormat("\n\tC: \"OO\",{0},{1}", MeshPD.uniqueID, m_GameObject.uniqueID);
                                        }
                                    }
                                }
                            }*/
                            #endregion

                            #region get Renderer
                            AssetPreloadData RendererPD;
                            if (assetsfileList.TryGetPD(m_GameObject.m_Renderer, out RendererPD))
                            {
                                Renderer m_Renderer = new Renderer(RendererPD);

                                foreach (var MaterialPPtr in m_Renderer.m_Materials)
                                {
                                    AssetPreloadData MaterialPD;
                                    if (assetsfileList.TryGetPD(MaterialPPtr, out MaterialPD))
                                    {
                                        MaterialList.Add(MaterialPD);
                                        cb.AppendFormat("\n\tC: \"OO\",{0},{1}", MaterialPD.uniqueID, m_GameObject.uniqueID);
                                    }
                                }
                            }

                            /*if (m_GameObject.m_Renderer != null && m_GameObject.m_Renderer.m_FileID >= 0)
                            {
                                var RendererAF = assetsfileList[m_GameObject.m_Renderer.m_FileID];
                                AssetPreloadData RendererPD;
                                if (RendererAF.preloadTable.TryGetValue(m_GameObject.m_Renderer.m_PathID, out RendererPD))
                                {
                                    Renderer m_Renderer = new Renderer(RendererPD);
                                    foreach (var MaterialPPtr in m_Renderer.m_Materials)
                                    {
                                        if (MaterialPPtr.m_FileID >= 0)
                                        {
                                            var MaterialAF = assetsfileList[MaterialPPtr.m_FileID];
                                            AssetPreloadData MaterialPD;
                                            if (MaterialAF.preloadTable.TryGetValue(MaterialPPtr.m_PathID, out MaterialPD))
                                            {
                                                MaterialList.Add(MaterialPD);
                                                cb.AppendFormat("\n\tC: \"OO\",{0},{1}", MaterialPD.uniqueID, m_GameObject.uniqueID);
                                            }
                                        }
                                    }
                                }
                            }*/
                            #endregion

                            #region get SkinnedMeshRenderer
                            AssetPreloadData SkinnedMeshPD;
                            if (assetsfileList.TryGetPD(m_GameObject.m_SkinnedMeshRenderer, out SkinnedMeshPD))
                            {
                                SkinnedMeshRenderer m_SkinnedMeshRenderer = new SkinnedMeshRenderer(SkinnedMeshPD);

                                foreach (var MaterialPPtr in m_SkinnedMeshRenderer.m_Materials)
                                {
                                    AssetPreloadData MaterialPD;
                                    if (assetsfileList.TryGetPD(MaterialPPtr, out MaterialPD))
                                    {
                                        MaterialList.Add(MaterialPD);
                                        cb.AppendFormat("\n\tC: \"OO\",{0},{1}", MaterialPD.uniqueID, m_GameObject.uniqueID);
                                    }
                                }

                                AssetPreloadData MeshPD;
                                if (assetsfileList.TryGetPD(m_SkinnedMeshRenderer.m_Mesh, out MeshPD))
                                {
                                    MeshList.Add(MeshPD);//first collect meshes in unique list to use instances and avoid duplicate geometry
                                    cb.AppendFormat("\n\tC: \"OO\",{0},{1}", MeshPD.uniqueID, m_GameObject.uniqueID);
                                }
                            }

                            /*if (m_GameObject.m_SkinnedMeshRenderer != null && m_GameObject.m_SkinnedMeshRenderer.m_FileID >= 0)
                            {
                                var SkinnedMeshAF = assetsfileList[m_GameObject.m_SkinnedMeshRenderer.m_FileID];
                                AssetPreloadData SkinnedMeshPD;
                                if (SkinnedMeshAF.preloadTable.TryGetValue(m_GameObject.m_SkinnedMeshRenderer.m_PathID, out SkinnedMeshPD))
                                {
                                    SkinnedMeshRenderer m_SkinnedMeshRenderer = new SkinnedMeshRenderer(SkinnedMeshPD);
                                    
                                    foreach (var MaterialPPtr in m_SkinnedMeshRenderer.m_Materials)
                                    {
                                        if (MaterialPPtr.m_FileID >= 0)
                                        {
                                            var MaterialAF = assetsfileList[MaterialPPtr.m_FileID];
                                            AssetPreloadData MaterialPD;
                                            if (MaterialAF.preloadTable.TryGetValue(MaterialPPtr.m_PathID, out MaterialPD))
                                            {
                                                MaterialList.Add(MaterialPD);
                                                cb.AppendFormat("\n\tC: \"OO\",{0},{1}", MaterialPD.uniqueID, m_GameObject.uniqueID);
                                            }
                                        }
                                    }

                                    if (m_SkinnedMeshRenderer.m_Mesh.m_FileID >= 0)
                                    {
                                        var MeshAF = assetsfileList[m_SkinnedMeshRenderer.m_Mesh.m_FileID];
                                        AssetPreloadData MeshPD;
                                        if (MeshAF.preloadTable.TryGetValue(m_SkinnedMeshRenderer.m_Mesh.m_PathID, out MeshPD))
                                        {
                                            MeshList.Add(MeshPD);
                                            cb.AppendFormat("\n\tC: \"OO\",{0},{1}", MeshPD.uniqueID, m_GameObject.uniqueID);
                                        }
                                    }
                                }
                            }*/
                            #endregion

                            //write data 8MB at a time
                            if (ob.Length > (8 * 0x100000))
                            {
                                FBXwriter.Write(ob);
                                ob.Length = 0;
                            }
                        }
                    }
                }
                #endregion

                #region write Geometry
                foreach (var MeshPD in MeshList)
                {
                    Mesh m_Mesh = new Mesh(MeshPD);

                    if (m_Mesh.m_VertexCount > 0)//general failsafe
                    {
                        StatusStripUpdate("Writing Geometry: " + m_Mesh.m_Name);

                        ob.AppendFormat("\n\tGeometry: {0}, \"Geometry::\", \"Mesh\" {{", MeshPD.uniqueID);
                        ob.Append("\n\t\tProperties70:  {");
                        var randomColor = RandomColorGenerator(MeshPD.uniqueID);
                        ob.AppendFormat("\n\t\t\tP: \"Color\", \"ColorRGB\", \"Color\", \"\",{0},{1},{2}", ((float)randomColor[0] / 255), ((float)randomColor[1] / 255), ((float)randomColor[2] / 255));
                        ob.Append("\n\t\t}");

                        #region Vertices
                        ob.AppendFormat("\n\t\tVertices: *{0} {{\n\t\t\ta: ", m_Mesh.m_VertexCount * 3);

                        int c = 3;//vertex components
                        //skip last value in half4 components
                        if (m_Mesh.m_Vertices.Length == m_Mesh.m_VertexCount * 4) { c++; } //haha

                        //split arrays in groups of 2040 chars
                        uint f3Lines = m_Mesh.m_VertexCount / 40;//40 verts * 3 components * 17 max chars per float including comma
                        uint remf3Verts = m_Mesh.m_VertexCount % 40;

                        uint f2Lines = m_Mesh.m_VertexCount / 60;//60 UVs * 2 components * 17 max chars per float including comma
                        uint remf2Verts = m_Mesh.m_VertexCount % 60;

                        //this is fast but line length is not optimal
                        for (int l = 0; l < f3Lines; l++)
                        {
                            for (int v = 0; v < 40; v++)
                            {
                                ob.AppendFormat("{0},{1},{2},", -m_Mesh.m_Vertices[(l * 40 + v) * c], m_Mesh.m_Vertices[(l * 40 + v) * c + 1], m_Mesh.m_Vertices[(l * 40 + v) * c + 2]);
                            }
                            ob.Append("\n");
                        }
                        
                        if (remf3Verts != 0)
                        {
                            for (int v = 0; v < remf3Verts; v++)
                            {
                                ob.AppendFormat("{0},{1},{2},", -m_Mesh.m_Vertices[(f3Lines * 40 + v) * c], m_Mesh.m_Vertices[(f3Lines * 40 + v) * c + 1], m_Mesh.m_Vertices[(f3Lines * 40 + v) * c + 2]);
                            }
                        }
                        else { ob.Length--; }//remove last newline
                        ob.Length--;//remove last comma
                        
                        ob.Append("\n\t\t}");
                        #endregion

                        #region Indices
                        //in order to test topology for triangles/quads we need to store submeshes and write each one as geometry, then link to Mesh Node
                        ob.AppendFormat("\n\t\tPolygonVertexIndex: *{0} {{\n\t\t\ta: ", m_Mesh.m_Indices.Count);

                        int iLines = m_Mesh.m_Indices.Count / 180;
                        int remTris = (m_Mesh.m_Indices.Count % 180) / 3;

                        for (int l = 0; l < iLines; l++)
                        {
                            for (int f = 0; f < 60; f++)
                            {
                                ob.AppendFormat("{0},{1},{2},", m_Mesh.m_Indices[l * 180 + f * 3], m_Mesh.m_Indices[l * 180 + f * 3 + 2], (-m_Mesh.m_Indices[l * 180 + f * 3 + 1] - 1));
                            }
                            ob.Append("\n");
                        }

                        if (remTris != 0)
                        {
                            for (int f = 0; f < remTris; f++)
                            {
                                ob.AppendFormat("{0},{1},{2},", m_Mesh.m_Indices[iLines * 180 + f * 3], m_Mesh.m_Indices[iLines * 180 + f * 3 + 2], (-m_Mesh.m_Indices[iLines * 180 + f * 3 + 1] - 1));
                            }
                        }
                        else { ob.Length--; }//remove last newline
                        ob.Length--;//remove last comma
                        
                        ob.Append("\n\t\t}");
                        ob.Append("\n\t\tGeometryVersion: 124");
                        #endregion
                        
                        #region Normals
                        if ((bool)Properties.Settings.Default["exportNormals"] && m_Mesh.m_Normals != null && m_Mesh.m_Normals.Length > 0)
                        {
                            ob.Append("\n\t\tLayerElementNormal: 0 {");
                            ob.Append("\n\t\t\tVersion: 101");
                            ob.Append("\n\t\t\tName: \"\"");
                            ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                            ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                            ob.AppendFormat("\n\t\t\tNormals: *{0} {{\n\t\t\ta: ", (m_Mesh.m_VertexCount * 3));

                            if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 3) { c = 3; }
                            else if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 4) { c = 4; }

                            for (int l = 0; l < f3Lines; l++)
                            {
                                for (int v = 0; v < 40; v++)
                                {
                                    ob.AppendFormat("{0},{1},{2},", -m_Mesh.m_Normals[(l * 40 + v) * c], m_Mesh.m_Normals[(l * 40 + v) * c + 1], m_Mesh.m_Normals[(l * 40 + v) * c + 2]);
                                }
                                ob.Append("\n");
                            }

                            if (remf3Verts != 0)
                            {
                                for (int v = 0; v < remf3Verts; v++)
                                {
                                    ob.AppendFormat("{0},{1},{2},", -m_Mesh.m_Normals[(f3Lines * 40 + v) * c], m_Mesh.m_Normals[(f3Lines * 40 + v) * c + 1], m_Mesh.m_Normals[(f3Lines * 40 + v) * c + 2]);
                                }
                            }
                            else { ob.Length--; }//remove last newline
                            ob.Length--;//remove last comma

                            ob.Append("\n\t\t\t}\n\t\t}");
                        }
                        #endregion
                        
                        #region Colors

                        if ((bool)Properties.Settings.Default["exportColors"] && m_Mesh.m_Colors != null && m_Mesh.m_Colors.Length > 0)
                        {
                            ob.Append("\n\t\tLayerElementColor: 0 {");
                            ob.Append("\n\t\t\tVersion: 101");
                            ob.Append("\n\t\t\tName: \"\"");
                            //ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                            //ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                            ob.Append("\n\t\t\tMappingInformationType: \"ByPolygonVertex\"");
                            ob.Append("\n\t\t\tReferenceInformationType: \"IndexToDirect\"");
                            ob.AppendFormat("\n\t\t\tColors: *{0} {{\n\t\t\ta: ", m_Mesh.m_Colors.Length);
                            //ob.Append(string.Join(",", m_Mesh.m_Colors));

                            int cLines = m_Mesh.m_Colors.Length / 120;
                            int remCols = m_Mesh.m_Colors.Length % 120;

                            for (int l = 0; l < cLines; l++)
                            {
                                for (int i = 0; i < 120; i++)
                                {
                                    ob.AppendFormat("{0},", m_Mesh.m_Colors[l * 120 + i]);
                                }
                                ob.Append("\n");
                            }

                            if (remCols > 0)
                            {
                                for (int i = 0; i < remCols; i++)
                                {
                                    ob.AppendFormat("{0},", m_Mesh.m_Colors[cLines * 120 + i]);
                                }
                            }
                            else { ob.Length--; }//remove last newline
                            ob.Length--;//remove last comma

                            ob.Append("\n\t\t\t}");
                            ob.AppendFormat("\n\t\t\tColorIndex: *{0} {{\n\t\t\ta: ", m_Mesh.m_Indices.Count);
                            
                            for (int l = 0; l < iLines; l++)
                            {
                                for (int f = 0; f < 60; f++)
                                {
                                    ob.AppendFormat("{0},{1},{2},", m_Mesh.m_Indices[l * 180 + f * 3], m_Mesh.m_Indices[l * 180 + f * 3 + 2], m_Mesh.m_Indices[l * 180 + f * 3 + 1]);
                                }
                                ob.Append("\n");
                            }

                            if (remTris != 0)
                            {
                                for (int f = 0; f < remTris; f++)
                                {
                                    ob.AppendFormat("{0},{1},{2},", m_Mesh.m_Indices[iLines * 180 + f * 3], m_Mesh.m_Indices[iLines * 180 + f * 3 + 2], m_Mesh.m_Indices[iLines * 180 + f * 3 + 1]);
                                }
                            }
                            else { ob.Length--; }//remove last newline
                            ob.Length--;//remove last comma

                            ob.Append("\n\t\t\t}\n\t\t}");
                        }
                        #endregion

                        #region UV
                        //does FBX support UVW coordinates?
                        if ((bool)Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV1 != null && m_Mesh.m_UV1.Length > 0)
                        {
                            ob.Append("\n\t\tLayerElementUV: 0 {");
                            ob.Append("\n\t\t\tVersion: 101");
                            ob.Append("\n\t\t\tName: \"UVChannel_1\"");
                            ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                            ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                            ob.AppendFormat("\n\t\t\tUV: *{0} {{\n\t\t\ta: ", m_Mesh.m_UV1.Length);

                            for (int l = 0; l < f2Lines; l++)
                            {
                                for (int v = 0; v < 60; v++)
                                {
                                    ob.AppendFormat("{0},{1},", m_Mesh.m_UV1[l * 120 + v * 2], 1 - m_Mesh.m_UV1[l * 120 + v * 2 + 1]);
                                }
                                ob.Append("\n");
                            }

                            if (remf2Verts != 0)
                            {
                                for (int v = 0; v < remf2Verts; v++)
                                {
                                    ob.AppendFormat("{0},{1},", m_Mesh.m_UV1[f2Lines * 120 + v * 2], 1 - m_Mesh.m_UV1[f2Lines * 120 + v * 2 + 1]);
                                }
                            }
                            else { ob.Length--; }//remove last newline
                            ob.Length--;//remove last comma

                            ob.Append("\n\t\t\t}\n\t\t}");
                        }

                        if ((bool)Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV2 != null && m_Mesh.m_UV2.Length > 0)
                        {
                            ob.Append("\n\t\tLayerElementUV: 1 {");
                            ob.Append("\n\t\t\tVersion: 101");
                            ob.Append("\n\t\t\tName: \"UVChannel_2\"");
                            ob.Append("\n\t\t\tMappingInformationType: \"ByVertice\"");
                            ob.Append("\n\t\t\tReferenceInformationType: \"Direct\"");
                            ob.AppendFormat("\n\t\t\tUV: *{0} {{\n\t\t\ta: ", m_Mesh.m_UV2.Length);

                            for (int l = 0; l < f2Lines; l++)
                            {
                                for (int v = 0; v < 60; v++)
                                {
                                    ob.AppendFormat("{0},{1},", m_Mesh.m_UV2[l * 120 + v * 2], 1 - m_Mesh.m_UV2[l * 120 + v * 2 + 1]);
                                }
                                ob.Append("\n");
                            }

                            if (remf2Verts != 0)
                            {
                                for (int v = 0; v < remf2Verts; v++)
                                {
                                    ob.AppendFormat("{0},{1},", m_Mesh.m_UV2[f2Lines * 120 + v * 2], 1 - m_Mesh.m_UV2[f2Lines * 120 + v * 2 + 1]);
                                }
                            }
                            else { ob.Length--; }//remove last newline
                            ob.Length--;//remove last comma

                            ob.Append("\n\t\t\t}\n\t\t}");
                        }
                        #endregion

                        #region Material
                        ob.Append("\n\t\tLayerElementMaterial: 0 {");
                        ob.Append("\n\t\t\tVersion: 101");
                        ob.Append("\n\t\t\tName: \"\"");
                        ob.Append("\n\t\t\tMappingInformationType: \"");
                        if (m_Mesh.m_SubMeshes.Count == 1) { ob.Append("AllSame\""); }
                        else { ob.Append("ByPolygon\""); }
                        ob.Append("\n\t\t\tReferenceInformationType: \"IndexToDirect\"");
                        ob.AppendFormat("\n\t\t\tMaterials: *{0} {{", m_Mesh.m_materialIDs.Count);
                        ob.Append("\n\t\t\t\t");
                        if (m_Mesh.m_SubMeshes.Count == 1) { ob.Append("0"); }
                        else
                        {
                            int idLines = m_Mesh.m_materialIDs.Count / 500;
                            int remIds = m_Mesh.m_materialIDs.Count % 500;

                            for (int l = 0; l < idLines; l++)
                            {
                                for (int i = 0; i < 500; i++)
                                {
                                    ob.AppendFormat("{0},", m_Mesh.m_materialIDs[l * 500 + i]);
                                }
                                ob.Append("\n");
                            }

                            if (remIds != 0)
                            {
                                for (int i = 0; i < remIds; i++)
                                {
                                    ob.AppendFormat("{0},", m_Mesh.m_materialIDs[idLines * 500 + i]);
                                }
                            }
                            else { ob.Length--; }//remove last newline
                            ob.Length--;//remove last comma
                        }
                        ob.Append("\n\t\t\t}\n\t\t}");
                        #endregion

                        #region Layers
                        ob.Append("\n\t\tLayer: 0 {");
                        ob.Append("\n\t\t\tVersion: 100");
                        if ((bool)Properties.Settings.Default["exportNormals"] && m_Mesh.m_Normals != null && m_Mesh.m_Normals.Length > 0)
                        {
                            ob.Append("\n\t\t\tLayerElement:  {");
                            ob.Append("\n\t\t\t\tType: \"LayerElementNormal\"");
                            ob.Append("\n\t\t\t\tTypedIndex: 0");
                            ob.Append("\n\t\t\t}");
                        }
                        ob.Append("\n\t\t\tLayerElement:  {");
                        ob.Append("\n\t\t\t\tType: \"LayerElementMaterial\"");
                        ob.Append("\n\t\t\t\tTypedIndex: 0");
                        ob.Append("\n\t\t\t}");
                        //
                        /*ob.Append("\n\t\t\tLayerElement:  {");
                        ob.Append("\n\t\t\t\tType: \"LayerElementTexture\"");
                        ob.Append("\n\t\t\t\tTypedIndex: 0");
                        ob.Append("\n\t\t\t}");
                        ob.Append("\n\t\t\tLayerElement:  {");
                        ob.Append("\n\t\t\t\tType: \"LayerElementBumpTextures\"");
                        ob.Append("\n\t\t\t\tTypedIndex: 0");
                        ob.Append("\n\t\t\t}");*/
                        if ((bool)Properties.Settings.Default["exportColors"] && m_Mesh.m_Colors != null && m_Mesh.m_Colors.Length > 0)
                        {
                            ob.Append("\n\t\t\tLayerElement:  {");
                            ob.Append("\n\t\t\t\tType: \"LayerElementColor\"");
                            ob.Append("\n\t\t\t\tTypedIndex: 0");
                            ob.Append("\n\t\t\t}");
                        }
                        if ((bool)Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV1 != null && m_Mesh.m_UV1.Length > 0)
                        {
                            ob.Append("\n\t\t\tLayerElement:  {");
                            ob.Append("\n\t\t\t\tType: \"LayerElementUV\"");
                            ob.Append("\n\t\t\t\tTypedIndex: 0");
                            ob.Append("\n\t\t\t}");
                        }
                        ob.Append("\n\t\t}"); //Layer 0 end

                        if ((bool)Properties.Settings.Default["exportUVs"] && m_Mesh.m_UV2 != null && m_Mesh.m_UV2.Length > 0)
                        {
                            ob.Append("\n\t\tLayer: 1 {");
                            ob.Append("\n\t\t\tVersion: 100");
                            ob.Append("\n\t\t\tLayerElement:  {");
                            ob.Append("\n\t\t\t\tType: \"LayerElementUV\"");
                            ob.Append("\n\t\t\t\tTypedIndex: 1");
                            ob.Append("\n\t\t\t}");
                            ob.Append("\n\t\t}"); //Layer 1 end
                        }
                        #endregion

                        ob.Append("\n\t}"); //Geometry end

                        //write data 8MB at a time
                        if (ob.Length > (8 * 0x100000))
                        {
                            FBXwriter.Write(ob);
                            ob.Length = 0;
                        }
                    }
                }
                #endregion

                #region write Materials, collect Texture objects
                StatusStripUpdate("Writing Materials");
                foreach (var MaterialPD in MaterialList)
                {
                    Material m_Material = new Material(MaterialPD);

                    ob.AppendFormat("\n\tMaterial: {0}, \"Material::{1}\", \"\" {{", MaterialPD.uniqueID, m_Material.m_Name);
                    ob.Append("\n\t\tVersion: 102");
                    ob.Append("\n\t\tShadingModel: \"phong\"");
                    ob.Append("\n\t\tMultiLayer: 0");
                    ob.Append("\n\t\tProperties70:  {");
                    ob.Append("\n\t\t\tP: \"ShadingModel\", \"KString\", \"\", \"\", \"phong\"");

                    #region write material colors
                    foreach (var m_Color in m_Material.m_Colors)
                    {
                        switch (m_Color.first)
                        {
                            case "_Color":
                            case "gSurfaceColor":
                                ob.AppendFormat("\n\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",{0},{1},{2}", m_Color.second[0], m_Color.second[1], m_Color.second[2]);
                                break;
                            case "_SpecColor":
                                ob.AppendFormat("\n\t\t\tP: \"SpecularColor\", \"Color\", \"\", \"A\",{0},{1},{2}", m_Color.second[0], m_Color.second[1], m_Color.second[2]);
                                break;
                            case "_ReflectColor":
                                ob.AppendFormat("\n\t\t\tP: \"AmbientColor\", \"Color\", \"\", \"A\",{0},{1},{2}", m_Color.second[0], m_Color.second[1], m_Color.second[2]);
                                break;
                            default:
                                ob.AppendFormat("\n;\t\t\tP: \"{3}\", \"Color\", \"\", \"A\",{0},{1},{2}", m_Color.second[0], m_Color.second[1], m_Color.second[2], m_Color.first);//commented out
                                break;
                        }
                    }
                    #endregion

                    #region write material parameters
                    foreach (var m_Float in m_Material.m_Floats)
                    {
                        switch (m_Float.first)
                        {
                            case "_Shininess":
                                ob.AppendFormat("\n\t\t\tP: \"ShininessExponent\", \"Number\", \"\", \"A\",{0}", m_Float.second);
                                break;
                            default:
                                ob.AppendFormat("\n;\t\t\tP: \"{0}\", \"Number\", \"\", \"A\",{1}", m_Float.first, m_Float.second);
                                break;
                        }
                    }
                    #endregion

                    //ob.Append("\n\t\t\tP: \"SpecularFactor\", \"Number\", \"\", \"A\",0");
                    ob.Append("\n\t\t}");
                    ob.Append("\n\t}");

                    #region write texture connections
                    foreach (var m_TexEnv in m_Material.m_TexEnvs)
                    {
                        AssetPreloadData TexturePD;
                        if (assetsfileList.TryGetPD(m_TexEnv.m_Texture, out TexturePD))
                        {
                            
                        }
                        else if (jsonMats != null)
                        {
                            Dictionary<string, string> matProp;
                            if (jsonMats.TryGetValue(m_Material.m_Name, out matProp))
                            {
                                string texName;
                                if (matProp.TryGetValue(m_TexEnv.name, out texName))
                                {
                                    foreach (var asset in exportableAssets)
                                    {
                                        if (asset.Type2 == 28 && asset.Text == texName)
                                        {
                                            TexturePD = asset;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (TexturePD != null)
                        {
                            TextureList.Add(TexturePD);

                            cb.AppendFormat("\n\tC: \"OP\",{0},{1}, \"", TexturePD.uniqueID, MaterialPD.uniqueID);
                            switch (m_TexEnv.name)
                            {
                                case "_MainTex":
                                case "gDiffuseSampler":
                                    cb.Append("DiffuseColor\"");
                                    break;
                                case "_SpecularMap":
                                case "gSpecularSampler":
                                    cb.Append("SpecularColor\"");
                                    break;
                                case "_NormalMap":
                                case "_BumpMap":
                                case "gNormalSampler":
                                    cb.Append("NormalMap\"");
                                    break;
                                default:
                                    cb.AppendFormat("{0}\"", m_TexEnv.name);
                                    break;
                            }
                        }
                    }
                    #endregion
                }
                #endregion

                #region write & extract Textures
                Directory.CreateDirectory(Path.GetDirectoryName(FBXfile) + "\\Texture2D");

                foreach (var TexturePD in TextureList)
                {
                    Texture2D m_Texture2D = new Texture2D(TexturePD, true);
                    
                    #region extract texture
                    string texPath = Path.GetDirectoryName(FBXfile) + "\\Texture2D\\" + TexturePD.Text;
//TODO check texture type and set path accordingly; eg. CubeMap, Texture3D
                    if (uniqueNames.Checked) { texPath += " #" + TexturePD.uniqueID; }
                    if (m_Texture2D.m_TextureFormat < 30) { texPath += ".dds"; }
                    else if (m_Texture2D.m_TextureFormat < 35) { texPath += ".pvr"; }
                    else { texPath += "_" + m_Texture2D.m_Width.ToString() + "x" + m_Texture2D.m_Height.ToString() + "." + m_Texture2D.m_TextureFormat.ToString() + ".tex"; }

                    if (File.Exists(texPath))
                    {
                        StatusStripUpdate("Texture file " + Path.GetFileName(texPath) + " already exists");
                    }
                    else
                    {
                        StatusStripUpdate("Exporting Texture2D: " + Path.GetFileName(texPath));

                        switch (m_Texture2D.m_TextureFormat)
                        {
                            case 1: //Alpha8
                            case 2: //A4R4G4B4
                            case 3: //B8G8R8 //confirmed on X360, iOS //PS3 unsure
                            case 4: //G8R8A8B8 //confirmed on X360, iOS
                            case 5: //B8G8R8A8 //confirmed on X360, PS3, Web, iOS
                            case 7: //R5G6B5 //confirmed switched on X360; confirmed on iOS
                            case 10: //DXT1
                            case 12: //DXT5
                            case 13: //R4G4B4A4, iOS (only?)
                                WriteDDS(texPath, m_Texture2D);
                                break;
                            case 30: //PVRTC_RGB2
                            case 31: //PVRTC_RGBA2
                            case 32: //PVRTC_RGB4
                            case 33: //PVRTC_RGBA4
                            case 34: //ETC_RGB4
                                WritePVR(texPath, m_Texture2D);
                                break;
                            default:
                                {
                                    using (BinaryWriter writer = new BinaryWriter(File.Open(texPath, FileMode.Create)))
                                    {
                                        writer.Write(m_Texture2D.image_data);
                                        writer.Close();
                                    }
                                    break;
                                }
                        }
                    }
                    #endregion

                    ob.AppendFormat("\n\tTexture: {0}, \"Texture::{1}\", \"\" {{", TexturePD.uniqueID, TexturePD.Text);
                    ob.Append("\n\t\tType: \"TextureVideoClip\"");
                    ob.Append("\n\t\tVersion: 202");
                    ob.AppendFormat("\n\t\tTextureName: \"Texture::{0}\"", TexturePD.Text);
                    ob.Append("\n\t\tProperties70:  {");
                    ob.Append("\n\t\t\tP: \"UVSet\", \"KString\", \"\", \"\", \"UVChannel_0\"");
                    ob.Append("\n\t\t\tP: \"UseMaterial\", \"bool\", \"\", \"\",1");
                    ob.Append("\n\t\t}");
                    ob.AppendFormat("\n\t\tMedia: \"Video::{0}\"", TexturePD.Text);
                    ob.AppendFormat("\n\t\tFileName: \"{0}\"", texPath);
                    ob.AppendFormat("\n\t\tRelativeFilename: \"Texture2D\\{0}\"", Path.GetFileName(texPath));
                    ob.Append("\n\t}");

                    //Video ID is prefixed by 1
                    ob.AppendFormat("\n\tVideo: 1{0}, \"Video::{1}\", \"Clip\" {{", TexturePD.uniqueID, TexturePD.Text);
                    ob.Append("\n\t\tType: \"Clip\"");
                    ob.Append("\n\t\tProperties70:  {");
                    ob.AppendFormat("\n\t\t\tP: \"Path\", \"KString\", \"XRefUrl\", \"\", \"{0}\"", texPath);
                    ob.Append("\n\t\t}");
                    ob.AppendFormat("\n\t\tFileName: \"{0}\"", texPath);
                    ob.AppendFormat("\n\t\tRelativeFilename: \"Texture2D\\{0}\"", Path.GetFileName(texPath));
                    ob.Append("\n\t}");

                    //connect video to texture
                    cb.AppendFormat("\n\tC: \"OO\",1{0},{1}", TexturePD.uniqueID, TexturePD.uniqueID);
                }
                #endregion

                FBXwriter.Write(ob);
                ob.Clear();
                
                cb.Append("\n}");//Connections end
                FBXwriter.Write(cb);
                cb.Clear();

                StatusStripUpdate("Finished exporting " + Path.GetFileName(FBXfile));
            }
        }

        private static float[] QuatToEuler(float[] q)
        {
            double eax = 0;
            double eay = 0;
            double eaz = 0;

            float qx = q[0];
            float qy = q[1];
            float qz = q[2];
            float qw = q[3];

            double[,] M = new double[4, 4];

            double Nq = qx * qx + qy * qy + qz * qz + qw * qw;
            double s = (Nq > 0.0) ? (2.0 / Nq) : 0.0;
            double xs = qx * s, ys = qy * s, zs = qz * s;
            double wx = qw * xs, wy = qw * ys, wz = qw * zs;
            double xx = qx * xs, xy = qx * ys, xz = qx * zs;
            double yy = qy * ys, yz = qy * zs, zz = qz * zs;

            M[0, 0] = 1.0 - (yy + zz); M[0, 1] = xy - wz; M[0, 2] = xz + wy;
            M[1, 0] = xy + wz; M[1, 1] = 1.0 - (xx + zz); M[1, 2] = yz - wx;
            M[2, 0] = xz - wy; M[2, 1] = yz + wx; M[2, 2] = 1.0 - (xx + yy);
            M[3, 0] = M[3, 1] = M[3, 2] = M[0, 3] = M[1, 3] = M[2, 3] = 0.0; M[3, 3] = 1.0;

            double test = Math.Sqrt(M[0, 0] * M[0, 0] + M[1, 0] * M[1, 0]);
            if (test > 16 * 1.19209290E-07F)//FLT_EPSILON
            {
                eax = Math.Atan2(M[2, 1], M[2, 2]);
                eay = Math.Atan2(-M[2, 0], test);
                eaz = Math.Atan2(M[1, 0], M[0, 0]);
            }
            else
            {
                eax = Math.Atan2(-M[1, 2], M[1, 1]);
                eay = Math.Atan2(-M[2, 0], test);
                eaz = 0;
            }

            return new float[3] { (float)(eax * 180 / Math.PI), (float)(eay * 180 / Math.PI), (float)(eaz * 180 / Math.PI) };
        }

        private static byte[] RandomColorGenerator(string name)
        {
            int nameHash = name.GetHashCode();
            Random r = new Random(nameHash);
            //Random r = new Random(DateTime.Now.Millisecond);

            byte red = (byte)r.Next(0, 255);
            byte green = (byte)r.Next(0, 255);
            byte blue = (byte)r.Next(0, 255);

            return new byte[3] { red, green, blue };
        }


        private void ExportAssets_Click(object sender, EventArgs e)
        {
            if (exportableAssets.Count > 0)
            {
                if (saveFolderDialog1.ShowDialog() == DialogResult.OK)
                {
                    var savePath = saveFolderDialog1.FileName;
                    if (Path.GetFileName(savePath) == "Select folder or write folder name to create")
                    { savePath = Path.GetDirectoryName(saveFolderDialog1.FileName); }
                    //Directory.CreateDirectory(saveFolderDialog1.FileName);//this will be created later, when grouping is determined

                    switch (((ToolStripItem)sender).Name)
                    {
                        case "exportAllAssetsMenuItem":
                            ExportAll(savePath, assetGroupOptions.SelectedIndex);
                            break;
                        case "exportFilteredAssetsMenuItem":
                            ExportFiltered(visibleAssets, savePath, assetGroupOptions.SelectedIndex);
                            break;
                        case "exportSelectedAssetsMenuItem":
                            List<AssetPreloadData> selectedAssetList = new List<AssetPreloadData>();
                            var selIndices = assetListView.SelectedIndices;
                            foreach (int index in selIndices) { selectedAssetList.Add((AssetPreloadData)assetListView.Items[index]); }
                            ExportFiltered(selectedAssetList, savePath, assetGroupOptions.SelectedIndex);
                            break;
                    }

                    if (openAfterExport.Checked) { System.Diagnostics.Process.Start(savePath); }
                }

            }
            else
            {
                StatusStripUpdate("No exportable assets loaded");
            }
        }

        private void ExportAll(string selectedPath, int groupFiles)
        {
            int exportedCount = 0;

            foreach (var assetsFile in assetsfileList)
            {
                if (assetsFile.exportableAssets.Count > 0)
                {
                    string exportpath = selectedPath;
                    if (groupFiles == 1) { exportpath += "\\" + Path.GetFileNameWithoutExtension(assetsFile.filePath) + "_export"; }
                    Directory.CreateDirectory(exportpath);

                    foreach (var asset in assetsFile.exportableAssets)
                    {
                        if (groupFiles == 0)
                        {
                            switch (asset.Type2)
                            {
                                case 28:
                                    exportpath = selectedPath + "\\Texture2D";
                                    break;
                                case 83:
                                    exportpath = selectedPath + "\\AudioClip";
                                    break;
                                case 48:
                                    exportpath = selectedPath + "\\Shader";
                                    break;
                                case 49:
                                    exportpath = selectedPath + "\\TextAsset";
                                    break;
                                case 128:
                                    exportpath = selectedPath + "\\Font";
                                    break;
                            }
                            Directory.CreateDirectory(exportpath);
                        }
                        exportedCount += ExportAsset(asset, exportpath);
                    }
                }
            }
            string statusText = "Finished exporting " + exportedCount.ToString() + " assets.";
            if ((exportableAssets.Count - exportedCount) > 0) { statusText += " " + (exportableAssets.Count - exportedCount).ToString() + " assets skipped (not extractable or files already exist)"; }
            StatusStripUpdate(statusText);
        }

        private void ExportFiltered(List<AssetPreloadData> filteredAssetList, string selectedPath, int groupFiles)
        {
            if (filteredAssetList.Count > 0)
            {
                int exportedCount = 0;

                foreach (var asset in filteredAssetList)
                {
                    string exportpath = selectedPath;
                    if (groupFiles == 1) { exportpath += "\\" + Path.GetFileNameWithoutExtension(asset.sourceFile.filePath) + "_export"; }
                    else if (groupFiles == 0)
                    {
                        switch (asset.Type2)
                        {
                            case 28:
                                exportpath = selectedPath + "\\Texture2D";
                                break;
                            case 83:
                                exportpath = selectedPath + "\\AudioClip";
                                break;
                            case 48:
                                exportpath = selectedPath + "\\Shader";
                                break;
                            case 49:
                                exportpath = selectedPath + "\\TextAsset";
                                break;
                            case 128:
                                exportpath = selectedPath + "\\Font";
                                break;
                        }
                    }

                    Directory.CreateDirectory(exportpath);
                    exportedCount += ExportAsset(asset, exportpath);
                }
                
                string statusText = "Finished exporting " + exportedCount.ToString() + " assets.";
                if ((filteredAssetList.Count - exportedCount) > 0) { statusText += " " + (filteredAssetList.Count - exportedCount).ToString() + " assets skipped (not extractable or files already exist)"; }
                StatusStripUpdate(statusText);
            }
            else
            {
                StatusStripUpdate("No exportable assets selected or filtered");
            }
        }

        private int ExportAsset(AssetPreloadData asset, string exportPath)
        {
            int exportCount = 0;
            
            switch (asset.Type2)
            {
                #region Texture2D
                case 28: //Texture2D
                    {
                        Texture2D m_Texture2D = new Texture2D(asset, true);
                        
                        string texPath = exportPath + "\\" + asset.Text;
                        if (uniqueNames.Checked) { texPath += " #" + asset.uniqueID; }
                        if (m_Texture2D.m_TextureFormat < 30) { texPath += ".dds"; }
                        else if (m_Texture2D.m_TextureFormat < 35) { texPath += ".pvr"; }
                        else { texPath += "_" + m_Texture2D.m_Width.ToString() + "x" + m_Texture2D.m_Height.ToString() + "." + m_Texture2D.m_TextureFormat.ToString() + ".tex"; }

                        if (File.Exists(texPath))
                        {
                            StatusStripUpdate("Texture file " + Path.GetFileName(texPath) + " already exists");
                        }
                        else
                        {
                            StatusStripUpdate("Exporting Texture2D: " + Path.GetFileName(texPath));
                            exportCount += 1;

                            switch (m_Texture2D.m_TextureFormat)
                            {
                                case 1: //Alpha8
                                case 2: //A4R4G4B4
                                case 3: //B8G8R8 //confirmed on X360, iOS //PS3 unsure
                                case 4: //G8R8A8B8 //confirmed on X360, iOS
                                case 5: //B8G8R8A8 //confirmed on X360, PS3, Web, iOS
                                case 7: //R5G6B5 //confirmed switched on X360; confirmed on iOS
                                case 10: //DXT1
                                case 12: //DXT5
                                case 13: //R4G4B4A4, iOS (only?)
                                    WriteDDS(texPath, m_Texture2D);
                                    break;
                                case 30: //PVRTC_RGB2
                                case 31: //PVRTC_RGBA2
                                case 32: //PVRTC_RGB4
                                case 33: //PVRTC_RGBA4
                                case 34: //ETC_RGB4
                                    WritePVR(texPath, m_Texture2D);
                                    break;
                                default:
                                    {
                                        using (BinaryWriter writer = new BinaryWriter(File.Open(texPath, FileMode.Create)))
                                        {
                                            writer.Write(m_Texture2D.image_data);
                                            writer.Close();
                                        }
                                        break;
                                    }
                            }
                        }
                        break;
                    }
                #endregion
                #region AudioClip
                case 83: //AudioClip
                    {
                        AudioClip m_AudioClip = new AudioClip(asset, true);
                        
                        string audPath = exportPath + "\\" + asset.Text;
                        if (uniqueNames.Checked) { audPath += " #" + asset.uniqueID; }
                        audPath += m_AudioClip.extension;

                        if (File.Exists(audPath))
                        {
                            StatusStripUpdate("Audio file " + Path.GetFileName(audPath) + " already exists");
                        }
                        else
                        {
                            StatusStripUpdate("Exporting AudioClip: " + Path.GetFileName(audPath));
                            exportCount += 1;
                            
                            using (BinaryWriter writer = new BinaryWriter(File.Open(audPath, FileMode.Create)))
                            {
                                writer.Write(m_AudioClip.m_AudioData);
                                writer.Close();
                            }

                        }
                        break;
                    }
                #endregion
                #region Shader & TextAsset
                case 48: //Shader
                case 49: //TextAsset
                    {
                        TextAsset m_TextAsset = new TextAsset(asset, true);

                        string textAssetPath = exportPath + "\\" + asset.Text;
                        if (uniqueNames.Checked) { textAssetPath += " #" + asset.uniqueID; }
                        textAssetPath += m_TextAsset.extension;

                        if (File.Exists(textAssetPath))
                        {
                            StatusStripUpdate("TextAsset file " + Path.GetFileName(textAssetPath) + " already exists");
                        }
                        else
                        {
                            StatusStripUpdate("Exporting TextAsset: " + Path.GetFileName(textAssetPath));
                            exportCount += 1;

                            using (BinaryWriter writer = new BinaryWriter(File.Open(textAssetPath, FileMode.Create)))
                            {
                                writer.Write(m_TextAsset.m_Script);
                                writer.Close();
                            }
                        }
                        break;
                    }
                #endregion
                #region Font
                case 128: //Font
                    {
                        unityFont m_Font = new unityFont(asset);

                        string fontPath = exportPath + "\\" + asset.Text;
                        if (uniqueNames.Checked) { fontPath += " #" + asset.uniqueID; }
                        fontPath += m_Font.extension;

                        if (File.Exists(fontPath))
                        {
                            StatusStripUpdate("Font file " + Path.GetFileName(fontPath) + " already exists");
                        }
                        else
                        {
                            StatusStripUpdate("Exporting Font: " + Path.GetFileName(fontPath));

                            using (BinaryWriter writer = new BinaryWriter(File.Open(fontPath, FileMode.Create)))
                            {
                                writer.Write(m_Font.m_FontData);
                                writer.Close();
                            }

                            exportCount += 1;
                        }
                        break;
                    }
                #endregion
                /*default:
                    {
                        string assetPath = exportPath + "\\" + asset.Name + "." + asset.TypeString;
                        byte[] assetData = new byte[asset.Size];
                        Stream.Read(assetData, 0, asset.Size);
                        using (BinaryWriter writer = new BinaryWriter(File.Open(assetPath, FileMode.Create)))
                        {
                            writer.Write(assetData);
                            writer.Close();
                        }
                        exportCount += 1;
                        break;
                    }*/
            }
            return exportCount;
        }

        private void WriteDDS(string DDSfile, Texture2D m_Texture2D)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(DDSfile, FileMode.Create)))
            {
                writer.Write(0x20534444);
                writer.Write(0x7C);
                writer.Write(m_Texture2D.dwFlags);
                writer.Write(m_Texture2D.m_Height);
                writer.Write(m_Texture2D.m_Width);
                writer.Write(m_Texture2D.dwPitchOrLinearSize); //should be main tex size without mips);
                writer.Write((int)0); //dwDepth not implemented
                writer.Write(m_Texture2D.dwMipMapCount);
                writer.Write(new byte[44]); //dwReserved1[11]
                writer.Write(m_Texture2D.dwSize);
                writer.Write(m_Texture2D.dwFlags2);
                writer.Write(m_Texture2D.dwFourCC);
                writer.Write(m_Texture2D.dwRGBBitCount);
                writer.Write(m_Texture2D.dwRBitMask);
                writer.Write(m_Texture2D.dwGBitMask);
                writer.Write(m_Texture2D.dwBBitMask);
                writer.Write(m_Texture2D.dwABitMask);
                writer.Write(m_Texture2D.dwCaps);
                writer.Write(m_Texture2D.dwCaps2);
                writer.Write(new byte[12]); //dwCaps3&4 & dwReserved2

                writer.Write(m_Texture2D.image_data);
                writer.Close();
            }
        }

        private void WritePVR(string PVRfile, Texture2D m_Texture2D)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(PVRfile, FileMode.Create)))
            {
                writer.Write(m_Texture2D.pvrVersion);
                writer.Write(m_Texture2D.pvrFlags);
                writer.Write(m_Texture2D.pvrPixelFormat);
                writer.Write(m_Texture2D.pvrColourSpace);
                writer.Write(m_Texture2D.pvrChannelType);
                writer.Write(m_Texture2D.m_Height);
                writer.Write(m_Texture2D.m_Width);
                writer.Write(m_Texture2D.pvrDepth);
                writer.Write(m_Texture2D.pvrNumSurfaces);
                writer.Write(m_Texture2D.pvrNumFaces);
                writer.Write(m_Texture2D.dwMipMapCount);
                writer.Write(m_Texture2D.pvrMetaDataSize);

                writer.Write(m_Texture2D.image_data);
                writer.Close();
            }
        }


        public UnityStudioForm()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            InitializeComponent();
            uniqueNames.Checked = (bool)Properties.Settings.Default["uniqueNames"];
            displayInfo.Checked = (bool)Properties.Settings.Default["displayInfo"];
            enablePreview.Checked = (bool)Properties.Settings.Default["enablePreview"];
            openAfterExport.Checked = (bool)Properties.Settings.Default["openAfterExport"];
            assetGroupOptions.SelectedIndex = (int)Properties.Settings.Default["assetGroupOption"];
            resizeNameColumn();
        }

        private void resetForm()
        {
            /*Properties.Settings.Default["uniqueNames"] = uniqueNamesMenuItem.Checked;
            Properties.Settings.Default["enablePreview"] = enablePreviewMenuItem.Checked;
            Properties.Settings.Default["displayInfo"] = displayAssetInfoMenuItem.Checked;
            Properties.Settings.Default.Save();*/

            base.Text = "Unity Studio";

            unityFiles.Clear();
            assetsfileList.Clear();
            exportableAssets.Clear();
            visibleAssets.Clear();

            sceneTreeView.Nodes.Clear();

            assetListView.VirtualListSize = 0;
            assetListView.Items.Clear();
            assetListView.Groups.Clear();

            previewPanel.BackgroundImage = global::Unity_Studio.Properties.Resources.preview;
            previewPanel.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            assetInfoLabel.Visible = false;
            assetInfoLabel.Text = null;
            textPreviewBox.Visible = false;
            fontPreviewBox.Visible = false;
            lastSelectedItem = null;
            lastLoadedAsset = null;

            FMODinit();

        }

        private void UnityStudioForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            /*Properties.Settings.Default["uniqueNames"] = uniqueNamesMenuItem.Checked;
            Properties.Settings.Default["enablePreview"] = enablePreviewMenuItem.Checked;
            Properties.Settings.Default["displayInfo"] = displayAssetInfoMenuItem.Checked;
            Properties.Settings.Default.Save();

            foreach (var assetsFile in assetsfileList) { assetsFile.a_Stream.Dispose(); } //is this needed?*/
        }

        public void StatusStripUpdate(string statusText)
        {
            toolStripStatusLabel1.Text = statusText;
            statusStrip1.Update();
        }
    }
}
