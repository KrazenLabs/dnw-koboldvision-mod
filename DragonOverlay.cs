using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KoboldVision
{
    internal sealed class DragonOverlay : IDisposable
    {
        private const string SurfaceShader = "Universal Render Pipeline/Unlit";
        private const string XRayShader = "Hidden/Internal-Colored";
        // Float a tiny amount over the normal model to avoid z-fighting
        private const float Lift = 0.004f;

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColor = Shader.PropertyToID("_Color");

        public readonly SkinnedMeshRenderer Body;

        private readonly DirtScanner _scanner;
        private readonly GameObject _root;
        private readonly SkinnedMeshRenderer _overlay;
        private readonly Mesh _source;
        private readonly Mesh _mesh;
        private readonly Material _surfaceMaterial;
        private readonly Material _xrayMaterial;
        private readonly Material[] _surfaceOnly;
        private readonly Material[] _surfaceAndXRay;
        private readonly Vector2[] _uvs;
        private readonly Color32[] _colors;
        private int _xrayVersion = -1;
        private bool _xrayShown;
        private bool _visible = true;

        private DragonOverlay(SkinnedMeshRenderer body, DirtScanner scanner, Shader surfaceShader, Shader xrayShader)
        {
            Body = body;
            _scanner = scanner;
            var source = _source = body.sharedMesh;
            _surfaceMaterial = CreateSurfaceMaterial(surfaceShader, scanner.Spots);
            _surfaceOnly = Repeat(_surfaceMaterial, source.subMeshCount, null, 0);

            if (source.isReadable)
            {
                _uvs = source.uv;
                bool xray = xrayShader != null && _uvs.Length == source.vertexCount;
                _mesh = CreateLiftedMesh(source, Lift / MeshScale(body), xray);
                if (xray)
                {
                    _colors = new Color32[source.vertexCount];
                    _xrayMaterial = CreateXRayMaterial(xrayShader);
                    _surfaceAndXRay = Repeat(_surfaceMaterial, source.subMeshCount, _xrayMaterial, source.subMeshCount);
                }
            }
            _root = new GameObject("Kobold Vision");
            _overlay = CreateRenderer(_mesh != null ? _mesh : source);
        }

        public bool IsStale
        {
            get { return Body == null || _root == null || _overlay == null || _source != Body.sharedMesh; }
        }

        public static DragonOverlay Create(SkinnedMeshRenderer body, DirtScanner scanner)
        {
            var surfaceShader = Shader.Find(SurfaceShader);
            if (surfaceShader == null || !surfaceShader.isSupported) return null;
            var xrayShader = Shader.Find(XRayShader);
            return new DragonOverlay(body, scanner, surfaceShader, xrayShader != null && xrayShader.isSupported ? xrayShader : null);
        }

        // Coverage
        public void Show(float coverage, float glow, float xray)
        {
            SetVisible(Body.enabled && Body.gameObject.activeInHierarchy && !Body.forceRenderingOff);
            if (!_visible) return;

            _overlay.localBounds = Body.localBounds;
            for (int i = 0, count = Body.sharedMesh.blendShapeCount; i < count; i++) _overlay.SetBlendShapeWeight(i, Body.GetBlendShapeWeight(i));
            _surfaceMaterial.SetVector(BaseColor, new Vector4(glow, glow, glow, coverage));

            bool showXRay = xray > 0.001f && _xrayMaterial != null;
            if (showXRay != _xrayShown)
            {
                _xrayShown = showXRay;
                _overlay.sharedMaterials = showXRay ? _surfaceAndXRay : _surfaceOnly;
            }
            if (!showXRay) return;
            if (_xrayVersion != _scanner.Version) PaintXRay();
            _xrayMaterial.SetVector(TintColor, new Vector4(glow * xray, glow * xray, glow * xray, xray));
        }

        public void Hide()
        {
            SetVisible(false);
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
            if (_mesh != null) Object.Destroy(_mesh);
            Object.Destroy(_surfaceMaterial);
            if (_xrayMaterial != null) Object.Destroy(_xrayMaterial);
        }

        private void SetVisible(bool visible)
        {
            if (visible == _visible) return;
            _visible = visible;
            _root.SetActive(visible);
        }

        private void PaintXRay()
        {
            _xrayVersion = _scanner.Version;
            var coarse = _scanner.Coarse;
            int size = DirtScanner.CoarseResolution;
            for (int i = 0; i < _uvs.Length; i++)
            {
                int x = Mathf.Clamp((int)(_uvs[i].x * size), 0, size - 1);
                int y = Mathf.Clamp((int)(_uvs[i].y * size), 0, size - 1);
                _colors[i] = coarse[y * size + x];
            }
            _mesh.colors32 = _colors;
        }

        private SkinnedMeshRenderer CreateRenderer(Mesh mesh)
        {
            _root.layer = Body.gameObject.layer;
            var renderer = _root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.rootBone = Body.rootBone;
            renderer.bones = Body.bones;
            renderer.quality = Body.quality;
            renderer.updateWhenOffscreen = Body.updateWhenOffscreen;
            renderer.localBounds = Body.localBounds;
            renderer.renderingLayerMask = Body.renderingLayerMask;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.skinnedMotionVectors = false;
            renderer.sharedMaterials = _surfaceOnly;
            return renderer;
        }

        private static Material CreateSurfaceMaterial(Shader shader, Texture spots)
        {
            var material = new Material(shader) { name = "Kobold Vision Surface" };
            material.SetTexture(BaseMap, spots);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormalsOnly", false);
            material.SetShaderPassEnabled("MotionVectors", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Material CreateXRayMaterial(Shader shader)
        {
            var material = new Material(shader) { name = "Kobold Vision X-Ray" };
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_ZTest", (float)CompareFunction.Greater);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.renderQueue = (int)RenderQueue.Transparent + 100;
            return material;
        }

        private static Mesh CreateLiftedMesh(Mesh source, float lift, bool xraySubmeshes)
        {
            var mesh = Object.Instantiate(source);
            mesh.name = source.name + " (Kobold Vision)";
            var vertices = source.vertices;
            var normals = source.normals;
            if (normals.Length == vertices.Length)
            {
                for (int i = 0; i < vertices.Length; i++) vertices[i] += normals[i].normalized * lift;
                mesh.vertices = vertices;
            }
            mesh.colors32 = new Color32[vertices.Length];
            if (xraySubmeshes)
            {
                int count = source.subMeshCount;
                mesh.subMeshCount = count * 2;
                for (int i = 0; i < count; i++) mesh.SetIndices(source.GetIndices(i), source.GetTopology(i), count + i, false);
            }
            mesh.bounds = source.bounds;
            return mesh;
        }

        private static float MeshScale(SkinnedMeshRenderer body)
        {
            var bones = body.bones;
            var bindposes = body.sharedMesh.bindposes;
            var scales = new List<float>(bones.Length);
            for (int i = 0; i < bones.Length && i < bindposes.Length; i++)
            {
                if (bones[i] == null) continue;
                Vector3 scale = (bones[i].localToWorldMatrix * bindposes[i]).lossyScale;
                scales.Add((Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);
            }
            if (scales.Count == 0) return 1f;
            scales.Sort();
            float median = scales[scales.Count / 2];
            return median > 1e-6f ? median : 1f;
        }

        private static Material[] Repeat(Material first, int firstCount, Material second, int secondCount)
        {
            var materials = new Material[firstCount + secondCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = i < firstCount ? first : second;
            return materials;
        }
    }
}
