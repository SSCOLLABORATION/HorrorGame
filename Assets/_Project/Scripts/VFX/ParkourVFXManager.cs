using System.Collections.Generic;
using UnityEngine;
using VectorParkour;

namespace Vector3D.VFX
{
    /// <summary>
    /// Zero-GC, authoritative environmental particle and surface reaction system for 
    /// 3rd-person parkour gameplay.
    /// Provides physical tactile feedback for rooftop landings, continuous ground sliding,
    /// wall-run sparks and scrape dust, wall-jump disengages, vault hurdle clearances,
    /// obstacle collisions, and sprint velocity trails.
    /// Fully integrated with URP 17.3.0 and Unity's Particle System module.
    /// </summary>
    public class ParkourVFXManager : MonoBehaviour
    {
        private static ParkourVFXManager _instance;
        public static ParkourVFXManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Object.FindFirstObjectByType<ParkourVFXManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[ParkourVFXManager]");
                        _instance = go.AddComponent<ParkourVFXManager>();
                        if (Application.isPlaying)
                        {
                            DontDestroyOnLoad(go);
                        }
                    }
                }
                return _instance;
            }
        }

        [Header("Surface Dust Settings")]
        [SerializeField] private Color concreteDustColor = new Color(0.86f, 0.85f, 0.83f, 0.40f);
        [SerializeField] private float landingMinParticles = 8;
        [SerializeField] private float landingMaxParticles = 36;

        [Header("Wall-Run Sparks Settings")]
        [SerializeField] private Color wallSparksColor = new Color(1.0f, 0.75f, 0.22f, 1.0f);
        [SerializeField] private float wallSparksRate = 38f;

        [Header("Slide Friction Settings")]
        [SerializeField] private float slideDustRate = 28f;

        // Shared Procedural Textures & Materials
        private Texture2D softDustTexture;
        private Texture2D sparkTexture;
        private Material matDust;
        private Material matSparks;
        private Material matSprint;
        private bool isInitialized = false;

        // Object-Pooled Emitters (Zero runtime GC allocations)
        private const int LandingPoolSize = 4;
        private ParticleSystem[] landingPool;
        private int landingPoolIndex = 0;

        private ParticleSystem slideDustSystem;
        private Transform activeSlideTarget;
        private bool isSlideActive = false;

        private ParticleSystem wallSparksSystem;
        private ParticleSystem wallDustSystem;
        private Transform activeWallRunTarget;
        private int activeWallSide = 0;
        private bool isWallRunActive = false;

        private ParticleSystem wallKickSystem;
        private ParticleSystem vaultPuffSystem;
        private ParticleSystem vaultSparkleSystem;
        private ParticleSystem stumbleDebrisSystem;
        private ParticleSystem sprintAeroSystem;
        private Transform activeSprintTarget;
        private bool isSprintActive = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (isInitialized) return;

            BuildProceduralAssets();
            BuildLandingPool();
            BuildSlideEmitter();
            BuildWallRunEmitters();
            BuildImpulseEmitters();
            BuildSprintEmitter();

            isInitialized = true;
        }

        private void BuildProceduralAssets()
        {
            Shader urpParticlesUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (urpParticlesUnlit == null)
            {
                urpParticlesUnlit = Shader.Find("Particles/Standard Unlit");
            }
            if (urpParticlesUnlit == null)
            {
                urpParticlesUnlit = Shader.Find("Mobile/Particles/Alpha Blended");
            }

            // 1. Soft Circular Dust Texture (64x64) with quadratic falloff
            int dustRes = 64;
            softDustTexture = new Texture2D(dustRes, dustRes, TextureFormat.RGBA32, false)
            {
                name = "Tex_Parkour_SoftDust",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Vector2 dustCenter = new Vector2(dustRes * 0.5f, dustRes * 0.5f);
            float dustRadius = dustRes * 0.48f;
            for (int y = 0; y < dustRes; y++)
            {
                for (int x = 0; x < dustRes; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), dustCenter) / dustRadius;
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = alpha * alpha; // Smooth organic curve
                    softDustTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            softDustTexture.Apply();

            // 2. Sharp Star/Diamond Spark Texture (32x32)
            int sparkRes = 32;
            sparkTexture = new Texture2D(sparkRes, sparkRes, TextureFormat.RGBA32, false)
            {
                name = "Tex_Parkour_SharpSpark",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Vector2 sparkCenter = new Vector2(sparkRes * 0.5f, sparkRes * 0.5f);
            float sparkRadius = sparkRes * 0.46f;
            for (int y = 0; y < sparkRes; y++)
            {
                for (int x = 0; x < sparkRes; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) - sparkCenter.x) / sparkRadius;
                    float dy = Mathf.Abs((y + 0.5f) - sparkCenter.y) / sparkRadius;
                    float diamondDist = dx + dy;
                    float alpha = Mathf.Clamp01(1f - diamondDist);
                    alpha = Mathf.Pow(alpha, 1.8f);
                    sparkTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            sparkTexture.Apply();

            // 3. Materials
            matDust = new Material(urpParticlesUnlit) { name = "Mat_Parkour_Dust" };
            matDust.mainTexture = softDustTexture;
            matDust.SetFloat("_Surface", 1); // Transparent
            matDust.SetFloat("_Blend", 0);   // Alpha
            matDust.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            matDust.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            matDust.SetInt("_ZWrite", 0);
            matDust.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            matSparks = new Material(urpParticlesUnlit) { name = "Mat_Parkour_Sparks" };
            matSparks.mainTexture = sparkTexture;
            matSparks.SetFloat("_Surface", 1); // Transparent
            matSparks.SetFloat("_Blend", 1);   // Additive
            matSparks.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            matSparks.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            matSparks.SetInt("_ZWrite", 0);
            matSparks.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            matSprint = new Material(urpParticlesUnlit) { name = "Mat_Parkour_Sprint" };
            matSprint.mainTexture = softDustTexture;
            matSprint.SetFloat("_Surface", 1);
            matSprint.SetFloat("_Blend", 1); // Additive
            matSprint.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            matSprint.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            matSprint.SetInt("_ZWrite", 0);
            matSprint.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private void BuildLandingPool()
        {
            landingPool = new ParticleSystem[LandingPoolSize];
            for (int i = 0; i < LandingPoolSize; i++)
            {
                var go = new GameObject($"LandingDust_{i}");
                go.transform.SetParent(transform);

                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.45f);
                main.startColor = concreteDustColor;
                main.gravityModifier = 0.08f;
                main.maxParticles = 64;

                var em = ps.emission;
                em.enabled = true;
                em.rateOverTime = 0f;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.35f;
                shape.rotation = new Vector3(90f, 0f, 0f);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.9f, 0.9f), 1f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0.35f, 0.4f), new GradientAlphaKey(0f, 1f) }
                );
                col.color = new ParticleSystem.MinMaxGradient(grad);

                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                var curve = new AnimationCurve();
                curve.AddKey(0f, 0.4f);
                curve.AddKey(1f, 1.35f);
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

                var rend = go.GetComponent<ParticleSystemRenderer>();
                rend.material = matDust;
                rend.renderMode = ParticleSystemRenderMode.Billboard;

                landingPool[i] = ps;
            }
        }

        private void BuildSlideEmitter()
        {
            var go = new GameObject("SlideDustEmitter");
            go.transform.SetParent(transform);

            slideDustSystem = go.AddComponent<ParticleSystem>();
            var main = slideDustSystem.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
            main.startColor = new Color(0.85f, 0.84f, 0.82f, 0.35f);
            main.gravityModifier = -0.02f;
            main.maxParticles = 120;

            var em = slideDustSystem.emission;
            em.enabled = true;
            em.rateOverTime = 0f;

            var shape = slideDustSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.5f, 0.1f, 0.6f);

            var col = slideDustSystem.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = slideDustSystem.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve();
            curve.AddKey(0f, 0.3f);
            curve.AddKey(1f, 1.2f);
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material = matDust;
        }

        private void BuildWallRunEmitters()
        {
            // Wall Sparks Emitter
            var goSparks = new GameObject("WallRunSparksEmitter");
            goSparks.transform.SetParent(transform);

            wallSparksSystem = goSparks.AddComponent<ParticleSystem>();
            var mainSparks = wallSparksSystem.main;
            mainSparks.playOnAwake = false;
            mainSparks.loop = true;
            mainSparks.simulationSpace = ParticleSystemSimulationSpace.World;
            mainSparks.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            mainSparks.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, 9.5f);
            mainSparks.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            mainSparks.startColor = wallSparksColor;
            mainSparks.gravityModifier = 0.95f;
            mainSparks.maxParticles = 200;

            var emSparks = wallSparksSystem.emission;
            emSparks.enabled = true;
            emSparks.rateOverTime = 0f;

            var shapeSparks = wallSparksSystem.shape;
            shapeSparks.enabled = true;
            shapeSparks.shapeType = ParticleSystemShapeType.Cone;
            shapeSparks.angle = 18f;
            shapeSparks.radius = 0.1f;
            shapeSparks.rotation = new Vector3(10f, 180f, 0f); // Spray backward along -Z

            var rendSparks = goSparks.GetComponent<ParticleSystemRenderer>();
            rendSparks.material = matSparks;
            rendSparks.renderMode = ParticleSystemRenderMode.Stretch;
            rendSparks.velocityScale = 0.06f;
            rendSparks.lengthScale = 1.4f;

            // Wall Concrete Scraping Dust Emitter
            var goDust = new GameObject("WallRunDustEmitter");
            goDust.transform.SetParent(transform);

            wallDustSystem = goDust.AddComponent<ParticleSystem>();
            var mainDust = wallDustSystem.main;
            mainDust.playOnAwake = false;
            mainDust.loop = true;
            mainDust.simulationSpace = ParticleSystemSimulationSpace.World;
            mainDust.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            mainDust.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            mainDust.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.32f);
            mainDust.startColor = new Color(0.88f, 0.88f, 0.85f, 0.35f);
            mainDust.gravityModifier = 0.15f;
            mainDust.maxParticles = 80;

            var emDust = wallDustSystem.emission;
            emDust.enabled = true;
            emDust.rateOverTime = 0f;

            var shapeDust = wallDustSystem.shape;
            shapeDust.enabled = true;
            shapeDust.shapeType = ParticleSystemShapeType.Cone;
            shapeDust.angle = 25f;
            shapeDust.radius = 0.15f;
            shapeDust.rotation = new Vector3(0f, 180f, 0f);

            var colDust = wallDustSystem.colorOverLifetime;
            colDust.enabled = true;
            var gradDust = new Gradient();
            gradDust.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            colDust.color = new ParticleSystem.MinMaxGradient(gradDust);

            var rendDust = goDust.GetComponent<ParticleSystemRenderer>();
            rendDust.material = matDust;
        }

        private void BuildImpulseEmitters()
        {
            // 1. Wall Kick Dust Burst
            var goKick = new GameObject("WallKickEmitter");
            goKick.transform.SetParent(transform);
            wallKickSystem = goKick.AddComponent<ParticleSystem>();
            ConfigureImpulseSystem(wallKickSystem, matDust, new Color(0.88f, 0.88f, 0.85f, 0.45f), 0.3f, 0.5f, 2.5f, 4.5f, 0.2f, 0.4f, 40);

            // 2. Vault Hurdle Clearance Dust
            var goVault = new GameObject("VaultPuffEmitter");
            goVault.transform.SetParent(transform);
            vaultPuffSystem = goVault.AddComponent<ParticleSystem>();
            ConfigureImpulseSystem(vaultPuffSystem, matDust, new Color(0.9f, 0.9f, 0.9f, 0.40f), 0.25f, 0.45f, 1.0f, 2.2f, 0.18f, 0.35f, 30);

            // 3. Vault Perfect Timing Golden Sparkles
            var goSparkle = new GameObject("VaultSparkleEmitter");
            goSparkle.transform.SetParent(transform);
            vaultSparkleSystem = goSparkle.AddComponent<ParticleSystem>();
            ConfigureImpulseSystem(vaultSparkleSystem, matSparks, new Color(1.0f, 0.85f, 0.3f, 0.95f), 0.35f, 0.6f, 1.8f, 3.8f, 0.08f, 0.16f, 30);

            // 4. Stumble Collision Debris
            var goStumble = new GameObject("StumbleDebrisEmitter");
            goStumble.transform.SetParent(transform);
            stumbleDebrisSystem = goStumble.AddComponent<ParticleSystem>();
            ConfigureImpulseSystem(stumbleDebrisSystem, matDust, new Color(0.8f, 0.8f, 0.78f, 0.55f), 0.35f, 0.65f, 2.0f, 5.0f, 0.15f, 0.4f, 50);
        }

        private void ConfigureImpulseSystem(ParticleSystem ps, Material mat, Color col, float lifeMin, float lifeMax, float spdMin, float spdMax, float sizeMin, float sizeMax, int maxP)
        {
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(spdMin, spdMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = col;
            main.gravityModifier = 0.12f;
            main.maxParticles = maxP;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;

            var colLife = ps.colorOverLifetime;
            colLife.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(col.a, 0f), new GradientAlphaKey(0f, 1f) }
            );
            colLife.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve();
            curve.AddKey(0f, 0.5f);
            curve.AddKey(1f, 1.25f);
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = mat;
        }

        private void BuildSprintEmitter()
        {
            var go = new GameObject("SprintAeroEmitter");
            go.transform.SetParent(transform);

            sprintAeroSystem = go.AddComponent<ParticleSystem>();
            var main = sprintAeroSystem.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.30f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
            main.startColor = new Color(0.35f, 0.85f, 1.0f, 0.45f);
            main.gravityModifier = 0f;
            main.maxParticles = 80;

            var em = sprintAeroSystem.emission;
            em.enabled = true;
            em.rateOverTime = 0f;

            var shape = sprintAeroSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.6f, 1.2f, 0.3f);
            shape.rotation = new Vector3(0f, 180f, 0f);

            var col = sprintAeroSystem.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.2f, 0.7f, 1f), 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material = matSprint;
        }

        private void LateUpdate()
        {
            if (isSlideActive && activeSlideTarget != null && slideDustSystem != null)
            {
                slideDustSystem.transform.position = activeSlideTarget.position + Vector3.up * 0.05f - activeSlideTarget.forward * 0.25f;
            }

            if (isWallRunActive && activeWallRunTarget != null)
            {
                Vector3 lateralOffset = (activeWallSide == -1 ? Vector3.left : Vector3.right) * 0.42f;
                Vector3 wallPos = activeWallRunTarget.position + Vector3.up * 0.95f + lateralOffset;

                if (wallSparksSystem != null)
                {
                    wallSparksSystem.transform.position = wallPos;
                    // Angle spark cone slightly outward into the gap away from the wall
                    float yaw = activeWallSide == -1 ? 195f : 165f;
                    wallSparksSystem.transform.rotation = Quaternion.Euler(12f, yaw, 0f);
                }

                if (wallDustSystem != null)
                {
                    wallDustSystem.transform.position = wallPos;
                }
            }

            if (isSprintActive && activeSprintTarget != null && sprintAeroSystem != null)
            {
                sprintAeroSystem.transform.position = activeSprintTarget.position + Vector3.up * 0.9f - activeSprintTarget.forward * 0.35f;
            }
        }

        // =========================================================================
        // PUBLIC API
        // =========================================================================

        /// <summary>
        /// Emits a dynamic ground dust ring / plume scaled by landing impact and fall distance.
        /// </summary>
        public void PlayLandingVFX(Vector3 position, float fallDistance, bool isPlayer = true)
        {
            EnsureInitialized();
            if (landingPool == null || landingPool.Length == 0) return;

            ParticleSystem ps = landingPool[landingPoolIndex];
            landingPoolIndex = (landingPoolIndex + 1) % landingPool.Length;

            ps.transform.position = new Vector3(position.x, position.y + 0.05f, position.z);

            int burstCount = Mathf.Clamp(Mathf.RoundToInt(landingMinParticles + fallDistance * 7.5f), (int)landingMinParticles, (int)landingMaxParticles);
            if (!isPlayer) burstCount = Mathf.Max(6, burstCount / 2); // Chaser dust slightly subdued

            ps.Emit(burstCount);
        }

        /// <summary>
        /// Activates continuous ground slide friction dust stream under character.
        /// </summary>
        public void StartSlideVFX(Transform characterTransform)
        {
            EnsureInitialized();
            activeSlideTarget = characterTransform;
            isSlideActive = true;

            if (slideDustSystem != null)
            {
                slideDustSystem.transform.position = characterTransform.position + Vector3.up * 0.05f;
                var em = slideDustSystem.emission;
                em.rateOverTime = slideDustRate;
                if (!slideDustSystem.isPlaying) slideDustSystem.Play();
            }
        }

        /// <summary>
        /// Halts continuous ground slide dust emission.
        /// </summary>
        public void StopSlideVFX()
        {
            isSlideActive = false;
            activeSlideTarget = null;

            if (slideDustSystem != null)
            {
                var em = slideDustSystem.emission;
                em.rateOverTime = 0f;
            }
        }

        /// <summary>
        /// Activates continuous friction sparks and wall dust scraping along contact surface.
        /// </summary>
        public void StartWallRunVFX(Transform characterTransform, int wallSide, Vector3 contactPoint)
        {
            EnsureInitialized();
            activeWallRunTarget = characterTransform;
            activeWallSide = wallSide;
            isWallRunActive = true;

            if (wallSparksSystem != null)
            {
                wallSparksSystem.transform.position = contactPoint;
                var em = wallSparksSystem.emission;
                em.rateOverTime = wallSparksRate;
                if (!wallSparksSystem.isPlaying) wallSparksSystem.Play();
            }

            if (wallDustSystem != null)
            {
                wallDustSystem.transform.position = contactPoint;
                var em = wallDustSystem.emission;
                em.rateOverTime = 16f;
                if (!wallDustSystem.isPlaying) wallDustSystem.Play();
            }
        }

        /// <summary>
        /// Halts wall-run sparks and dust emissions.
        /// </summary>
        public void StopWallRunVFX()
        {
            isWallRunActive = false;
            activeWallRunTarget = null;
            activeWallSide = 0;

            if (wallSparksSystem != null)
            {
                var em = wallSparksSystem.emission;
                em.rateOverTime = 0f;
            }

            if (wallDustSystem != null)
            {
                var em = wallDustSystem.emission;
                em.rateOverTime = 0f;
            }
        }

        /// <summary>
        /// Emits a directional kick-off dust burst against the wall when jumping or dismounting.
        /// </summary>
        public void PlayWallKickVFX(Vector3 position, Vector3 wallNormal)
        {
            EnsureInitialized();
            if (wallKickSystem == null) return;

            wallKickSystem.transform.position = position + Vector3.up * 0.9f;
            wallKickSystem.transform.rotation = Quaternion.LookRotation(wallNormal.sqrMagnitude > 0.01f ? wallNormal : Vector3.up);
            wallKickSystem.Emit(14);
        }

        /// <summary>
        /// Emits hurdle top-edge dust and golden sparks on vault obstacle clearance.
        /// </summary>
        public void PlayVaultVFX(Vector3 hurdlePos, VaultQuality quality)
        {
            EnsureInitialized();
            if (vaultPuffSystem != null)
            {
                vaultPuffSystem.transform.position = hurdlePos + Vector3.up * 0.05f;
                vaultPuffSystem.Emit(quality == VaultQuality.Perfect ? 18 : 10);
            }

            if (quality == VaultQuality.Perfect && vaultSparkleSystem != null)
            {
                vaultSparkleSystem.transform.position = hurdlePos + Vector3.up * 0.12f;
                vaultSparkleSystem.Emit(16);
            }
        }

        /// <summary>
        /// Emits sharp collision grit and dust burst on obstacle collision / stumble.
        /// </summary>
        public void PlayStumbleVFX(Vector3 obstaclePoint)
        {
            EnsureInitialized();
            if (stumbleDebrisSystem == null) return;

            stumbleDebrisSystem.transform.position = obstaclePoint;
            stumbleDebrisSystem.Emit(22);
        }

        /// <summary>
        /// Enables/disables aerodynamic sprint velocity air ribbons behind the runner.
        /// </summary>
        public void SetSprintVFX(bool active, Transform characterTransform)
        {
            EnsureInitialized();
            isSprintActive = active;
            activeSprintTarget = active ? characterTransform : null;

            if (sprintAeroSystem != null)
            {
                var em = sprintAeroSystem.emission;
                em.rateOverTime = active ? 22f : 0f;
                if (active && !sprintAeroSystem.isPlaying)
                {
                    sprintAeroSystem.transform.position = characterTransform.position + Vector3.up * 0.9f;
                    sprintAeroSystem.Play();
                }
            }
        }

        /// <summary>
        /// Immediately clears active emissions and resets all continuous particle loops.
        /// </summary>
        public void ResetAll()
        {
            StopSlideVFX();
            StopWallRunVFX();
            SetSprintVFX(false, null);

            if (slideDustSystem != null) slideDustSystem.Clear();
            if (wallSparksSystem != null) wallSparksSystem.Clear();
            if (wallDustSystem != null) wallDustSystem.Clear();
            if (sprintAeroSystem != null) sprintAeroSystem.Clear();

            if (landingPool != null)
            {
                for (int i = 0; i < landingPool.Length; i++)
                {
                    if (landingPool[i] != null) landingPool[i].Clear();
                }
            }
        }
    }
}
