using System;
using System.Collections.Generic;
using System.Linq;
using Cdm.Figma.UI.Utils;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cdm.Figma.UI
{
    public class FigmaImporter : IFigmaImporter
    {
        private readonly HashSet<NodeConverter> _nodeConverters = new HashSet<NodeConverter>();

        public ISet<NodeConverter> nodeConverters => _nodeConverters;

        private readonly HashSet<ComponentConverter> _componentConverters = new HashSet<ComponentConverter>();

        public ISet<ComponentConverter> componentConverters => _componentConverters;

        private readonly List<FigmaImporterLogReference> _logs = new List<FigmaImporterLogReference>();

        public List<FontSource> fonts { get; } = new List<FontSource>();

        public AssetCache generatedGameObjects { get; } = new AssetCache();
        public AssetCache generatedAssets { get; } = new AssetCache();
        public AssetCache dependencyAssets { get; } = new AssetCache();

        public bool failOnError { get; set; } = true;

        /// <summary>
        /// Gets or sets the fallback font that is used when a font mapping does not found.
        /// </summary>
        public TMP_FontAsset fallbackFont { get; set; }

        public static NodeConverter[] GetDefaultNodeConverters()
        {
            return new NodeConverter[]
            {
                new GroupNodeConverter(),
                new FrameNodeConverter(),
                new VectorNodeConverter(),
                new RectangleNodeConverter(),
                new EllipseNodeConverter(),
                new LineNodeConverter(),
                new PolygonNodeConverter(),
                new StarNodeConverter(),
                new TextNodeConverter(),
                new InstanceNodeConverter()
            };
        }

        public static ComponentConverter[] GetDefaultComponentConverters()
        {
            return new ComponentConverter[]
            {
                new UnknownComponentConverter(),
                new ButtonComponentConverter(),
                new ToggleComponentConverter(),
                new SliderComponentConverter(),
                new InputFieldComponentConverter(),
                new ScrollbarComponentConverter(),
                new ScrollViewComponentConverter(),
                new DropdownComponentConverter()
            };
        }

        public Figma.FigmaDesign ImportFile(FigmaFile file, IFigmaImporter.Options options = null)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            options ??= new IFigmaImporter.Options();

            file.BuildHierarchy();

            generatedAssets.Clear();
            generatedGameObjects.Clear();
            dependencyAssets.Clear();
            _logs.Clear();

            InitNodeConverters();
            InitComponentConverters();

            try
            {
                var conversionArgs = new NodeConvertArgs(this, file);
                var importedPages = new List<FigmaPage>();
                var pageNodes = file.document.children;

                foreach (var pageNode in pageNodes)
                {
                    // Do not import ignored pages.
                    if (options.selectedPages != null && options.selectedPages.All(p => p != pageNode.id))
                        continue;

                    var figmaPage = CreateFigmaNode<FigmaPage>(pageNode);
                    figmaPage.rectTransform.anchorMin = new Vector2(0, 0);
                    figmaPage.rectTransform.anchorMax = new Vector2(1, 1);
                    figmaPage.rectTransform.offsetMin = new Vector2(0, 0);
                    figmaPage.rectTransform.offsetMax = new Vector2(0, 0);

                    var nodes = pageNode.children;
                    foreach (var node in nodes)
                    {
                        if (node is FrameNode)
                        {
                            if (TryConvertNode(figmaPage, node, conversionArgs, out var frameNode))
                            {
                                frameNode.rectTransform.anchorMin = new Vector2(0, 0);
                                frameNode.rectTransform.anchorMax = new Vector2(1, 1);
                                frameNode.rectTransform.offsetMin = new Vector2(0, 0);
                                frameNode.rectTransform.offsetMax = new Vector2(0, 0);
                                frameNode.transform.SetParent(figmaPage.rectTransform, false);
                            }
                        }
                    }

                    SetPageLogs(figmaPage, _logs);
                    importedPages.Add(figmaPage);

                    _logs.Clear();
                }

                return FigmaDesign.Create<FigmaDesign>(file, importedPages);
            }
            catch (Exception)
            {
                // In case an error, cleanup generated resources.
                foreach (var generatedObject in generatedGameObjects)
                {
                    if (generatedObject.Value != null)
                    {
                        ObjectUtils.Destroy(generatedObject.Value);
                    }
                }

                foreach (var generatedAsset in generatedAssets)
                {
                    if (generatedAsset.Value != null)
                    {
                        ObjectUtils.Destroy(generatedAsset.Value);
                    }
                }

                generatedAssets.Clear();
                generatedGameObjects.Clear();
                dependencyAssets.Clear();
                throw;
            }
        }

        internal bool TryConvertNode(FigmaNode parentObject, Node node, NodeConvertArgs args,
            out FigmaNode nodeObject)
        {
            // Init instance node's main component, and main component's component set.
            if (node is InstanceNode instanceNode)
            {
                args.file.InitInstanceNode(instanceNode);
            }

            // Try with component converters first.
            var componentConverter = componentConverters.FirstOrDefault(c => c.CanConvert(node, args));
            if (componentConverter != null)
            {
                nodeObject = componentConverter.Convert(parentObject, node, args);
                return true;
            }

            // Try with node converters.
            var nodeConverter = nodeConverters.FirstOrDefault(c => c.CanConvert(node, args));
            if (nodeConverter != null)
            {
                nodeObject = nodeConverter.Convert(parentObject, node, args);
                return true;
            }

            nodeObject = null;
            return false;
        }

        internal bool TryGetFont(string fontName, out TMP_FontAsset font)
        {
            var fontIndex = fonts.FindIndex(
                x => string.Equals(x.fontName, fontName, StringComparison.OrdinalIgnoreCase));

            if (fontIndex >= 0 && fonts[fontIndex].font != null)
            {
                font = fonts[fontIndex].font;
                return true;
            }

            if (fallbackFont != null)
            {
                font = fallbackFont;
                return true;
            }

            font = null;
            return false;
        }

        internal T CreateFigmaNode<T>(Node node) where T : FigmaNode
        {
            var figmaNode = FigmaNode.Create<T>(node);
            generatedGameObjects.Add(figmaNode.nodeID, figmaNode.gameObject);
            return figmaNode;
        }

        internal void LogWarning(FigmaImporterLogType type, string message, Object target = null)
        {
            Debug.LogWarning(message, target);

            _logs.Add(new FigmaImporterLogReference(new FigmaImporterLog(type, message), target));
        }

        internal void LogError(Exception exception, Object target = null)
        {
            if (failOnError)
            {
                throw exception;
            }
            
            Debug.LogError(exception, target);
            _logs.Add(new FigmaImporterLogReference(
                new FigmaImporterLog(FigmaImporterLogType.Error, exception.Message), target));
        }
        
        internal void LogError(string message, Object target = null)
        {
            LogError(new Exception(message), target);
        }

        private static void SetPageLogs(FigmaPage figmaPage, IEnumerable<FigmaImporterLogReference> logReferences)
        {
            foreach (var logReference in logReferences)
            {
                if (logReference.target != null)
                {
                    GameObject targetGameObject = null;

                    if (logReference.target is GameObject gameObject)
                    {
                        targetGameObject = gameObject;
                    }
                    else if (logReference.target is UnityEngine.Component component)
                    {
                        targetGameObject = component.gameObject;
                    }

                    if (targetGameObject != null)
                    {
                        var figmaNode = targetGameObject.GetComponent<FigmaNode>();
                        if (figmaNode != null)
                        {
                            figmaNode.logs.Add(logReference.log);
                        }
                    }
                }

                figmaPage.allLogs.Add(logReference);
            }
        }

        private void InitNodeConverters()
        {
            if (!nodeConverters.Any())
            {
                var converters = GetDefaultNodeConverters();
                foreach (var converter in converters)
                {
                    nodeConverters.Add(converter);
                }
            }
        }

        private void InitComponentConverters()
        {
            if (!componentConverters.Any())
            {
                var converters = GetDefaultComponentConverters();
                foreach (var converter in converters)
                {
                    componentConverters.Add(converter);
                }
            }
        }
    }

    [Serializable]
    public struct FigmaImporterLogReference
    {
        public FigmaImporterLog log;
        public Object target;

        public FigmaImporterLogReference(FigmaImporterLog log, Object target)
        {
            this.log = log;
            this.target = target;
        }
    }

    [Serializable]
    public struct FigmaImporterLog
    {
        public FigmaImporterLogType type;
        public string message;

        public FigmaImporterLog(FigmaImporterLogType type, string message)
        {
            this.type = type;
            this.message = message;
        }
    }

    [Serializable]
    public enum FigmaImporterLogType
    {
        Info,
        Warning,
        Error
    }
}