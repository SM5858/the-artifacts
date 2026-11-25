using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>
    /// The player's attack effects, timed by <see cref="PlayerMotion"/>'s clips: a trail from the blade's tip while it
    /// cuts (a thin, quick one for the slashes and Lunge; a wide, bright one that lingers for Overpower's diagonal cut),
    /// and Overpower's aura, sparks rising around her with a soft glow and a light that swell while she gathers herself
    /// and fade after the blow.
    /// </summary>
    public sealed class PlayerEffects
    {
        static readonly Color Core = new Color(1f, 0.97f, 0.9f);
        static readonly Color Blue = new Color(0.55f, 0.78f, 1f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.45f);

        readonly Transform _tip;
        readonly TrailRenderer _trail;
        readonly ParticleSystem _sparks, _glow;
        readonly Light _light;
        bool _cutting;

        /// <param name="feet">Where the aura rises from: the model's root, at her feet.</param>
        public PlayerEffects(Transform feet)
        {
            var dot = SoftDot(64);
            var additive = Additive(dot);

            _tip = new GameObject("Blade Tip").transform;
            _tip.SetParent(feet, false);
            _trail = _tip.gameObject.AddComponent<TrailRenderer>();
            _trail.emitting = false;
            _trail.minVertexDistance = 0.015f;
            _trail.alignment = LineAlignment.View;
            _trail.numCapVertices = 2;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.material = Additive(null);
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

            _sparks = Particles("Aura Sparks", feet, additive, local: false);
            {
                var main = _sparks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
                main.startColor = new ParticleSystem.MinMaxGradient(Blue, Gold);
                main.maxParticles = 500;
                var shape = _sparks.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.5f;
                shape.radiusThickness = 0.3f;
                shape.rotation = new Vector3(90f, 0f, 0f);
                var velocity = _sparks.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                velocity.y = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
                velocity.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                var fade = _sparks.colorOverLifetime;
                fade.enabled = true;
                fade.color = Fade(1f);
                var size = _sparks.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));
            }

            _glow = Particles("Aura Glow", feet, additive, local: true);
            {
                var main = _glow.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(Blue.r, Blue.g, Blue.b, 0.22f), new Color(Core.r, Core.g, Core.b, 0.16f));
                main.maxParticles = 80;
                var shape = _glow.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.35f, 1.3f, 0.35f);
                shape.position = new Vector3(0f, 0.9f, 0f);
                var fade = _glow.colorOverLifetime;
                fade.enabled = true;
                fade.color = Fade(1f);
            }

            _light = new GameObject("Aura Light").AddComponent<Light>();
            _light.transform.SetParent(feet, false);
            _light.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            _light.type = LightType.Point;
            _light.color = Blue;
            _light.range = 4f;
            _light.intensity = 0f;
            _light.shadows = LightShadows.None;
        }

        /// <summary>Moves the trail's anchor to a point (world space) on <paramref name="parent"/>: the blade's tip.</summary>
        public void AttachBladeTip(Transform parent, Vector3 worldTip)
        {
            _tip.SetParent(parent, true);
            _tip.position = worldTip;
            _trail.Clear();
        }

        public void Reset()
        {
            _cutting = false;
            _trail.emitting = false;
            _trail.Clear();
            _sparks.Clear();
            _glow.Clear();
            _light.intensity = 0f;
        }

        public void Update(PlayerMotion motion)
        {
            var overpower = motion.Current == PlayerMotion.Action.Overpower;
            var cutting = motion.Trail > 0.05f;
            if (cutting && !_cutting)
            {
                // Overpower's cut leaves a wide, bright mark that lingers; the others a quick, thin streak.
                _trail.time = overpower ? 0.65f : 0.2f;
                _trail.widthMultiplier = overpower ? 0.32f : 0.1f;
                _trail.colorGradient = Streak(overpower ? Gold : Blue, overpower ? 1f : 0.8f);
                if (overpower) _sparks.Emit(80);
            }
            _cutting = cutting;
            _trail.emitting = cutting;

            var aura = motion.Aura;
            var sparks = _sparks.emission;
            sparks.rateOverTime = 260f * aura;
            var glow = _glow.emission;
            glow.rateOverTime = 40f * aura;
            _light.intensity = 4f * aura;
        }

        static ParticleSystem Particles(string name, Transform parent, Material material, bool local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static ParticleSystem.MinMaxGradient Fade(float peak)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.2f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>White at the blade, fading through the tint to nothing at the trail's end.</summary>
        static Gradient Streak(Color tint, float alpha)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Core, 0f), new GradientColorKey(tint, 0.35f), new GradientColorKey(tint, 1f) },
                new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha * 0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>An unlit additive material that takes vertex colors (particles, trails).</summary>
        static Material Additive(Texture texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader);
            if (shader.name.Contains("Particles"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 2f);  // additive
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.SetColor("_BaseColor", Color.white);
                if (texture != null) m.SetTexture("_BaseMap", texture);
            }
            else if (texture != null) m.mainTexture = texture;
            return m;
        }

        /// <summary>A round, soft-edged white dot for the particles.</summary>
        static Texture2D SoftDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                var a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(255f * a * a));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
