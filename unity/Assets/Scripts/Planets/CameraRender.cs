using UnityEngine;
using UnityEngine.Rendering;

namespace PlanetTrader.Visuals
{
    /// <summary>Общее для рендера: отрисовать камеру вне обычного цикла (портреты, снимки в редакторе).</summary>
    public static class CameraRender
    {
        /// <summary>
        /// Сглаживание текстур, в которые рисуют камеры. Должно совпадать с MSAA в Assets/Settings/UniversalRP.asset
        /// (RenderPipelineSetup): иначе URP не может записать итоговый кадр в текстуру и она остаётся чёрной.
        /// </summary>
        public const int Msaa = 4;

        /// <summary>
        /// Отрисовать камеру сейчас, в её текстуру. В URP — запросом к конвейеру (Camera.Render там не для этого),
        /// в Built-in — Camera.Render().
        /// </summary>
        public static void Now(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var request = new RenderPipeline.StandardRequest { destination = camera.targetTexture };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    return;
                }
            }
            camera.Render();
        }
    }
}
