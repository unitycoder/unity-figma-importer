﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.VectorGraphics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cdm.Figma.Utils
{
    public class VectorImageUtils
    {
        public class SpriteOptions
        {
            public VectorUtils.TessellationOptions tessellationOptions { get; set; } =
                new VectorUtils.TessellationOptions()
                {
                    StepDistance = 2.0f,
                    MaxCordDeviation = 0.5f,
                    MaxTanAngleDeviation = 0.1f,
                    SamplingStepSize = 0.01f
                };

            public FilterMode filterMode { get; set; } = FilterMode.Bilinear;
            public TextureWrapMode wrapMode { get; set; } = TextureWrapMode.Clamp;
            public int textureSize { get; set; } = 256;
            public int sampleCount { get; set; } = 4;
            public ushort gradientResolution { get; set; } = 128;
            public float svgPixelsPerUnit { get; set; } = 100f;
            public float pixelsPerUnit { get; set; } = 100f;
        }

        public static Sprite CreateSpriteFromSvg(VectorNode vectorNode, string svg, SpriteOptions options = null)
        {
            options ??= new SpriteOptions();
            var sceneInfo = SVGParser.ImportSVG(
                new StringReader(svg), ViewportOptions.PreserveViewport);
            return CreateSpriteWithTexture(vectorNode, options, sceneInfo.Scene, sceneInfo);
        }

        [Obsolete("Use CreateSpriteFromSvg()")]
        public static Sprite CreateSpriteFromPath(VectorNode vectorNode, SpriteOptions options = null)
        {
            options ??= new SpriteOptions();
            
            var width = vectorNode.size.x;
            var height = vectorNode.size.y;
            var strokeWidth = vectorNode.strokeWeight ?? 0;

            var svgString = new StringBuilder();
            svgString.AppendLine($@"<svg width=""{width}"" height=""{height}"" xmlns=""http://www.w3.org/2000/svg"">");

            for (var i = 0; i < vectorNode.fillGeometry.Length; i++)
            {
                var path = vectorNode.fillGeometry[i].path;
                var windingRule = vectorNode.fillGeometry[i].windingRule;
                svgString.Append($@"<path ");

                if (!string.IsNullOrEmpty(windingRule))
                {
                    svgString.Append($@"fill-rule=""{windingRule.ToLowerInvariant()}"" ");
                    svgString.Append($@"clip-rule=""{windingRule.ToLowerInvariant()}"" ");
                }

                if (!string.IsNullOrEmpty(path))
                {
                    svgString.Append($@"d=""{path}"" ");
                }
                
                if (i < vectorNode.fills.Count && vectorNode.fills[i] is SolidPaint fill)
                {
                    svgString.Append($@"fill=""{fill.color.ToString("rgb-hex")}"" ");
                }

                if (i < vectorNode.strokes.Count && vectorNode.strokes[i] is SolidPaint stroke)
                {
                    svgString.Append($@"stroke=""{stroke.color.ToString("rgb-hex")}"" ");
                    svgString.Append($@"stroke-width=""{strokeWidth}"" ");
                }

                svgString.AppendLine("/>");
            }

            svgString.AppendLine("</svg>");
            
            Debug.Log($"{vectorNode.id}: {svgString}");

            var sceneInfo = SVGParser.ImportSVG(
                new StringReader(svgString.ToString()), ViewportOptions.PreserveViewport);
            return CreateSpriteWithTexture(vectorNode, options, sceneInfo.Scene, sceneInfo);
        }

        private static IFill CreateShapeFill(Paint paint, out float angle)
        {
            IFill fill = null;
            angle = 0f;
            
            if (paint is SolidPaint solidPaint)
            {
                var solidFill = new SolidFill();
                solidFill.Color = solidPaint.color;
                solidFill.Opacity = solidPaint.opacity;
                fill = solidFill;
            }

            if (paint is GradientPaint gradientPaint)
            {
                var gradientFill = new GradientFill();
                gradientFill.Addressing = AddressMode.Clamp;
                gradientFill.Opacity = gradientPaint.opacity;

                var handleStart = (Vector2) gradientPaint.gradientHandlePositions[0];
                var handleEnd = (Vector2) gradientPaint.gradientHandlePositions[1];

                var direction = (handleEnd - handleStart).normalized;
                angle = Vector2.Angle(Vector2.right, direction);
                
                // TODO: gradient handles
                
                // Add gradient stops.
                var gradientStops = new List<GradientStop>();
                foreach (var gs in gradientPaint.gradientStops)
                {
                    var gradientStop = new GradientStop();
                    gradientStop.Color = gs.color;
                    gradientStop.StopPercentage = gs.position;
                    gradientStops.Add(gradientStop);
                }
                gradientFill.Stops = gradientStops.ToArray();
                
                if (gradientPaint is LinearGradientPaint)
                {
                    gradientFill.Type = GradientFillType.Linear;
                } 
                else if (gradientPaint is RadialGradientPaint)
                {
                    gradientFill.Type = GradientFillType.Radial;
                }

                fill = gradientFill;
            }

            
            return fill;
        }

        public static Sprite CreateSpriteFromRect(SceneNode node, SpriteOptions options = null)
        {
            options ??= new SpriteOptions();
            
            var nodeTransform = (INodeTransform)node;
            var nodeFill = (INodeFill)node;
            var nodeRect = (INodeRect)node;

            if (nodeTransform == null || nodeFill == null || nodeRect == null)
            {
                return null;
            }

            var scene = new Scene()
            {
                Root = new Unity.VectorGraphics.SceneNode()
                {
                    Shapes = new List<Shape>(),
                }
            };

            var rect = new Rect(0, 0, nodeTransform.size.x, nodeTransform.size.y);
            var radiusTL = Vector2.one * nodeRect.topLeftRadius;
            var radiusTR = Vector2.one * nodeRect.topRightRadius;
            var radiusBR = Vector2.one * nodeRect.bottomRightRadius;
            var radiusBL = Vector2.one * nodeRect.bottomLeftRadius;
            var rectContour = VectorUtils.BuildRectangleContour(rect, radiusTL, radiusTR, radiusBR, radiusBL);
            
            foreach (var paint in nodeFill.fills)
            {
                var fill = CreateShapeFill(paint, out var angle);
                var shape = new Shape()
                {
                    Contours = new BezierContour[] { rectContour },
                    Fill = fill,
                    FillTransform = Matrix2D.RotateLH(angle * Mathf.Deg2Rad),
                    IsConvex = true
                };
                
                scene.Root.Shapes.Add(shape);
            }
            
            // Add strokes at top of fills.
            var strokeWidth = nodeFill.strokeWeight ?? 0f;
            foreach (var stroke in nodeFill.strokes)
            {
                var fill = CreateShapeFill(stroke, out var angle);
                var shape = new Shape()
                {
                    Contours = new BezierContour[] { rectContour },
                    IsConvex = true,
                    PathProps = new PathProperties()
                    {
                        Stroke = new Stroke()
                        {
                            Fill = fill,
                            FillTransform = Matrix2D.RotateLH(angle * Mathf.Deg2Rad),
                            HalfThickness = strokeWidth * 0.5f,
                            Pattern = nodeFill.strokeDashes
                        }
                    }
                };
                
                scene.Root.Shapes.Add(shape);
            }
            
            /*var hasFill = false;
            var hasStroke = false;

            
            
            var fillColor = new SolidPaint();
            if (nodeBlend.fills.Count > 0)
            {
                hasFill = true;
                fillColor = (SolidPaint)nodeBlend.fills[0];
            }

            var strokeColor = new SolidPaint();
            if (nodeBlend.strokes.Count > 0 && nodeBlend.strokes[0] is SolidPaint)
            {
                hasStroke = true;
                strokeColor = (SolidPaint) nodeBlend.strokes[0];
            }

            var strokeWidth = nodeBlend.strokeWeight ?? 0f;*/

            //var rect = VectorUtils.BuildRectangleContour(
             //   new Rect(0, 0, width, height), radiusTL, radiusTR, radiusBR, radiusBL);
            /*var scene = new Scene()
            {
                Root = new Unity.VectorGraphics.SceneNode()
                {
                    Shapes = new List<Shape>
                    {
                        new Shape()
                        {
                            Contours = new BezierContour[] { rect },
                            Fill = new SolidFill()
                            {
                                Color = hasFill
                                    ? new UnityEngine.Color(fillColor.color.r, fillColor.color.g, fillColor.color.b)
                                    : UnityEngine.Color.clear,
                                Opacity = nodeBlend.opacity,
                                Mode = FillMode.NonZero
                            },

                            PathProps = new PathProperties()
                            {
                                Stroke = new Stroke()
                                {
                                    Color = hasStroke
                                        ? new UnityEngine.Color(strokeColor.color.r, strokeColor.color.g,
                                            strokeColor.color.b)
                                        : UnityEngine.Color.clear,
                                    HalfThickness = strokeWidth
                                }
                            }
                        }
                    }
                }
            };*/

            // Left, bottom, right and top.
            var strokePadding = strokeWidth * 2 + 4;
            var borders = new Vector4(
                Mathf.Max(nodeRect.topLeftRadius, nodeRect.bottomLeftRadius, strokePadding),
                Mathf.Max(nodeRect.bottomLeftRadius, nodeRect.bottomRightRadius, strokePadding),
                Mathf.Max(nodeRect.topRightRadius, nodeRect.bottomRightRadius, strokePadding),
                Mathf.Max(nodeRect.topLeftRadius, nodeRect.topRightRadius, strokePadding)
            );
            
            return CreateSpriteWithTexture(node, options, scene, null, borders);
        }

        private static Sprite CreateSpriteWithTexture(
            SceneNode node, SpriteOptions options, Scene svg, SVGParser.SceneInfo? sceneInfo = null, Vector4? borders = null)
        {
            
            var geometries = VectorUtils.TessellateScene(svg, options.tessellationOptions, sceneInfo?.NodeOpacity);
            var sprite = VectorUtils.BuildSprite(geometries, options.svgPixelsPerUnit, VectorUtils.Alignment.TopLeft, 
                Vector2.zero, options.gradientResolution, true);
            if (sprite == null)
                return null;

            var widthRatio = options.textureSize / sprite.rect.width;
            var heightRatio = options.textureSize / sprite.rect.height;

            var ratio = Mathf.Min(widthRatio, heightRatio);
            
            var width = (int) (sprite.rect.width * ratio);
            var height = (int) (sprite.rect.height * ratio);

            
            var expandEdges = options.filterMode != FilterMode.Point || options.sampleCount > 1;
            var material = new Material(Shader.Find("Unlit/VectorGradient"));
            var texture = 
                VectorUtils.RenderSpriteToTexture2D(sprite, width, height, material, options.sampleCount, expandEdges);

            if (texture != null)
            {
                texture.filterMode = options.filterMode;
                texture.wrapMode = options.wrapMode;    
            }
            
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(material);

            if (texture == null)
                return null;

            var spriteRect = new Rect(0, 0, texture.width, texture.height);
            var spritePivot = spriteRect.center;

            var pixelsPerUnity = options.pixelsPerUnit * ratio;
            
            Sprite spriteWithTexture = null;
            if (borders.HasValue)
            {
                borders *= ratio;
                spriteWithTexture = Sprite.Create(
                    texture, spriteRect, spritePivot, pixelsPerUnity, 0, SpriteMeshType.FullRect, borders.Value);
            }
            else
            {
                spriteWithTexture = Sprite.Create(texture, spriteRect, spritePivot, pixelsPerUnity, 0);
            }

            spriteWithTexture.name = node.id;

            return spriteWithTexture;
        }
    }
}