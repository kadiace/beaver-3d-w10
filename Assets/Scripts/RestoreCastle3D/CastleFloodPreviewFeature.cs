using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RestoreCastle3D
{
    public sealed class CastleFloodPreviewFeature : ScriptableRendererFeature
    {
        [Header("Flood preview")]
        [SerializeField] private Material _overlayMaterial;

        private FloodPreviewPass _pass;

        /// <summary>
        /// Inspector의 강조 재질을 사용하여 홍수 미리보기 렌더 패스를 준비한다.
        /// 준비된 패스는 투명한 물이 렌더링된 뒤 파괴 예상 자재를 표시한다.
        /// </summary>
        public override void Create()
        {
            _pass = _overlayMaterial == null ? null : new FloodPreviewPass(_overlayMaterial);
        }

        /// <summary>
        /// 현재 게임 카메라에 활성화된 미리보기 대상이 있으면 전용 패스를 등록한다.
        /// 장면 뷰와 다른 카메라에는 미리보기 표시를 추가하지 않는다.
        /// </summary>
        public override void AddRenderPasses(ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            CameraData camera = renderingData.cameraData;
            if (_pass == null || camera.cameraType != CameraType.Game ||
                camera.renderType != CameraRenderType.Base)
                return;
            if (camera.camera.TryGetComponent(out CastleFloodPreview3D preview) &&
                preview.IsVisible && preview.DrawTargets.Count > 0)
                renderer.EnqueuePass(_pass);
        }

        private sealed class FloodPreviewPass : ScriptableRenderPass
        {
            private const int MASK_SHADER_PASS = 0;
            private const int COMPOSITE_SHADER_PASS = 1;

            private readonly Material _material;

            /// <summary>
            /// 강조 재질을 저장하고 물의 투명 렌더링 뒤에 실행할 시점을 지정한다.
            /// 같은 재질의 마스크 및 합성 패스를 순서대로 사용한다.
            /// </summary>
            public FloodPreviewPass(Material material)
            {
                _material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            /// <summary>
            /// 현재 카메라의 대상 메시를 깊이 가림 없는 마스크에 그리고 화면에 한 번 합성한다.
            /// 프레임별 RenderGraph 텍스처를 사용해 겹친 대상의 색 농도를 일정하게 유지한다.
            /// </summary>
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                CastleFloodPreview3D preview = camera.camera.GetComponent<CastleFloodPreview3D>();
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                RenderTextureDescriptor descriptor = camera.cameraTargetDescriptor;
                descriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(graph,
                    descriptor, "Castle Flood Preview Mask", true, FilterMode.Point);

                using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass<MaskData>(
                    "Castle Flood Preview Mask", out MaskData data))
                {
                    data.Targets = preview.DrawTargets;
                    data.Material = _material;
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                    {
                        foreach (CastleFloodPreview3D.DrawTarget target in pass.Targets)
                        {
                            Renderer renderer = target.Renderer;
                            if (renderer == null || !renderer.enabled ||
                                !renderer.gameObject.activeInHierarchy)
                                continue;

                            for (int i = 0; i < target.SubMeshCount; i++)
                                context.cmd.DrawRenderer(renderer, pass.Material, i, MASK_SHADER_PASS);
                        }
                    });
                }

                using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass<CompositeData>(
                    "Castle Flood Preview Tint", out CompositeData data))
                {
                    data.Mask = mask;
                    data.Material = _material;
                    builder.UseTexture(mask, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                        Blitter.BlitTexture(context.cmd, pass.Mask, new Vector4(1f, 1f, 0f, 0f),
                            pass.Material, COMPOSITE_SHADER_PASS));
                }
            }

            private sealed class MaskData
            {
                public IReadOnlyList<CastleFloodPreview3D.DrawTarget> Targets { get; set; }
                public Material Material { get; set; }
            }

            private sealed class CompositeData
            {
                public TextureHandle Mask { get; set; }
                public Material Material { get; set; }
            }
        }
    }
}
