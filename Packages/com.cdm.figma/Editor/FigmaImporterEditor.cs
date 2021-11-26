using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VectorGraphics.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cdm.Figma
{
    [CustomEditor(typeof(FigmaImporterTaskFile))]
    public class FigmaImporterEditor : Editor
    {
        private FigmaFileAsset _selectedFile;
        private Editor _fileAssetEditor;
        private VisualElement _fileAssetElement;

        private void OnDisable()
        {
            if (_fileAssetEditor != null)
            {
                DestroyImmediate(_fileAssetEditor);
                _fileAssetEditor = null;
            }
        }
        
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{PackageUtils.VisualTreeFolder}/FigmaImporter.uxml");
            visualTree.CloneTree(root);
            
            root.Q<Button>("accessTokenHelpButton").clicked += () =>
            {
                Application.OpenURL("https://www.figma.com/developers/api#access-tokens");
            };
            
            root.Q<Button>("downloadFilesButton").clicked += async () =>
            {
                await GetFilesAsync((FigmaImporterTaskFile) target);
            };
            
            root.Q<Button>("generateViewsButton").clicked += async () =>
            {
                await ImportFilesAsync((FigmaImporterTaskFile) target);
            };
            
            var fileListView = root.Q<ListView>("filesList");
            fileListView.onSelectionChange += objects =>
            {
                if (_fileAssetEditor != null)
                {
                    root.Remove(_fileAssetElement);
                    DestroyImmediate(_fileAssetEditor);
                }
                
                var selectedItem = (SerializedProperty) objects.LastOrDefault();
                if (selectedItem != null)
                {
                    PopulatePageList(root, selectedItem.stringValue);    
                }
            };
            return root;
        }

        private void PopulatePageList(VisualElement root, string fileId)
        {
            var assetPath = GetFigmaAssetPath((FigmaImporterTaskFile) target, fileId);
            if (File.Exists(assetPath))
            {
                _selectedFile = AssetDatabase.LoadAssetAtPath<FigmaFileAsset>(assetPath);

                _fileAssetEditor = CreateEditor(_selectedFile);
                _fileAssetElement = _fileAssetEditor.CreateInspectorGUI();
                _fileAssetElement.Bind(_fileAssetEditor.serializedObject);
                
                root.Add(_fileAssetElement);
            }
        }
        
        
        public override bool HasPreviewGUI()
        {
            return _fileAssetEditor != null && _fileAssetEditor.HasPreviewGUI();
        }

        public override void DrawPreview(Rect previewArea)
        {
            _fileAssetEditor.DrawPreview(previewArea);
        }

        private async Task ImportFilesAsync(FigmaImporterTaskFile taskFile)
        {
            if (taskFile.importer == null)
            {
                Debug.LogError($"{nameof(FigmaImporter)} cannot be empty.");
                return;
            }

            try
            {
                var fileCount = taskFile.fileIds.Count;
                for (var i = 0; i < fileCount; i++)
                {
                    var fileId = taskFile.fileIds[i];

                    EditorUtility.DisplayProgressBar("Importing Figma files", $"File: {fileId}", (float) i / fileCount);

                    var assetPath = GetFigmaAssetPath(taskFile, fileId);
                    if (File.Exists(assetPath))
                    {
                        var fileAsset = AssetDatabase.LoadAssetAtPath<FigmaFileAsset>(assetPath);
                        if (fileAsset != null)
                        {
                            var options = new FigmaImportOptions
                            {
                                pages = fileAsset.pages.Where(p => p.enabled).Select(p => p.id).ToArray()
                            };
                            await taskFile.importer.ImportFileAsync(fileAsset.GetFile(), options);
                        }
                        else
                        {
                            Debug.LogError($"File asset could not be loaded: {assetPath}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"File '{fileId}' asset does not exist. Please download it before importing.");
                    }

                    EditorUtility.DisplayProgressBar("Importing Figma files", $"File: {fileId}",
                        (float) (i + 1) / fileCount);
                }

            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        
        private async Task GetFilesAsync(FigmaImporterTaskFile taskFile)
        {
            try
            {
                var fileCount = taskFile.fileIds.Count;
                for (var i = 0; i < fileCount; i++)
                {
                    var fileId = taskFile.fileIds[i];
                
                    EditorUtility.DisplayProgressBar("Downloading Figma files", $"File: {fileId}", (float) i / fileCount);
                    
                    var fileContent = await FigmaApi.GetFileAsTextAsync(
                        new FigmaFileRequest(taskFile.personalAccessToken, fileId)
                        {
                            //geometry = "paths"
                        });

                    var file = FigmaFile.FromString(fileContent);
                    var thumbnail = await FigmaApi.GetThumbnailImageAsync(file.thumbnailUrl);
                    
                    // Save figma file asset.
                    var fileAsset = SaveFigmaFile(taskFile, file, fileId, fileContent, thumbnail);
                    
                    // Save Vector nodes as graphic asset.
                    await SaveVectorGraphicsAsync(taskFile, file, fileId, fileAsset);

                    EditorUtility.DisplayProgressBar("Downloading Figma files", $"File: {fileId}", (float) (i + 1) / fileCount);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static async Task SaveVectorGraphicsAsync(
            FigmaImporterTaskFile taskFile, FigmaFile file, string fileId, FigmaFileAsset fileAsset)
        {
            var nodes = new List<VectorNode>();
            file.document.Traverse(node =>
            {
                if (node.visible)
                {
                    nodes.Add((VectorNode) node);    
                }
                return true;
            }, NodeType.Vector);

            if (!nodes.Any())
                return;
            
            var graphics = 
                await FigmaApi.GetImageAsync(new FigmaImageRequest(taskFile.personalAccessToken, fileId)
                {
                    ids = nodes.Select(x => x.id).ToArray(),
                    format = "svg",
                    svgIncludeId = false,
                    svgSimplifyStroke = true
                });
            
            var directory = Path.Combine("Assets", taskFile.graphicsPath);
            Directory.CreateDirectory(directory);

            foreach (var graphic in graphics)
            {
                if (graphic.Value != null)
                {
                    var fileName = $"{graphic.Key.Replace(":", "-").Replace(";", "_")}.svg";
                    
                    var path = Path.Combine(Application.dataPath, taskFile.graphicsPath, fileName);
                    await File.WriteAllBytesAsync(path, graphic.Value);

                    var assetPath = Path.Combine("Assets", taskFile.graphicsPath, fileName);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    
                    var svgImporter = (SVGImporter) AssetImporter.GetAtPath(assetPath);
                    svgImporter.PreserveSVGImageAspect = true;
                    svgImporter.SvgType = SVGType.UIToolkit;
                    
                    EditorUtility.SetDirty(svgImporter);
                    svgImporter.SaveAndReimport();

                    var resourcePath = Path.Combine(taskFile.graphicsPath, fileName).Replace("\\", "/");
                    fileAsset.vectorGraphics.Add(graphic.Key, resourcePath);
                }
                else
                {
                    Debug.LogWarning($"Graphic could not be rendered: {graphic.Key}");
                }
            }
            
            EditorUtility.SetDirty(fileAsset);
            AssetDatabase.SaveAssetIfDirty(fileAsset);
        }
        
        private static FigmaFileAsset SaveFigmaFile(FigmaImporterTaskFile taskFile, FigmaFile figmaFile, string fileId, 
            string fileContent, byte[] thumbnail)
        {
            var directory = Path.Combine("Assets", taskFile.assetsPath);
            Directory.CreateDirectory(directory);
            
            var figmaAssetPath = GetFigmaAssetPath(taskFile, fileId);

            var oldFigmaAsset = AssetDatabase.LoadAssetAtPath<FigmaFileAsset>(figmaAssetPath);
            var oldPages = oldFigmaAsset != null ? oldFigmaAsset.pages : new FigmaFilePage[0];
            
            var figmaAsset = CreateInstance<FigmaFileAsset>();
            figmaAsset.id = fileId;
            figmaAsset.title = figmaFile.name;
            figmaAsset.version = figmaFile.version;
            figmaAsset.lastModified = figmaFile.lastModified.ToString("u");
            figmaAsset.content = new TextAsset(JObject.Parse(fileContent).ToString(Formatting.Indented));
            figmaAsset.content.name = "File";
            figmaAsset.thumbnail = new Texture2D(1, 1);
            figmaAsset.thumbnail.name = "Thumbnail";
            figmaAsset.thumbnail.LoadImage(thumbnail);
            
            var canvases = figmaFile.document.children;
            var pages = new FigmaFilePage[canvases.Length];
            
            for (var i = 0; i < pages.Length; i++)
            {
                pages[i] = new FigmaFilePage()
                {
                    id = canvases[i].id,
                    name = canvases[i].name
                };

                var oldPage = oldPages.FirstOrDefault(x => x.id == pages[i].id);
                if (oldPage != null)
                {
                    pages[i].enabled = oldPage.enabled;
                }
            }

            figmaAsset.pages = pages;

            
            AssetDatabase.DeleteAsset(figmaAssetPath);
            AssetDatabase.CreateAsset(figmaAsset, figmaAssetPath);
            
            AssetDatabase.AddObjectToAsset(figmaAsset.content, figmaAsset);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(figmaAsset.content));
            
            AssetDatabase.AddObjectToAsset(figmaAsset.thumbnail, figmaAsset);
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(figmaAsset.thumbnail));
            
            AssetDatabase.Refresh();
            
            Debug.Log($"Figma file saved at: {figmaAssetPath}");
            return figmaAsset;
        }
        
        private static string GetFigmaAssetPath(FigmaImporterTaskFile taskFile, string fileId) 
            => Path.Combine("Assets", taskFile.assetsPath,  $"{fileId}.asset");
    }
}