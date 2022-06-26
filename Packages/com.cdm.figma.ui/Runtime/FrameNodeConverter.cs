using System.Linq;
using Cdm.Figma.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Cdm.Figma.UI
{
    public class FrameNodeConverter : NodeConverter<FrameNode>
    {
        protected override NodeObject Convert(NodeObject parentObject, FrameNode frameNode, NodeConvertArgs args)
        {
            var nodeObject = NodeObject.NewNodeObject(frameNode, args);
            nodeObject.SetTransform(frameNode);

            // Frame node's parent may be a page so check if it is INodeTransform.
            if (frameNode.parent is INodeTransform parent)
            {
                nodeObject.SetLayoutConstraints(parent);
            }

            AddImageIfNeeded(nodeObject, frameNode);
            AddLayoutComponentIfNeeded(nodeObject, frameNode);
            AddContentSizeFitterIfNeeded(nodeObject, frameNode);
            AddMaskIfNeeded(nodeObject, frameNode);

            BuildChildren(frameNode, nodeObject, args);

            return nodeObject;
        }

        private static void BuildChildren(FrameNode currentNode, NodeObject nodeObject, NodeConvertArgs args)
        {
            var children = currentNode.children;
            if (children != null)
            {
                for (var child = 0; child < children.Length; child++)
                {
                    if (args.importer.TryConvertNode(nodeObject, children[child], args, out var childObject))
                    {
                        if (currentNode.layoutMode != LayoutMode.None)
                        {
                            childObject.gameObject.AddComponent<LayoutElement>();
                            HandleFillContainer(currentNode.layoutMode, nodeObject, childObject);
                        }

                        childObject.rectTransform.SetParent(nodeObject.rectTransform, false);
                        childObject.AdjustPosition(currentNode.size);
                    }
                }
            }
        }

        private static void AddImageIfNeeded(NodeObject nodeObject, FrameNode frameNode)
        {
            if (frameNode.fills.Any() || frameNode.strokes.Any())
            {
                var options = new VectorImageUtils.SpriteOptions()
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    sampleCount = 8,
                    textureSize = 1024
                };

                var sprite = VectorImageUtils.CreateSpriteFromRect(frameNode, options);

                var image = nodeObject.gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.color = new UnityEngine.Color(1f, 1f, 1f, frameNode.opacity);
                foreach (var fill in frameNode.fills)
                {
                    if (!fill.visible)
                    {
                        //Multiple fill is not supported, only one image is attached to the node object
                        image.enabled = false;
                    }
                }
            }
        }

        private static void AddMaskIfNeeded(NodeObject nodeObject, FrameNode frameNode)
        {
            if (frameNode.clipsContent)
            {
                nodeObject.gameObject.AddComponent<Mask>();
            }
        }

        private static void HandleFillContainer(LayoutMode layoutMode, NodeObject nodeObject, NodeObject childElement)
        {
            INodeLayout childLayout = (INodeLayout)childElement.node;
            INodeTransform childTransform = (INodeTransform)childElement.node;
            
            if (childLayout.layoutAlign == LayoutAlign.Stretch)
            {
                if (layoutMode == LayoutMode.Horizontal)
                {
                    nodeObject.GetComponent<HorizontalLayoutGroup>().childControlHeight = true;
                    childElement.gameObject.GetComponent<LayoutElement>().flexibleHeight = 1;
                }
                else if (layoutMode == LayoutMode.Vertical)
                {
                    nodeObject.GetComponent<VerticalLayoutGroup>().childControlWidth = true;
                    childElement.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1;
                }
            }
            else
            {
                if (layoutMode == LayoutMode.Horizontal)
                {
                    nodeObject.GetComponent<HorizontalLayoutGroup>().childControlHeight = true;
                    childElement.gameObject.GetComponent<LayoutElement>().minHeight = childTransform.size.y;
                }
                else
                {
                    nodeObject.GetComponent<VerticalLayoutGroup>().childControlWidth = true;
                    childElement.gameObject.GetComponent<LayoutElement>().minWidth = childTransform.size.x;
                }
            }

            if (childLayout.layoutGrow.HasValue && childLayout.layoutGrow != 0)
            {
                if (layoutMode == LayoutMode.Horizontal)
                {
                    nodeObject.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
                    childElement.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1;
                    childElement.gameObject.GetComponent<LayoutElement>().minWidth = 1;
                }
                else if (layoutMode == LayoutMode.Vertical)
                {
                    nodeObject.GetComponent<VerticalLayoutGroup>().childControlHeight = true;
                    childElement.gameObject.GetComponent<LayoutElement>().flexibleHeight = 1;
                    childElement.gameObject.GetComponent<LayoutElement>().minHeight = 1;
                }
            }
            else
            {
                if (layoutMode == LayoutMode.Horizontal)
                {
                    nodeObject.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
                    childElement.gameObject.GetComponent<LayoutElement>().minWidth = childTransform.size.x;
                }
                else
                {
                    nodeObject.GetComponent<VerticalLayoutGroup>().childControlHeight = true;
                    childElement.gameObject.GetComponent<LayoutElement>().minHeight = childTransform.size.y;
                }
            }
        }

        private static void AddContentSizeFitterIfNeeded(NodeObject nodeObject, FrameNode groupNode)
        {
            if (groupNode.layoutMode == LayoutMode.None)
                return;
            
            if (groupNode.primaryAxisSizingMode == AxisSizingMode.Auto ||
                groupNode.counterAxisSizingMode == AxisSizingMode.Auto)
            {
                nodeObject.gameObject.AddComponent<ContentSizeFitter>();
            }

            if (groupNode.primaryAxisSizingMode == AxisSizingMode.Auto)
            {
                if (groupNode.layoutMode == LayoutMode.Horizontal)
                {
                    nodeObject.gameObject.GetComponent<ContentSizeFitter>().horizontalFit =
                        ContentSizeFitter.FitMode.PreferredSize;
                }
                else
                {
                    nodeObject.gameObject.GetComponent<ContentSizeFitter>().verticalFit =
                        ContentSizeFitter.FitMode.PreferredSize;
                }
            }

            if (groupNode.counterAxisSizingMode == AxisSizingMode.Auto)
            {
                if (groupNode.layoutMode == LayoutMode.Horizontal)
                {
                    nodeObject.gameObject.GetComponent<ContentSizeFitter>().verticalFit =
                        ContentSizeFitter.FitMode.PreferredSize;
                }
                else
                {
                    nodeObject.gameObject.GetComponent<ContentSizeFitter>().horizontalFit =
                        ContentSizeFitter.FitMode.PreferredSize;
                }
            }
        }

        private static void AddLayoutComponentIfNeeded(NodeObject nodeObject, FrameNode groupNode)
        {
            var layoutMode = groupNode.layoutMode;
            if (layoutMode == LayoutMode.None)
                return;

            HorizontalOrVerticalLayoutGroup layoutGroup = null;

            if (layoutMode == LayoutMode.Horizontal)
            {
                layoutGroup = nodeObject.gameObject.AddComponent<HorizontalLayoutGroup>();

                if (groupNode.primaryAxisAlignItems == PrimaryAxisAlignItems.Min)
                {
                    if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Min)
                    {
                        layoutGroup.childAlignment = TextAnchor.UpperLeft;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Max)
                    {
                        layoutGroup.childAlignment = TextAnchor.LowerLeft;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Center)
                    {
                        layoutGroup.childAlignment = TextAnchor.MiddleLeft;
                    }
                }
                else if (groupNode.primaryAxisAlignItems == PrimaryAxisAlignItems.Max)
                {
                    if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Min)
                    {
                        layoutGroup.childAlignment = TextAnchor.UpperRight;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Max)
                    {
                        layoutGroup.childAlignment = TextAnchor.LowerRight;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Center)
                    {
                        layoutGroup.childAlignment = TextAnchor.MiddleRight;
                    }
                }
                else if (groupNode.primaryAxisAlignItems == PrimaryAxisAlignItems.Center)
                {
                    if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Min)
                    {
                        layoutGroup.childAlignment = TextAnchor.UpperCenter;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Max)
                    {
                        layoutGroup.childAlignment = TextAnchor.LowerCenter;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Center)
                    {
                        layoutGroup.childAlignment = TextAnchor.MiddleCenter;
                    }
                }
            }
            else
            {
                layoutGroup = nodeObject.gameObject.AddComponent<VerticalLayoutGroup>();

                if (groupNode.primaryAxisAlignItems == PrimaryAxisAlignItems.Min)
                {
                    if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Min)
                    {
                        layoutGroup.childAlignment = TextAnchor.UpperLeft;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Max)
                    {
                        layoutGroup.childAlignment = TextAnchor.UpperRight;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Center)
                    {
                        layoutGroup.childAlignment = TextAnchor.UpperCenter;
                    }
                }
                else if (groupNode.primaryAxisAlignItems == PrimaryAxisAlignItems.Max)
                {
                    if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Min)
                    {
                        layoutGroup.childAlignment = TextAnchor.LowerLeft;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Max)
                    {
                        layoutGroup.childAlignment = TextAnchor.LowerRight;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Center)
                    {
                        layoutGroup.childAlignment = TextAnchor.LowerCenter;
                    }
                }
                else if (groupNode.primaryAxisAlignItems == PrimaryAxisAlignItems.Center)
                {
                    if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Min)
                    {
                        layoutGroup.childAlignment = TextAnchor.MiddleLeft;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Max)
                    {
                        layoutGroup.childAlignment = TextAnchor.MiddleRight;
                    }
                    else if (groupNode.counterAxisAlignItems == CounterAxisAlignItems.Center)
                    {
                        layoutGroup.childAlignment = TextAnchor.MiddleCenter;
                    }
                }
            }

            layoutGroup.childControlWidth = false;
            layoutGroup.childControlHeight = false;
            layoutGroup.childScaleWidth = false;
            layoutGroup.childScaleHeight = false;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childForceExpandHeight = false;

            // Set padding.
            nodeObject.GetComponent<LayoutGroup>().padding = new RectOffset(
                (int)groupNode.paddingLeft,
                (int)groupNode.paddingRight,
                (int)groupNode.paddingTop,
                (int)groupNode.paddingBottom);

            // Set spacing.
            layoutGroup.spacing = groupNode.itemSpacing;
        }
    }
}