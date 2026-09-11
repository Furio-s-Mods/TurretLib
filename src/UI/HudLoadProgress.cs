using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Cairo;

namespace TurretLib;

public class HudLoadProgress : HudElement
{
    public const string watched_key = "loadProgress";
    private int textureId = 0;
    private float lastProgress = -1f;

    public override string? ToggleKeyCombinationCode => null;

    public HudLoadProgress(ICoreClientAPI capi) : base(capi)
    {
        TryOpen();
    }

    public override bool ShouldReceiveMouseEvents() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        EntityPlayer? entity = capi.World.Player?.Entity;
        if (entity == null) return;

        bool isSneaking = entity.Controls.Sneak;
        bool isRightClicking = entity.Controls.RightMouseDown;
        float progress = entity.WatchedAttributes?.GetFloat(watched_key, 0f) ?? 0f;

        // Strict visibility guard: hide immediately if not sneaking, not right clicking, or progress is inactive/complete
        if (!isSneaking || !isRightClicking || progress <= 0f || progress >= 1f)
        {
            if (lastProgress > 0f)
            {
                ClearTexture();
                lastProgress = 0f;
            }
            return;
        }

        // Performance guard: Throttle Cairo redrawing to 1% progress increments (~10 updates total)
        if (Math.Abs(progress - lastProgress) >= 0.01f)
        {
            lastProgress = progress;
            RedrawCircle(progress);
        }

        if (textureId != 0)
        {
            float size = 52f;
            float x = (capi.Render.FrameWidth / 2f) - (size / 2f);
            float y = (capi.Render.FrameHeight / 2f) - (size / 2f);

            capi.Render.Render2DTexturePremultipliedAlpha(textureId, x, y, size, size);
        }

        base.OnRenderGUI(deltaTime);
    }

    private void RedrawCircle(float progress)
    {
        ClearTexture();

        int canvasSize = 120;
        using ImageSurface surface = new ImageSurface(Format.Argb32, canvasSize, canvasSize);
        using Context ctx = new Context(surface);

        float centerX = canvasSize / 2f;
        float centerY = canvasSize / 2f;
        float radius = 38f;

        ctx.LineCap = LineCap.Round;

        // Track background
        ctx.LineWidth = 12;
        ctx.SetSourceRGBA(0.12, 0.12, 0.12, 0.4);
        ctx.Arc(centerX, centerY, radius, 0, Math.PI * 2);
        ctx.Stroke();

        // Progress arc
        double startAngle = -Math.PI / 2;
        double endAngle = startAngle + (progress * Math.PI * 2);

        ctx.SetSourceRGBA(0.65, 0.65, 0.65, 0.95);
        ctx.Arc(centerX, centerY, radius, startAngle, endAngle);
        ctx.Stroke();

        textureId = capi.Gui.LoadCairoTexture(surface, true);
    }

    private void ClearTexture()
    {
        if (textureId != 0)
        {
            capi.Gui.DeleteTexture(textureId);
            textureId = 0;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        ClearTexture();
        GC.SuppressFinalize(this);
    }
}