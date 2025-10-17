using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class Water_Volume : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private readonly ProfilingSampler _profilingSampler = new ProfilingSampler("Water Volume");

        private RTHandle _source;
        private RTHandle _tempColor;

        public CustomRenderPass(Material mat)
        {
            _material = mat;
            ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
        }

        public void Setup(RTHandle source)
        {
            _source = source;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _tempColor, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_WaterVolumeTempColor");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if(renderingData.cameraData.cameraType != CameraType.Reflection && _source != null)
            {
                CommandBuffer commandBuffer = CommandBufferPool.Get();

                using (new ProfilingScope(commandBuffer, _profilingSampler))
                {
                    Blitter.BlitCameraTexture(commandBuffer, _source, _tempColor, _material, 0);
                    Blitter.BlitCameraTexture(commandBuffer, _tempColor, _source);
                }

                context.ExecuteCommandBuffer(commandBuffer);
                CommandBufferPool.Release(commandBuffer);
            }
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            // Nothing per-frame; RTHandles are reused. Cleanup handled in Dispose.
        }

        public void Dispose()
        {
            _tempColor?.Release();
            _tempColor = null;
        }
    }

    [System.Serializable]
    public class _Settings
    {
        //[HideInInspector]
        public Material material = null;
        public RenderPassEvent renderPass = RenderPassEvent.AfterRenderingSkybox;
    }

    public _Settings settings = new _Settings();

    CustomRenderPass m_ScriptablePass;

    public override void Create()
    {
        if(settings.material == null)
        {
            settings.material = (Material)Resources.Load("Water_Volume");
        }

        m_ScriptablePass = new CustomRenderPass(settings.material);

        // Configures where the render pass should be injected.
        //m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        m_ScriptablePass.renderPassEvent = settings.renderPass;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {       
        m_ScriptablePass.Setup(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(m_ScriptablePass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        m_ScriptablePass?.Dispose();
        m_ScriptablePass = null;
    }
}
